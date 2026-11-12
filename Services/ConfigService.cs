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

        public string AppDataFolder { get; }
        public string IconsFolder { get; }
        public string ShortcutsFolder { get; }
        public string ConfigFilePath { get; }

        public AppConfig Current { get; private set; }

        public event Action ConfigChanged;

        public ConfigService()
        {
            AppDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinBins");
            IconsFolder = Path.Combine(AppDataFolder, "Icons");
            ShortcutsFolder = Path.Combine(AppDataFolder, "Shortcuts");
            ConfigFilePath = Path.Combine(AppDataFolder, "config.json");

            Directory.CreateDirectory(AppDataFolder);
            Directory.CreateDirectory(IconsFolder);
            Directory.CreateDirectory(ShortcutsFolder);
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

            if (notify)
            {
                ConfigChanged?.Invoke();
            }
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
