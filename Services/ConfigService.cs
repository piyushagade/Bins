using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;
using Microsoft.Win32;
using WinBins.Models;

namespace WinBins.Services
{
    public class ConfigService
    {
        private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunRegistryValueName = "WinBins";
        private const string SyncPointerFileName = "sync_folder.txt";
        private const int MaxAutoBackupSnapshots = 5;
        private DateTime _lastAutoBackupUtc = DateTime.MinValue;

        public string BaseAppDataFolder { get; }
        public string AppDataFolder { get; private set; }
        public string IconsFolder { get; private set; }
        public string ShortcutsFolder { get; private set; }
        public string BackupsFolder { get; private set; }
        public string ConfigFilePath { get; private set; }
        public string CustomSyncFolder { get; private set; }

        public bool IsUsingCustomSyncFolder => !string.IsNullOrWhiteSpace(CustomSyncFolder);

        public AppConfig Current { get; private set; }

        public event Action ConfigChanged;

        public ConfigService(string explicitBaseFolder = null)
        {
            BaseAppDataFolder = !string.IsNullOrWhiteSpace(explicitBaseFolder)
                ? explicitBaseFolder
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WinBins");

            Directory.CreateDirectory(BaseAppDataFolder);
            ShortcutsFolder = Path.Combine(BaseAppDataFolder, "Shortcuts");
            Directory.CreateDirectory(ShortcutsFolder);
            BackupsFolder = Path.Combine(BaseAppDataFolder, "backups");
            Directory.CreateDirectory(BackupsFolder);

            ResolveStoragePaths();
        }

        private void ResolveStoragePaths()
        {
            string pointerFile = Path.Combine(BaseAppDataFolder, SyncPointerFileName);
            string customDir = null;
            if (File.Exists(pointerFile))
            {
                try
                {
                    string candidate = Environment.ExpandEnvironmentVariables(File.ReadAllText(pointerFile, Encoding.UTF8).Trim());
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        Directory.CreateDirectory(candidate);
                        if (Directory.Exists(candidate))
                        {
                            customDir = Path.GetFullPath(candidate);
                        }
                    }
                }
                catch
                {
                    customDir = null;
                }
            }

            CustomSyncFolder = customDir;
            AppDataFolder = customDir ?? BaseAppDataFolder;
            IconsFolder = Path.Combine(AppDataFolder, "Icons");
            ConfigFilePath = Path.Combine(AppDataFolder, "config.json");

            Directory.CreateDirectory(AppDataFolder);
            Directory.CreateDirectory(IconsFolder);
        }

