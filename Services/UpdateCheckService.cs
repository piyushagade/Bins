using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinBins.Services
{
    /// <summary>
    /// Periodically checks GitHub Releases in the background (at most once every 24 hours)
    /// and raises an event when a newer version of Bins is available.
    /// </summary>
    public sealed class UpdateCheckService : IDisposable
    {
        private const string StateFileName = "update_state.json";
        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan PollInterval = TimeSpan.FromHours(6);
        private static readonly TimeSpan CheckCooldown = TimeSpan.FromHours(24);
        private static readonly TimeSpan FailureRetryCooldown = TimeSpan.FromHours(6);

        private readonly string _stateFilePath;
        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private UpdateLocalState _state;
        private Timer _timer;
        private DateTime _lastAttemptUtc = DateTime.MinValue;
        private bool _checkInFlight;
        private bool _disposed;

        public UpdateCheckResult LatestAvailableUpdate { get; private set; }

        /// <summary>
        /// Raised when an update is available. The boolean indicates whether a one-time notification
        /// toast/balloon should be shown for this version.
        /// </summary>
        public event Action<UpdateCheckResult, bool> UpdateAvailable;

        public UpdateCheckService(ConfigService configService)
        {
            if (configService == null)
                throw new ArgumentNullException(nameof(configService));

            _stateFilePath = Path.Combine(configService.BaseAppDataFolder, StateFileName);
            _state = LoadState();
        }

        public void Start()
        {
            if (_disposed)
                return;

            _timer = new Timer(
                _ => _ = CheckIfDueAsync(),
                null,
                InitialDelay,
                PollInterval);
        }

        public void RecordManualCheckResult(UpdateCheckResult result)
        {
            if (result == null || !result.Success)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            lock (_lock)
            {
                _state.LastSuccessfulCheckUtc = nowUtc.ToString("O", CultureInfo.InvariantCulture);
                if (result.IsUpdateAvailable && !string.IsNullOrWhiteSpace(result.LatestVersion))
                {
                    LatestAvailableUpdate = result;
                    _state.LastNotifiedVersion = result.LatestVersion;
                }
                else
                {
                    LatestAvailableUpdate = null;
                }
            }

            SaveStateSafe();

            if (result.IsUpdateAvailable)
            {
                UpdateAvailable?.Invoke(result, false);
            }
        }

        private async Task CheckIfDueAsync()
        {
            if (_disposed || _cts.IsCancellationRequested)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            lock (_lock)
            {
                if (_checkInFlight)
                    return;

                if ((nowUtc - _lastAttemptUtc) < FailureRetryCooldown)
                    return;

                if (DateTime.TryParse(
                        _state.LastSuccessfulCheckUtc,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out DateTime lastSuccessUtc))
                {
                    if ((nowUtc - lastSuccessUtc) < CheckCooldown)
                        return;
                }

                _checkInFlight = true;
                _lastAttemptUtc = nowUtc;
            }

            try
            {
                UpdateCheckResult res = await BackupSyncService.CheckGitHubLatestReleaseAsync().ConfigureAwait(false);
                if (_disposed || _cts.IsCancellationRequested || res == null || !res.Success)
                    return;

                bool shouldNotify = false;
                lock (_lock)
                {
                    _state.LastSuccessfulCheckUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

                    if (res.IsUpdateAvailable && !string.IsNullOrWhiteSpace(res.LatestVersion))
                    {
                        LatestAvailableUpdate = res;
                        if (!string.Equals(_state.LastNotifiedVersion, res.LatestVersion, StringComparison.OrdinalIgnoreCase))
                        {
                            _state.LastNotifiedVersion = res.LatestVersion;
                            shouldNotify = true;
                        }
                    }
                    else
                    {
                        LatestAvailableUpdate = null;
                    }
                }

                SaveStateSafe();

                if (res.IsUpdateAvailable)
                {
                    UpdateAvailable?.Invoke(res, shouldNotify);
                }
            }
            catch
            {
                // Fail silently on background update checks
            }
            finally
            {
                lock (_lock)
                {
                    _checkInFlight = false;
                }
            }
        }

        private UpdateLocalState LoadState()
        {
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    byte[] raw = File.ReadAllBytes(_stateFilePath);
                    using (var ms = new MemoryStream(raw))
                    {
                        var ser = new DataContractJsonSerializer(typeof(UpdateLocalState));
                        if (ser.ReadObject(ms) is UpdateLocalState loaded)
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

            return new UpdateLocalState();
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
                lock (_lock)
                {
                    _state.EnsureDefaults();
                    using (var ms = new MemoryStream())
                    using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, true, true, "  "))
                    {
                        var ser = new DataContractJsonSerializer(typeof(UpdateLocalState));
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

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try { _timer?.Dispose(); } catch { }
            _timer = null;
            try { _cts.Cancel(); } catch { }
            try { _cts.Dispose(); } catch { }
        }

        [DataContract]
        private class UpdateLocalState
        {
            [DataMember(Order = 1)]
            public string LastSuccessfulCheckUtc { get; set; }

            [DataMember(Order = 2)]
            public string LastNotifiedVersion { get; set; }

            public UpdateLocalState()
            {
                EnsureDefaults();
            }

            public void EnsureDefaults()
            {
                if (LastSuccessfulCheckUtc == null) LastSuccessfulCheckUtc = string.Empty;
                if (LastNotifiedVersion == null) LastNotifiedVersion = string.Empty;
            }
        }
    }
}
