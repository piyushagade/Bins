using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinBins.Models;

namespace WinBins.Services
{
    /// <summary>
    /// Privacy-first, zero-PII aggregate usage analytics backed by the lightweight counter API.
    /// Tracks only anonymous aggregate counts (DAU, WAU, installs, version adoption, daily feature usage,
    /// and rate-limited error counts) with local per-machine deduplication and a circuit breaker when the API is unreachable.
    /// </summary>
    public sealed class AnalyticsService : IDisposable
    {
        private const string CounterBaseUrl = "https://app.piyushagade.xyz/counter/bins/";
        private const string StateFileName = "analytics.json";
        private const int RequestTimeoutMs = 3500;
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(60);

        private readonly ConfigService _configService;
        private readonly string _stateFilePath;
        private readonly object _stateLock = new object();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private AnalyticsLocalState _state;
        private Timer _heartbeatTimer;
        private int _consecutiveFailures;
        private DateTime _circuitOpenUntilUtc = DateTime.MinValue;
        private bool _sessionLaunchReported;
        private bool _disposed;

        public AnalyticsService(ConfigService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _stateFilePath = Path.Combine(_configService.BaseAppDataFolder, StateFileName);
            _state = LoadState();
        }

        public bool IsEnabled => !_disposed && (_configService.Current?.SendDiagnosticsAndUsage ?? true);

        public void Start()
        {
            if (_disposed)
                return;

            _heartbeatTimer = new Timer(
                _ => _ = RunHeartbeatAsync(),
                null,
                InitialDelay,
                HeartbeatInterval);
        }

        /// <summary>
        /// Records daily usage of a feature (at most one ping per feature per UTC day per machine).
        /// </summary>
        public void TrackFeature(string featureKey)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(featureKey))
                return;

            string sanitized = SanitizeKeySegment(featureKey);
            if (string.IsNullOrEmpty(sanitized))
                return;

            string today = GetUtcDateKey(DateTime.UtcNow);
            lock (_stateLock)
            {
                if (IsCircuitOpenLocked(DateTime.UtcNow))
                    return;

                if (HasFeatureBeenSentTodayLocked(sanitized, today))
                    return;
            }