        public bool SetSyncFolder(string targetFolder, bool copyCurrentIfTargetEmpty = true)
        {
            if (string.IsNullOrWhiteSpace(targetFolder))
                return false;

            try
            {
                string fullTarget = Path.GetFullPath(Environment.ExpandEnvironmentVariables(targetFolder.Trim()));
                Directory.CreateDirectory(fullTarget);
                string targetIcons = Path.Combine(fullTarget, "Icons");
                Directory.CreateDirectory(targetIcons);

                string targetConfig = Path.Combine(fullTarget, "config.json");
                string oldConfig = ConfigFilePath;
                string oldIcons = IconsFolder;

                if (copyCurrentIfTargetEmpty && !File.Exists(targetConfig) && File.Exists(oldConfig))
                {
                    File.Copy(oldConfig, targetConfig, overwrite: true);
                    if (Directory.Exists(oldIcons))
                    {
                        foreach (string iconFile in Directory.GetFiles(oldIcons))
                        {
                            string destIcon = Path.Combine(targetIcons, Path.GetFileName(iconFile));
                            if (!File.Exists(destIcon))
                            {
                                try { File.Copy(iconFile, destIcon, overwrite: false); } catch { }
                            }
                        }
                    }
                }

                string pointerFile = Path.Combine(BaseAppDataFolder, SyncPointerFileName);
                if (string.Equals(fullTarget.TrimEnd('\\'), BaseAppDataFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(pointerFile))
                        File.Delete(pointerFile);
                }
                else
                {
                    File.WriteAllText(pointerFile, fullTarget, Encoding.UTF8);
                }

                ResolveStoragePaths();
                Load();
                ConfigChanged?.Invoke();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void ResetSyncFolderToDefault()
        {
            try
            {
                string pointerFile = Path.Combine(BaseAppDataFolder, SyncPointerFileName);
                string prevConfig = ConfigFilePath;
                string prevIcons = IconsFolder;

                if (File.Exists(pointerFile))
                {
                    File.Delete(pointerFile);
                }

                ResolveStoragePaths();

                // Copy latest synced config back to local folder if newer or missing
                if (!string.IsNullOrEmpty(prevConfig) &&
                    !string.Equals(prevConfig, ConfigFilePath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(prevConfig))
                {
                    try { File.Copy(prevConfig, ConfigFilePath, overwrite: true); } catch { }
                    if (Directory.Exists(prevIcons))
                    {
                        foreach (string iconFile in Directory.GetFiles(prevIcons))
                        {
                            string destIcon = Path.Combine(IconsFolder, Path.GetFileName(iconFile));
                            try { File.Copy(iconFile, destIcon, overwrite: true); } catch { }
                        }
                    }
                }

                Load();
                ConfigChanged?.Invoke();
            }
            catch
            {
            }
        }

        public AppConfig Load()
        {
            if (File.Exists(ConfigFilePath))
            {
                try
                {
                    byte[] raw = File.ReadAllBytes(ConfigFilePath);
                    using (var ms = new MemoryStream(raw))
                    {
                        var ser = new DataContractJsonSerializer(typeof(AppConfig));
                        var loaded = ser.ReadObject(ms) as AppConfig;
                        if (loaded != null)
                        {
                            loaded.EnsureDefaults();
                            Current = loaded;
                            if (Current.LaunchAtStartup)
                            {
                                ApplyStartupSetting(true);
                            }
                            return Current;
                        }
                    }
                }
                catch
                {
                    // Fall back to default starter configuration if corrupted
                }
            }

            Current = CreateDefaultStarterConfig();
            Save();
            return Current;
        }

        public void Save(bool notify = true)
        {
            if (Current == null)
                return;

            Current.EnsureDefaults();
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    CreateAutoBackupSnapshot(force: false);
                }

                string tempPath = ConfigFilePath + ".tmp";
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, true, true, "  "))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppConfig));
                    ser.WriteObject(writer, Current);
                    writer.Flush();
                }

                if (File.Exists(ConfigFilePath))
                {
                    File.Delete(ConfigFilePath);
                }
                File.Move(tempPath, ConfigFilePath);
            }
            catch
            {
            }

            ApplyStartupSetting(Current.LaunchAtStartup);
            ShellContextMenuService.SyncExplorerContextMenu(Current);

            if (notify)
            {
                ConfigChanged?.Invoke();
            }
        }

        public void CreateAutoBackupSnapshot(bool force = false)
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                    return;

                DateTime nowUtc = DateTime.UtcNow;
                if (!force && (nowUtc - _lastAutoBackupUtc).TotalMinutes < 5)
                    return;

                Directory.CreateDirectory(BackupsFolder);
                string fileName = $"config_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                string destPath = Path.Combine(BackupsFolder, fileName);
                File.Copy(ConfigFilePath, destPath, overwrite: true);
                _lastAutoBackupUtc = nowUtc;

                // Prune older snapshots, keeping the newest MaxAutoBackupSnapshots
                var files = new DirectoryInfo(BackupsFolder).GetFiles("config_*.json");
                Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                for (int i = MaxAutoBackupSnapshots; i < files.Length; i++)
                {
                    try { files[i].Delete(); } catch { }
                }
            }
            catch
            {
            }
        }

        public List<FileInfo> ListAutoBackupSnapshots()
        {
            var result = new List<FileInfo>();
            try
            {
                if (!Directory.Exists(BackupsFolder))
                    return result;

                var files = new DirectoryInfo(BackupsFolder).GetFiles("config_*.json");
                Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                result.AddRange(files);
            }
            catch
            {
            }
            return result;
        }

        public bool RestoreAutoBackupSnapshot(string snapshotFilePath)
        {
            if (string.IsNullOrWhiteSpace(snapshotFilePath) || !File.Exists(snapshotFilePath))
                return false;

            try
            {
                byte[] raw = File.ReadAllBytes(snapshotFilePath);
                using (var ms = new MemoryStream(raw))
                {
                    var ser = new DataContractJsonSerializer(typeof(AppConfig));
                    var parsed = ser.ReadObject(ms) as AppConfig;
                    if (parsed == null)
                        return false;
                }

                CreateAutoBackupSnapshot(force: true);
                File.WriteAllBytes(ConfigFilePath, raw);
                Load();
                Save(notify: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool ApplyProfile(string profileIdOrAll)
        {
            if (Current == null)
                return false;

            if (string.IsNullOrWhiteSpace(profileIdOrAll) ||
                string.Equals(profileIdOrAll, "all", StringComparison.OrdinalIgnoreCase))
            {
                Current.ActiveProfileId = string.Empty;
                foreach (BinGroup bin in Current.Bins)
                {
                    bin.IsEnabledOnTaskbar = true;
                }
                Save(notify: true);
                return true;
            }

            BinProfile profile = Current.Profiles?.Find(p =>
                string.Equals(p.Id, profileIdOrAll, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, profileIdOrAll, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
                return false;

            Current.ActiveProfileId = profile.Id;
            var enabledSet = new HashSet<string>(profile.EnabledBinIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (BinGroup bin in Current.Bins)
            {
                bin.IsEnabledOnTaskbar = enabledSet.Contains(bin.Id);
            }

            Save(notify: true);
            return true;
        }

        public void ApplyStartupSetting(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true))
                {
                    if (key == null)
                        return;

                    if (enabled)
                    {
                        string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        key.SetValue(RunRegistryValueName, $"\"{exePath}\" --background");
                    }
                    else
                    {
                        if (key.GetValue(RunRegistryValueName) != null)
                        {
                            key.DeleteValue(RunRegistryValueName, throwOnMissingValue: false);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static AppConfig CreateDefaultStarterConfig()
        {
            var config = new AppConfig
            {
                ClosePopupOnLaunch = true,
                MinimizeToTrayOnClose = true,
                LaunchAtStartup = false
            };

            var essentialsBin = new BinGroup
            {
                Id = "essentials",
                Name = "Essentials",
                IconMode = BinIconMode.GridMosaic,
                Columns = 4,
                IsEnabledOnTaskbar = true
            };

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string downloads = Path.Combine(userProfile, "Downloads");
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

            TryAddStarterItem(essentialsBin.Items, "File Explorer", Path.Combine(winDir, "explorer.exe"));
            if (Directory.Exists(downloads))
                TryAddStarterItem(essentialsBin.Items, "Downloads", downloads);
            if (Directory.Exists(documents))
                TryAddStarterItem(essentialsBin.Items, "Documents", documents);
            TryAddStarterItem(essentialsBin.Items, "Notepad", Path.Combine(sys32, "notepad.exe"));
            TryAddStarterItem(essentialsBin.Items, "Calculator", Path.Combine(sys32, "calc.exe"));
            TryAddStarterItem(essentialsBin.Items, "Command Prompt", Path.Combine(sys32, "cmd.exe"));
            if (Directory.Exists(desktop))
                TryAddStarterItem(essentialsBin.Items, "Desktop", desktop);

            config.Bins.Add(essentialsBin);
            return config;
        }

        private static void TryAddStarterItem(List<BinItem> list, string name, string path)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                var item = BinItem.FromPath(path);
                item.Name = name;
                list.Add(item);
            }
        }
    }
}
