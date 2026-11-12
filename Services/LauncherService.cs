using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
        public bool IsPackagedApp { get; set; }
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

                if (BinItem.IsPackagedApp(target))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = target,
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

        public static bool LaunchItemWithFiles(BinItem item, IEnumerable<string> droppedFiles)
        {
            if (item == null || droppedFiles == null || string.IsNullOrWhiteSpace(item.TargetPath))
                return false;

            var validFiles = droppedFiles
                .Where(f => !string.IsNullOrWhiteSpace(f) && (File.Exists(f) || Directory.Exists(f)))
                .Select(f => $"\"{f.Trim().Trim('\"')}\"")
                .ToList();

            if (validFiles.Count == 0)
                return false;

            string joinedFiles = string.Join(" ", validFiles);
            string target = Environment.ExpandEnvironmentVariables(item.TargetPath.Trim());

            try
            {
                string launchFile = target;
                string launchArgs = string.IsNullOrWhiteSpace(item.Arguments)
                    ? joinedFiles
                    : (item.Arguments.Trim() + " " + joinedFiles);
                string workDir = item.WorkingDirectory;

                // If item is a .lnk shortcut, resolve the underlying executable so arguments pass cleanly
                if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(target) &&
                    ShortcutService.TryResolveShortcutTarget(target, out string resolvedExe, out string lnkArgs) &&
                    File.Exists(resolvedExe))
                {
                    launchFile = resolvedExe;
                    var argParts = new List<string>(3);
                    if (!string.IsNullOrWhiteSpace(lnkArgs)) argParts.Add(lnkArgs.Trim());
                    if (!string.IsNullOrWhiteSpace(item.Arguments)) argParts.Add(item.Arguments.Trim());
                    argParts.Add(joinedFiles);
                    launchArgs = string.Join(" ", argParts);
                    if (string.IsNullOrWhiteSpace(workDir))
                    {
                        workDir = Path.GetDirectoryName(resolvedExe);
                    }
                }
                else if (BinItem.IsPackagedApp(target))
                {
                    // Packaged UWP/Store app fallback: launch app directly
                    return LaunchItem(item);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = launchFile,
                    Arguments = launchArgs,
                    UseShellExecute = true
                };

                if (!string.IsNullOrWhiteSpace(workDir) && Directory.Exists(workDir))
                {
                    psi.WorkingDirectory = workDir;
                }

                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                ContentDialogWindow.ShowMessage(
                    Application.Current?.MainWindow,
                    "Unable to Open Dropped Files",
                    $"Could not open files with \"{item.Name}\":\n{ex.Message}");
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

            // Also discover modern packaged Microsoft Store & Windows 11 apps via shell:AppsFolder
            DiscoverPackagedAppsInto(results);

            return results.Values
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static void DiscoverPackagedAppsInto(Dictionary<string, DiscoveredAppEntry> results)
        {
            object shell = null;
            object folder = null;
            object items = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                    return;

                shell = Activator.CreateInstance(shellType);
                folder = shellType.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { "shell:AppsFolder" });
                if (folder == null)
                    return;

                items = folder.GetType().InvokeMember("Items", BindingFlags.InvokeMethod, null, folder, null);
                if (items == null)
                    return;

                Type itemsType = items.GetType();
                int count = Convert.ToInt32(itemsType.InvokeMember("Count", BindingFlags.GetProperty, null, items, null));

                for (int i = 0; i < count; i++)
                {
                    object item = null;
                    try
                    {
                        item = itemsType.InvokeMember("Item", BindingFlags.InvokeMethod, null, items, new object[] { i });
                        if (item == null)
                            continue;

                        Type itemType = item.GetType();
                        string name = itemType.InvokeMember("Name", BindingFlags.GetProperty, null, item, null) as string;
                        string path = itemType.InvokeMember("Path", BindingFlags.GetProperty, null, item, null) as string;

                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
                            continue;

                        // Packaged UWP / Store apps have an AUMID containing '!' (e.g. Microsoft.WindowsCalculator_8wekyb3d8bbwe!App)
                        if (!path.Contains("!"))
                            continue;

                        string lowerName = name.ToLowerInvariant();
                        if (lowerName.Contains("uninstall") || lowerName.StartsWith("ms-resource:"))
                            continue;

                        if (!results.ContainsKey(name))
                        {
                            results[name] = new DiscoveredAppEntry
                            {
                                Name = name.Trim(),
                                ShortcutPath = BinItem.ShellAppsPrefix + path.Trim(),
                                Category = "Microsoft Store / Modern App",
                                IsPackagedApp = true
                            };
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        if (item != null && Marshal.IsComObject(item))
                        {
                            try { Marshal.ReleaseComObject(item); } catch { }
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (items != null && Marshal.IsComObject(items)) try { Marshal.ReleaseComObject(items); } catch { }
                if (folder != null && Marshal.IsComObject(folder)) try { Marshal.ReleaseComObject(folder); } catch { }
                if (shell != null && Marshal.IsComObject(shell)) try { Marshal.ReleaseComObject(shell); } catch { }
            }
        }
    }
}