            _ = SendSingleFeatureAsync(sanitized, today);
        }

        /// <summary>
        /// Records an unhandled application error (at most one ping per UTC day per machine).
        /// </summary>
        public void TrackError()
        {
            if (!IsEnabled)
                return;

            string today = GetUtcDateKey(DateTime.UtcNow);
            lock (_stateLock)
            {
                if (IsCircuitOpenLocked(DateTime.UtcNow))
                    return;

                if (string.Equals(_state.LastErrorDate, today, StringComparison.Ordinal))
                    return;
            }

            _ = SendErrorPingAsync(today);
        }

        private async Task RunHeartbeatAsync()
        {
            if (!IsEnabled)
                return;

            CancellationToken token = _cts.Token;
            if (token.IsCancellationRequested)
                return;

            bool acquired = false;
            try
            {
                acquired = await _sendGate.WaitAsync(0, token).ConfigureAwait(false);
                if (!acquired)
                    return;

                DateTime nowUtc = DateTime.UtcNow;
                string today = GetUtcDateKey(nowUtc);
                string isoWeek = GetUtcIsoWeekKey(nowUtc);
                string versionKey = "ver-" + SanitizeKeySegment(AppVersion.Current);

                bool needAppLaunch;
                bool needInstall;
                bool needVersion;
                bool needDau;
                bool needWau;
                bool needLiveFolder;

                lock (_stateLock)
                {
                    if (IsCircuitOpenLocked(nowUtc))
                        return;

                    needAppLaunch = !_sessionLaunchReported;
                    needInstall = !_state.InstallReported;
                    needVersion = !string.Equals(_state.LastVersionReported, AppVersion.Current, StringComparison.OrdinalIgnoreCase);
                    needDau = !string.Equals(_state.LastDauDate, today, StringComparison.Ordinal);
                    needWau = !string.Equals(_state.LastWauWeek, isoWeek, StringComparison.Ordinal);
                    needLiveFolder = HasAnyLiveFolderBin() && !HasFeatureBeenSentTodayLocked("livefolder", today);
                }

                bool stateDirty = false;

                if (needAppLaunch && IsEnabled && !token.IsCancellationRequested)
                {
                    if (await TryIncrementCounterAsync("app-launches", token).ConfigureAwait(false))
                    {
                        lock (_stateLock) { _sessionLaunchReported = true; }
                    }
                    else
                    {
                        // Circuit breaker tripped; abort remaining pings for this cycle
                        return;
                    }
                }

                if (needInstall && IsEnabled && !token.IsCancellationRequested)
                {
                    if (await TryIncrementCounterAsync("installs", token).ConfigureAwait(false))
                    {
                        lock (_stateLock) { _state.InstallReported = true; }
                        stateDirty = true;
                    }
                    else
                    {
                        // Circuit breaker tripped; abort remaining pings for this cycle
                        return;
                    }
                }

                if (needVersion && IsEnabled && !token.IsCancellationRequested)
                {
                    if (await TryIncrementCounterAsync(versionKey, token).ConfigureAwait(false))
                    {
                        lock (_stateLock) { _state.LastVersionReported = AppVersion.Current; }
                        stateDirty = true;
                    }
                    else
                    {
                        if (stateDirty) SaveStateSafe();
                        return;
                    }
                }

                if (needDau && IsEnabled && !token.IsCancellationRequested)
                {
                    if (await TryIncrementCounterAsync("dau-" + today, token).ConfigureAwait(false))
                    {
                        lock (_stateLock) { _state.LastDauDate = today; }
                        stateDirty = true;
                    }
                    else
                    {
                        if (stateDirty) SaveStateSafe();
                        return;
                    }
                }

                if (needWau && IsEnabled && !token.IsCancellationRequested)
                {
                    if (await TryIncrementCounterAsync("wau-" + isoWeek, token).ConfigureAwait(false))
                    {
                        lock (_stateLock) { _state.LastWauWeek = isoWeek; }
                        stateDirty = true;
                    }
                    else
                    {
                        if (stateDirty) SaveStateSafe();
                        return;
                    }
                }

                if (needLiveFolder && IsEnabled && !token.IsCancellationRequested)
                {
                    if (await TryIncrementCounterAsync("feat-livefolder", token).ConfigureAwait(false))
                    {
                        lock (_stateLock) { MarkFeatureSentTodayLocked("livefolder", today); }
                        stateDirty = true;
                    }
                }

                if (stateDirty)
                {
                    SaveStateSafe();
                }
            }
            catch
            {
                // Never propagate analytics errors
            }
            finally
            {
                if (acquired)
                {
                    try { _sendGate.Release(); } catch { }
                }
            }
        }

        private async Task SendSingleFeatureAsync(string sanitizedFeature, string today)
        {
            if (!IsEnabled)
                return;

            CancellationToken token = _cts.Token;
            if (token.IsCancellationRequested)
                return;

            bool acquired = false;
            try
            {
                acquired = await _sendGate.WaitAsync(1500, token).ConfigureAwait(false);
                if (!acquired || !IsEnabled)
                    return;

                lock (_stateLock)
                {
                    if (IsCircuitOpenLocked(DateTime.UtcNow) || HasFeatureBeenSentTodayLocked(sanitizedFeature, today))
                        return;
                }

                if (await TryIncrementCounterAsync("feat-" + sanitizedFeature, token).ConfigureAwait(false))
                {
                    lock (_stateLock)
                    {
                        MarkFeatureSentTodayLocked(sanitizedFeature, today);
                    }
                    SaveStateSafe();
                }
            }
            catch
            {
            }
            finally
            {
                if (acquired)
                {
                    try { _sendGate.Release(); } catch { }
                }
            }
        }

        private async Task SendErrorPingAsync(string today)
        {
            if (!IsEnabled)
                return;

            CancellationToken token = _cts.Token;
            if (token.IsCancellationRequested)
                return;

            bool acquired = false;
            try
            {
                acquired = await _sendGate.WaitAsync(1500, token).ConfigureAwait(false);
                if (!acquired || !IsEnabled)
                    return;

                lock (_stateLock)
                {
                    if (IsCircuitOpenLocked(DateTime.UtcNow) ||
                        string.Equals(_state.LastErrorDate, today, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                if (await TryIncrementCounterAsync("errors-" + today, token).ConfigureAwait(false))
                {
                    lock (_stateLock)
                    {
                        _state.LastErrorDate = today;
                    }
                    SaveStateSafe();
                }
            }
            catch
            {
            }
            finally
            {
                if (acquired)
                {
                    try { _sendGate.Release(); } catch { }
                }
            }
        }

        private async Task<bool> TryIncrementCounterAsync(string counterKey, CancellationToken token)
        {
            if (!IsEnabled || token.IsCancellationRequested)
                return false;

            lock (_stateLock)
            {
                if (IsCircuitOpenLocked(DateTime.UtcNow))
                    return false;
            }

            string url = CounterBaseUrl + Uri.EscapeDataString(counterKey) + "/up";

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentLength = 0;
                req.UserAgent = "WinBins/" + AppVersion.Current;
                req.Timeout = RequestTimeoutMs;
                req.ReadWriteTimeout = RequestTimeoutMs;
                req.AllowAutoRedirect = true;

                using (token.Register(() => { try { req.Abort(); } catch { } }))
                using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                {
                    int code = (int)resp.StatusCode;
                    if (code >= 200 && code < 300)
                    {
                        RecordSuccess();
                        return true;
                    }
                }
            }
            catch
            {
                // Swallow all network, DNS, timeout, TLS, and HTTP errors
            }

            RecordFailure();
            return false;
        }

        private void RecordSuccess()
        {
            lock (_stateLock)
            {
                _consecutiveFailures = 0;
                _circuitOpenUntilUtc = DateTime.MinValue;
            }
        }

        private void RecordFailure()
        {
            lock (_stateLock)
            {
                _consecutiveFailures++;
                // Exponential backoff: 15m -> 30m -> 60m (capped at 60m)
                int backoffMinutes = _consecutiveFailures == 1
                    ? 15
                    : (_consecutiveFailures == 2 ? 30 : 60);
                _circuitOpenUntilUtc = DateTime.UtcNow.AddMinutes(backoffMinutes);
            }
        }

        private bool IsCircuitOpenLocked(DateTime nowUtc)
        {
            return nowUtc < _circuitOpenUntilUtc;
        }

        private bool HasAnyLiveFolderBin()
        {
            var bins = _configService.Current?.Bins;
            if (bins == null)
                return false;

            return bins.Any(b => b != null && !string.IsNullOrWhiteSpace(b.LiveFolderPath));
        }

        private bool HasFeatureBeenSentTodayLocked(string featureKey, string today)
        {
            if (_state.FeatureDates == null)
                return false;

            string entry = featureKey + ":" + today;
            return _state.FeatureDates.Contains(entry, StringComparer.OrdinalIgnoreCase);
        }

        private void MarkFeatureSentTodayLocked(string featureKey, string today)
        {
            if (_state.FeatureDates == null)
            {
                _state.FeatureDates = new List<string>();
            }

            // Prune entries from previous days so the list stays tiny
            string suffix = ":" + today;
            _state.FeatureDates.RemoveAll(e => string.IsNullOrWhiteSpace(e) || !e.EndsWith(suffix, StringComparison.Ordinal));

            string entry = featureKey + suffix;
            if (!_state.FeatureDates.Contains(entry, StringComparer.OrdinalIgnoreCase))
            {
                _state.FeatureDates.Add(entry);
            }
        }

        private AnalyticsLocalState LoadState()
        {
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    byte[] raw = File.ReadAllBytes(_stateFilePath);
                    using (var ms = new MemoryStream(raw))
                    {
                        var ser = new DataContractJsonSerializer(typeof(AnalyticsLocalState));
                        if (ser.ReadObject(ms) is AnalyticsLocalState loaded)
                        {
                            loaded.EnsureDefaults();
                            return loaded;
                        }
                    }
                }
            }
            catch
            {
            }

            return new AnalyticsLocalState();
        }

        private void SaveStateSafe()
        {
            try
            {
                string dir = Path.GetDirectoryName(_stateFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                byte[] payload;
                lock (_stateLock)
                {
                    _state.EnsureDefaults();
                    using (var ms = new MemoryStream())
                    using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, true, true, "  "))
                    {
                        var ser = new DataContractJsonSerializer(typeof(AnalyticsLocalState));
                        ser.WriteObject(writer, _state);
                        writer.Flush();
                        payload = ms.ToArray();
                    }
                }

                string tmpPath = _stateFilePath + ".tmp";
                File.WriteAllBytes(tmpPath, payload);
                if (File.Exists(_stateFilePath))
                {
                    File.Delete(_stateFilePath);
                }
                File.Move(tmpPath, _stateFilePath);
            }
            catch
            {
            }
        }

        private static string GetUtcDateKey(DateTime utcNow)
        {
            return utcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns an ISO-8601 week key such as "2026-W47" (Monday-start, first 4-day week).
        /// </summary>
        private static string GetUtcIsoWeekKey(DateTime utcNow)
        {
            DayOfWeek day = CultureInfo.InvariantCulture.Calendar.GetDayOfWeek(utcNow);
            int isoDay = day == DayOfWeek.Sunday ? 7 : (int)day;
            DateTime thursday = utcNow.AddDays(4 - isoDay);
            int weekNum = CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(
                thursday,
                CalendarWeekRule.FirstFourDayWeek,
                DayOfWeek.Monday);

            return string.Format(CultureInfo.InvariantCulture, "{0:0000}-W{1:00}", thursday.Year, weekNum);
        }

        private static string SanitizeKeySegment(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string cleaned = Regex.Replace(raw.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
            return cleaned;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try { _heartbeatTimer?.Dispose(); } catch { }
            _heartbeatTimer = null;
            try { _cts.Cancel(); } catch { }
            try { _cts.Dispose(); } catch { }
            try { _sendGate.Dispose(); } catch { }
        }

        [DataContract]
        private class AnalyticsLocalState
        {
            [DataMember(Order = 1)]
            public bool InstallReported { get; set; }

            [DataMember(Order = 2)]
            public string LastVersionReported { get; set; }

            [DataMember(Order = 3)]
            public string LastDauDate { get; set; }

            [DataMember(Order = 4)]
            public string LastWauWeek { get; set; }

            [DataMember(Order = 5)]
            public string LastErrorDate { get; set; }

            [DataMember(Order = 6)]
            public List<string> FeatureDates { get; set; }

            public AnalyticsLocalState()
            {
                EnsureDefaults();
            }

            public void EnsureDefaults()
            {
                if (LastVersionReported == null) LastVersionReported = string.Empty;
                if (LastDauDate == null) LastDauDate = string.Empty;
                if (LastWauWeek == null) LastWauWeek = string.Empty;
                if (LastErrorDate == null) LastErrorDate = string.Empty;
                if (FeatureDates == null) FeatureDates = new List<string>();
            }
        }
    }
}
