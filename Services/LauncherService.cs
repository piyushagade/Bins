using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using WinBins.Models;
using WinBins.Views;

namespace WinBins.Services
{
    public class DiscoveredAppEntry
    {
        public string Name { get; set; }
        public string ShortcutPath { get; set; }
        public string Category { get; set; }
    }

    public static class LauncherService
    {
        public static bool LaunchItem(BinItem item, bool runAsAdmin = false)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.TargetPath))
                return false;

            string target = Environment.ExpandEnvironmentVariables(item.TargetPath.Trim());

            try
            {
                if (BinItem.IsWebUrl(target))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = target,
                        UseShellExecute = true
                    });
                    return true;
                }

                if (Directory.Exists(target))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{target}\"",
                        UseShellExecute = true
                    });
                    return true;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                };

                if (!string.IsNullOrWhiteSpace(item.Arguments))
                {
                    psi.Arguments = item.Arguments;
                }

                if (!string.IsNullOrWhiteSpace(item.WorkingDirectory) && Directory.Exists(item.WorkingDirectory))
                {
                    psi.WorkingDirectory = item.WorkingDirectory;
                }
                else if (File.Exists(target))
                {
                    string parentDir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrWhiteSpace(parentDir) && Directory.Exists(parentDir))
                    {
                        psi.WorkingDirectory = parentDir;
                    }
                }

                if (runAsAdmin)
                {
                    psi.Verb = "runas";
                }

                Process.Start(psi);
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User cancelled UAC prompt
                return false;
            }
            catch (Exception ex)
            {
                ContentDialogWindow.ShowMessage(
                    Application.Current?.MainWindow,
                    "Unable to Open Item",
                    $"Could not open \"{item.Name}\":\n{ex.Message}\n\nTarget: {target}");
                return false;
            }
        }

        public static void OpenItemLocation(BinItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.TargetPath))
                return;

            string target = Environment.ExpandEnvironmentVariables(item.TargetPath.Trim());
            try
            {
                if (File.Exists(target) || Directory.Exists(target))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{target}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch
            {
            }
        }

        public static List<DiscoveredAppEntry> DiscoverStartMenuApps()
        {
            var results = new Dictionary<string, DiscoveredAppEntry>(StringComparer.OrdinalIgnoreCase);

            string[] roots =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs)
            };

            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    continue;

                try
                {
                    foreach (string file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                    {
                        string ext = Path.GetExtension(file)?.ToLowerInvariant();
                        if (ext != ".lnk" && ext != ".appref-ms")
                            continue;

                        string name = Path.GetFileNameWithoutExtension(file);
                        if (string.IsNullOrWhiteSpace(name))
                            continue;

                        // Skip common uninstaller / readme links
                        string lower = name.ToLowerInvariant();
                        if (lower.Contains("uninstall") || lower.Contains("readme") || lower.Contains("release notes"))
                            continue;

                        if (!results.ContainsKey(name))
                        {
                            string relDir = Path.GetDirectoryName(file) ?? string.Empty;
                            string category = relDir.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                                ? relDir.Substring(root.Length).Trim(Path.DirectorySeparatorChar)
                                : string.Empty;

                            results[name] = new DiscoveredAppEntry
                            {
                                Name = name,
                                ShortcutPath = file,
                                Category = string.IsNullOrWhiteSpace(category) ? "Apps" : category
                            };
                        }
                    }
                }
                catch
                {
                }
            }

            return results.Values
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }
}
