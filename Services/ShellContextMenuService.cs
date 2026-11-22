using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Win32;
using WinBins.Models;

namespace WinBins.Services
{
    /// <summary>
    /// Manages the optional per-user Windows File Explorer "Add to Bin -> [Bin Name]" cascading context menu
    /// under HKCU\Software\Classes (no administrator privileges required).
    /// </summary>
    public static class ShellContextMenuService
    {
        private const string VerbKeyName = "WinBinsAdd";
        private static readonly string[] TargetRoots =
        {
            @"Software\Classes\*\shell\" + VerbKeyName,
            @"Software\Classes\Directory\shell\" + VerbKeyName
        };

        public static void SyncExplorerContextMenu(AppConfig config)
        {
            if (config == null || !config.ExplorerContextMenuEnabled || config.Bins == null || config.Bins.Count == 0)
            {
                RemoveExplorerContextMenu();
                return;
            }

            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrWhiteSpace(exePath))
                    return;

                foreach (string rootPath in TargetRoots)
                {
                    try { Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false); } catch { }

                    using (RegistryKey rootKey = Registry.CurrentUser.CreateSubKey(rootPath))
                    {
                        if (rootKey == null)
                            continue;

                        rootKey.SetValue("MUIVerb", "Add to Bin");
                        rootKey.SetValue("Icon", $"\"{exePath}\",0");
                        rootKey.SetValue("SubCommands", string.Empty);

                        using (RegistryKey shellKey = rootKey.CreateSubKey("shell"))
                        {
                            if (shellKey == null)
                                continue;

                            int idx = 0;
                            foreach (BinGroup bin in config.Bins)
                            {
                                if (bin == null || string.IsNullOrWhiteSpace(bin.Id))
                                    continue;

                                string safeKey = $"{idx:D2}_{SanitizeKeyName(bin.Id)}";
                                using (RegistryKey binKey = shellKey.CreateSubKey(safeKey))
                                {
                                    if (binKey == null)
                                        continue;

                                    string label = string.IsNullOrWhiteSpace(bin.Name) ? "Bin" : bin.Name.Replace("&", "&&");
                                    binKey.SetValue("MUIVerb", label);
                                    binKey.SetValue("Icon", $"\"{exePath}\",0");

                                    using (RegistryKey cmdKey = binKey.CreateSubKey("command"))
                                    {
                                        cmdKey?.SetValue(string.Empty, $"\"{exePath}\" --add-path \"{bin.Id}\" \"%1\"");
                                    }
                                }
                                idx++;
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public static void RemoveExplorerContextMenu()
        {
            foreach (string rootPath in TargetRoots)
            {
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(rootPath, throwOnMissingSubKey: false);
                }
                catch
                {
                }
            }
        }

        private static string SanitizeKeyName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return "bin";

            var chars = new List<char>(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    chars.Add(c);
            }
            return chars.Count > 0 ? new string(chars.ToArray()) : "bin";
        }
    }
}
