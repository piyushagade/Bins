using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WinBins.Models;

namespace WinBins.Services
{
    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersion { get; set; }
        public string LatestVersion { get; set; }
        public string ReleaseUrl { get; set; }
        public string ErrorMessage { get; set; }
    }

    public static class BackupSyncService
    {
        public const string GitHubRepoUrl = "https://github.com/piyushagade/Bins";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/piyushagade/Bins/releases/latest";
        private const string TagsApiUrl = "https://api.github.com/repos/piyushagade/Bins/tags";

        public static bool ExportBackupArchive(ConfigService configService, string destinationBinsFilePath, out string error)
        {
            error = null;
            if (configService == null || string.IsNullOrWhiteSpace(destinationBinsFilePath))
            {
                error = "Invalid backup path.";
                return false;
            }

            try
            {
                configService.Save(notify: false);

                string dir = Path.GetDirectoryName(destinationBinsFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(destinationBinsFilePath))
                    File.Delete(destinationBinsFilePath);

                using (FileStream fs = new FileStream(destinationBinsFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    if (File.Exists(configService.ConfigFilePath))
                    {
                        zip.CreateEntryFromFile(configService.ConfigFilePath, "config.json", CompressionLevel.Optimal);
                    }

                    var bundledIconNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Bundle cached favicons and user custom icons stored in IconsFolder
                    if (Directory.Exists(configService.IconsFolder))
                    {
                        foreach (string iconFile in Directory.GetFiles(configService.IconsFolder))
                        {
                            string fileName = Path.GetFileName(iconFile);
                            if (string.IsNullOrEmpty(fileName))
                                continue;

                            // Skip generated bin_<id>.ico caches (rebuilt automatically), keep custom icons & favicons
                            if (fileName.StartsWith("bin_", StringComparison.OrdinalIgnoreCase) &&
                                fileName.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            zip.CreateEntryFromFile(iconFile, "Icons/" + fileName, CompressionLevel.Optimal);
                            bundledIconNames.Add(fileName);
                        }
                    }

                    // Also bundle any external custom image files referenced by CustomIconPath
                    if (configService.Current?.Bins != null)
                    {
                        foreach (BinGroup bin in configService.Current.Bins)
                        {
                            if (!string.IsNullOrWhiteSpace(bin.CustomIconPath) && bin.CustomIconIndex == 0)
                            {
                                TryBundleExternalIcon(zip, bin.CustomIconPath, bundledIconNames);
                            }

                            if (bin.Items == null)
                                continue;

                            foreach (BinItem item in bin.Items)
                            {
                                if (!string.IsNullOrWhiteSpace(item.CustomIconPath) && item.CustomIconIndex == 0)
                                {
                                    TryBundleExternalIcon(zip, item.CustomIconPath, bundledIconNames);
                                }
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void TryBundleExternalIcon(ZipArchive zip, string rawPath, HashSet<string> bundledNames)
        {
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
                string ext = Path.GetExtension(expanded)?.ToLowerInvariant() ?? string.Empty;
                if ((ext == ".png" || ext == ".ico" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp") &&
                    File.Exists(expanded))
                {
                    string fileName = Path.GetFileName(expanded);
                    if (!string.IsNullOrEmpty(fileName) && bundledNames.Add(fileName))
                    {
                        zip.CreateEntryFromFile(expanded, "Icons/" + fileName, CompressionLevel.Optimal);
                    }
                }
            }
            catch
            {
            }
        }

        public static bool ImportBackupArchive(ConfigService configService, string sourceBinsFilePath, out string error)
        {
            error = null;
            if (configService == null || string.IsNullOrWhiteSpace(sourceBinsFilePath) || !File.Exists(sourceBinsFilePath))
            {
                error = "Backup file was not found.";
                return false;
            }

            try
            {
                using (FileStream fs = new FileStream(sourceBinsFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (ZipArchive zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry configEntry = zip.GetEntry("config.json");
                    if (configEntry == null)
                    {
                        error = "This .bins archive does not contain a valid config.json.";
                        return false;
                    }

                    AppConfig parsedConfig;
                    byte[] configBytes;
                    using (Stream entryStream = configEntry.Open())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        entryStream.CopyTo(ms);
                        configBytes = ms.ToArray();
                    }

                    using (MemoryStream parseStream = new MemoryStream(configBytes))
                    {
                        var ser = new DataContractJsonSerializer(typeof(AppConfig));
                        parsedConfig = ser.ReadObject(parseStream) as AppConfig;
                    }

                    if (parsedConfig == null)
                    {
                        error = "Could not parse configuration inside the .bins backup.";
                        return false;
                    }

                    Directory.CreateDirectory(configService.IconsFolder);

                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        if (entry.FullName.StartsWith("Icons/", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrEmpty(entry.Name))
                        {
                            string safeFileName = Path.GetFileName(entry.Name);
                            if (string.IsNullOrEmpty(safeFileName))
                                continue;

                            string destIconPath = Path.Combine(configService.IconsFolder, safeFileName);
                            entry.ExtractToFile(destIconPath, overwrite: true);
                        }
                    }

                    // Remap any missing custom icon paths to extracted IconsFolder files if needed
                    parsedConfig.EnsureDefaults();
                    foreach (BinGroup bin in parsedConfig.Bins)
                    {
                        bin.CustomIconPath = RemapBundledIconIfMissing(bin.CustomIconPath, configService.IconsFolder);
                        if (bin.Items != null)
                        {
                            foreach (BinItem item in bin.Items)
                            {
                                item.CustomIconPath = RemapBundledIconIfMissing(item.CustomIconPath, configService.IconsFolder);
                            }
                        }
                    }

                    configService.CreateAutoBackupSnapshot(force: true);
                    File.WriteAllBytes(configService.ConfigFilePath, configBytes);
                    configService.Load();
                    // Re-save remapped paths & trigger ConfigChanged
                    if (configService.Current != null)
                    {
                        foreach (BinGroup bin in configService.Current.Bins)
                        {
                            bin.CustomIconPath = RemapBundledIconIfMissing(bin.CustomIconPath, configService.IconsFolder);
                            if (bin.Items != null)
                            {
                                foreach (BinItem item in bin.Items)
                                {
                                    item.CustomIconPath = RemapBundledIconIfMissing(item.CustomIconPath, configService.IconsFolder);
                                }
                            }
                        }
                        configService.Save(notify: true);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string RemapBundledIconIfMissing(string customPath, string iconsFolder)
        {
            if (string.IsNullOrWhiteSpace(customPath))
                return customPath;

            string expanded = Environment.ExpandEnvironmentVariables(customPath.Trim().Trim('"'));
            if (File.Exists(expanded))
                return customPath;

            string fileName = Path.GetFileName(expanded);
            if (!string.IsNullOrEmpty(fileName))
            {
                string candidate = Path.Combine(iconsFolder, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return customPath;
        }

        public static Task<UpdateCheckResult> CheckGitHubLatestReleaseAsync()
        {
            return Task.Run(() => CheckGitHubLatestReleaseSync());
        }

        private static UpdateCheckResult CheckGitHubLatestReleaseSync()
        {
            var result = new UpdateCheckResult
            {
                CurrentVersion = AppVersion.Current,
                ReleaseUrl = GitHubRepoUrl + "/releases"
            };

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                string json = DownloadGitHubJson(LatestReleaseApiUrl);
                string tagName = null;
                string htmlUrl = null;

                if (!string.IsNullOrWhiteSpace(json))
                {
                    tagName = ExtractJsonStringField(json, "tag_name");
                    htmlUrl = ExtractJsonStringField(json, "html_url");
                }

                // Fall back to /tags if repository uses git tags without formal GitHub Releases
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    string tagsJson = DownloadGitHubJson(TagsApiUrl);
                    if (!string.IsNullOrWhiteSpace(tagsJson))
                    {
                        tagName = ExtractJsonStringField(tagsJson, "name");
                        if (!string.IsNullOrWhiteSpace(tagName))
                        {
                            htmlUrl = GitHubRepoUrl + "/releases/tag/" + tagName;
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(tagName))
                {
                    result.Success = true;
                    result.LatestVersion = AppVersion.Current;
                    result.IsUpdateAvailable = false;
                    return result;
                }

                string cleanLatest = NormalizeVersionString(tagName);
                result.LatestVersion = cleanLatest;
                if (!string.IsNullOrWhiteSpace(htmlUrl))
                    result.ReleaseUrl = htmlUrl;

                if (Version.TryParse(NormalizeToFourPartVersion(cleanLatest), out Version latestVer) &&
                    Version.TryParse(NormalizeToFourPartVersion(AppVersion.Current), out Version currentVer))
                {
                    result.IsUpdateAvailable = latestVer > currentVer;
                }
                else
                {
                    result.IsUpdateAvailable = !string.Equals(cleanLatest, AppVersion.Current, StringComparison.OrdinalIgnoreCase);
                }

                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private static string DownloadGitHubJson(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.UserAgent = "WinBins/" + AppVersion.Current;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 6000;
                req.ReadWriteTimeout = 6000;

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var stream = resp.GetResponseStream())
                {
                    if (stream == null)
                        return null;
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractJsonStringField(string json, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(fieldName))
                return null;

            Match m = Regex.Match(json, "\"" + Regex.Escape(fieldName) + "\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        private static string NormalizeVersionString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return AppVersion.Current;

            string trimmed = raw.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(1).Trim();

            int dashIdx = trimmed.IndexOf('-');
            if (dashIdx > 0)
                trimmed = trimmed.Substring(0, dashIdx);

            return trimmed;
        }

        private static string NormalizeToFourPartVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v))
                return "0.0.0.0";

            string[] parts = v.Split('.');
            var nums = new List<string>(4);
            for (int i = 0; i < 4; i++)
            {
                if (i < parts.Length && int.TryParse(parts[i], out int n))
                    nums.Add(n.ToString());
                else
                    nums.Add("0");
            }
            return string.Join(".", nums);
        }
    }
}
