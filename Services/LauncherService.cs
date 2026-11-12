using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using WinBins.Interop;
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
        public static ProcessWindowStyle MapWindowStyle(BinWindowStyle style)
        {
            switch (style)
            {
                case BinWindowStyle.Maximized: return ProcessWindowStyle.Maximized;
                case BinWindowStyle.Minimized: return ProcessWindowStyle.Minimized;
                default: return ProcessWindowStyle.Normal;
            }
        }

        public static bool LaunchItem(BinItem item, bool runAsAdmin = false)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.TargetPath))
                return false;

            string target = Environment.ExpandEnvironmentVariables(item.TargetPath.Trim());
            bool elevate = runAsAdmin || item.RunAsAdmin;

            try
            {
                if (item.Kind == BinItemKind.SystemCommand || BinItem.IsSystemCommand(target))
                {
                    return ExecuteSystemAction(item, elevate);
                }

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
                        UseShellExecute = true,
                        WindowStyle = MapWindowStyle(item.WindowStyle)
                    });
                    return true;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true,
                    WindowStyle = MapWindowStyle(item.WindowStyle)
                };

                if (!string.IsNullOrWhiteSpace(item.Arguments))
                {
                    psi.Arguments = Environment.ExpandEnvironmentVariables(item.Arguments);
                }

                if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
                {
                    string expandedWorkDir = Environment.ExpandEnvironmentVariables(item.WorkingDirectory.Trim());
                    if (Directory.Exists(expandedWorkDir))
                    {
                        psi.WorkingDirectory = expandedWorkDir;
                    }
                }
                else if (File.Exists(target))
                {
                    string parentDir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrWhiteSpace(parentDir) && Directory.Exists(parentDir))
                    {
                        psi.WorkingDirectory = parentDir;
                    }
                }

                if (elevate)
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

        private static bool ExecuteSystemAction(BinItem item, bool elevate)
        {
            string action = BinItem.GetSystemActionKey(item.TargetPath);
            switch (action)
            {
                case "recyclebin":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "shell:RecycleBinFolder",
                        UseShellExecute = true
                    });
                    return true;

                case "lock":
                    return Win32Interop.LockWorkStation();

                case "sleep":
                    return Win32Interop.SetSuspendState(false, false, false);

                case "restart":
                    if (!ContentDialogWindow.ShowConfirm(
                        Application.Current?.MainWindow,
                        "Restart PC",
                        "Are you sure you want to restart your computer now?",
                        primaryText: "Restart",
                        isDestructive: true))
                    {
                        return false;
                    }
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown.exe",
                        Arguments = "/r /t 0",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    });
                    return true;

                case "shutdown":
                    if (!ContentDialogWindow.ShowConfirm(
                        Application.Current?.MainWindow,
                        "Shut Down PC",
                        "Are you sure you want to shut down your computer now?",
                        primaryText: "Shut Down",
                        isDestructive: true))
                    {
                        return false;
                    }
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown.exe",
                        Arguments = "/s /t 0",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    });
                    return true;

                case "snip":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "ms-screenclip:",
                        UseShellExecute = true
                    });
                    return true;

                case "taskmgr":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "taskmgr.exe",
                        UseShellExecute = true
                    });
                    return true;

                case "settings":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "ms-settings:",
                        UseShellExecute = true
                    });
                    return true;

                case "powershell":
                {
                    string cmd = (item.Arguments ?? string.Empty).Trim();
                    string escaped = cmd.Replace("\"", "\\\"");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = string.IsNullOrWhiteSpace(cmd)
                            ? "-NoLogo"
                            : $"-NoProfile -ExecutionPolicy Bypass -Command \"{escaped}\"",
                        UseShellExecute = true,
                        WindowStyle = MapWindowStyle(item.WindowStyle)
                    };
                    if (!string.IsNullOrWhiteSpace(item.WorkingDirectory))
                    {
                        string wd = Environment.ExpandEnvironmentVariables(item.WorkingDirectory.Trim());
                        if (Directory.Exists(wd))
                            psi.WorkingDirectory = wd;
                    }
                    if (elevate)
                    {
                        psi.Verb = "runas";
                    }
                    Process.Start(psi);
                    return true;
                }

                default:
                    return false;
            }
        }

        public static List<BinItem> GetLiveFolderItems(BinGroup bin, int? maxOverride = null)
        {
            var list = new List<BinItem>();
            if (bin == null || string.IsNullOrWhiteSpace(bin.LiveFolderPath))
                return list;

            string expanded = Environment.ExpandEnvironmentVariables(bin.LiveFolderPath.Trim().Trim('"'));
            if (!Directory.Exists(expanded))
                return list;

            int cap = Math.Max(1, Math.Min(48, maxOverride ?? bin.LiveFolderMaxItems));

            try
            {
                var dirInfo = new DirectoryInfo(expanded);
                var entries = dirInfo.EnumerateFileSystemInfos()
                    .Where(fsi =>
                    {
                        if ((fsi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                            return false;
                        string n = fsi.Name;
                        if (string.Equals(n, "desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(n, "thumbs.db", StringComparison.OrdinalIgnoreCase))
                            return false;
                        return true;
                    });

                switch (bin.LiveFolderSort)
                {
                    case LiveFolderSortOrder.NameAscending:
                        entries = entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
                        break;
                    case LiveFolderSortOrder.TypeThenName:
                        entries = entries
                            .OrderByDescending(e => (e.Attributes & FileAttributes.Directory) != 0)
                            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
                        break;
                    default: // NewestFirst
                        entries = entries.OrderByDescending(e => e.LastWriteTimeUtc);
                        break;
                }

                foreach (FileSystemInfo fsi in entries.Take(cap))
                {
                    BinItem liveItem = BinItem.FromPath(fsi.FullName);
                    liveItem.Id = "live_" + fsi.FullName.GetHashCode().ToString("x8");
                    liveItem.IsLiveFolderItem = true;
                    liveItem.IncludeInOpenAll = false;
                    liveItem.Section = "Live Folder";
                    list.Add(liveItem);
                }
            }
            catch
            {
            }

            return list;
        }

        public static async Task LaunchBinWorkspaceAsync(BinGroup bin, RunningAppsTaskbarManager runningAppsMgr)
        {
            if (bin == null)
                return;

            var targets = (bin.Items ?? new List<BinItem>())
                .Where(it => it != null && it.IncludeInOpenAll && !BinItem.IsSystemCommand(it.TargetPath))
                .ToList();

            if (targets.Count == 0 && !string.IsNullOrWhiteSpace(bin.LiveFolderPath))
            {
                string folder = Environment.ExpandEnvironmentVariables(bin.LiveFolderPath.Trim());
                if (Directory.Exists(folder))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{folder}\"",
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
                return;
            }

            foreach (BinItem item in targets)
            {
                var liveWindows = runningAppsMgr?.GetRunningWindowsForItem(item);
                if (liveWindows != null && liveWindows.Count > 0)
                {
                    Win32Interop.ActivateAndRestoreWindow(liveWindows[0].Hwnd);
                }
                else
                {
                    LaunchItem(item);
                }

                if (item.OpenAllDelayMs > 0)
                {
                    int clampedDelay = Math.Min(15000, item.OpenAllDelayMs);
                    await Task.Delay(clampedDelay).ConfigureAwait(true);
                }
            }
        }

        public static string FormatByteSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int idx = 0;
            while (val >= 1024 && idx < units.Length - 1)
            {
                val /= 1024.0;
                idx++;
            }
            return idx == 0 ? $"{bytes} B" : $"{val:0.#} {units[idx]}";
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
