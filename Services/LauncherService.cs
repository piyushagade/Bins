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
        public string Arguments { get; set; }
        public string Category { get; set; }
        public bool IsPackagedApp { get; set; }
    }

    public static class LauncherService
    {
        public static event Action WorkspaceLaunched;

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

                if (item.Kind == BinItemKind.Snippet || BinItem.IsSnippet(target))
                {
                    string text = item.Arguments ?? string.Empty;
                    try
                    {
                        Clipboard.SetText(text);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                if (item.Kind == BinItemKind.SubBin || BinItem.IsSubBin(target))
                {
                    return false; // Handled inline by BinPopupWindow / Program
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

                case "mute":
                    Win32Interop.SendMediaKey(Win32Interop.VK_VOLUME_MUTE);
                    return true;

                case "playpause":
                    Win32Interop.SendMediaKey(Win32Interop.VK_MEDIA_PLAY_PAUSE);
                    return true;

                case "nexttrack":
                    Win32Interop.SendMediaKey(Win32Interop.VK_MEDIA_NEXT_TRACK);
                    return true;

                case "prevtrack":
                    Win32Interop.SendMediaKey(Win32Interop.VK_MEDIA_PREV_TRACK);
                    return true;

                case "toggletheme":
                    return ToggleWindowsDarkLightTheme();

                case "clearclipboard":
                    try { Clipboard.Clear(); return true; } catch { return false; }

                case "soundsettings":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "ms-settings:sound",
                        UseShellExecute = true
                    });
                    return true;

                default:
                    return false;
            }
        }

        private static bool ToggleWindowsDarkLightTheme()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: true))
                {
                    if (key == null)
                        return false;

                    int current = Convert.ToInt32(key.GetValue("AppsUseLightTheme", 0));
                    int next = current == 0 ? 1 : 0;
                    key.SetValue("AppsUseLightTheme", next, Microsoft.Win32.RegistryValueKind.DWord);
                    key.SetValue("SystemUsesLightTheme", next, Microsoft.Win32.RegistryValueKind.DWord);
                }
                Win32Interop.BroadcastThemeChange();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsItemTargetBroken(BinItem item, AppConfig config = null)
        {
            if (item == null || item.IsLiveFolderItem)
                return false;

            string raw = (item.TargetPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw))
                return true;

            if (item.Kind == BinItemKind.SystemCommand || BinItem.IsSystemCommand(raw) ||
                item.Kind == BinItemKind.WebUrl || BinItem.IsWebUrl(raw) ||
                item.Kind == BinItemKind.PackagedApp || BinItem.IsPackagedApp(raw) ||
                item.Kind == BinItemKind.Snippet || BinItem.IsSnippet(raw))
            {
                return false;
            }

            if (item.Kind == BinItemKind.SubBin || BinItem.IsSubBin(raw))
            {
                if (config?.Bins == null)
                    return false;
                string subId = BinItem.GetSubBinId(raw);
                return !config.Bins.Any(b => string.Equals(b.Id, subId, StringComparison.OrdinalIgnoreCase));
            }

            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(raw.Trim('"'));
                if (File.Exists(expanded) || Directory.Exists(expanded))
                    return false;
                return true;
            }
            catch
            {
                return true;
            }
        }

        public static HashSet<string> FindDuplicateItemIds(BinGroup bin)
        {
            var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (bin?.Items == null || bin.Items.Count < 2)
                return duplicates;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BinItem item in bin.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.TargetPath))
                    continue;

                string key = item.Kind == BinItemKind.Snippet
                    ? "snippet|" + (item.Name ?? string.Empty).Trim() + "|" + (item.Arguments ?? string.Empty).Trim()
                    : Environment.ExpandEnvironmentVariables(item.TargetPath.Trim().Trim('"')).TrimEnd('\\') + "|" + (item.Arguments ?? string.Empty).Trim();

                if (!seen.Add(key))
                {
                    duplicates.Add(item.Id);
                }
            }

            return duplicates;
        }

        private static bool MatchesLiveFolderFilter(string fileName, string filterSpec)
        {
            if (string.IsNullOrWhiteSpace(filterSpec))
                return true;

            string[] patterns = filterSpec.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (patterns.Length == 0)
                return true;

            foreach (string rawPat in patterns)
            {
                string pat = rawPat.Trim();
                if (string.IsNullOrEmpty(pat) || pat == "*" || pat == "*.*")
                    return true;

                // Allow shorthand like ".pdf" or "pdf" as "*.pdf"
                if (!pat.Contains("*") && !pat.Contains("?"))
                {
                    pat = pat.StartsWith(".", StringComparison.Ordinal) ? ("*" + pat) : ("*." + pat);
                }

                string regexPat = "^" + System.Text.RegularExpressions.Regex.Escape(pat)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                if (System.Text.RegularExpressions.Regex.IsMatch(fileName, regexPat, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    return true;
            }

            return false;
        }

        public static List<BinItem> GetLiveFolderItems(BinGroup bin, int? maxOverride = null)
        {
            var list = new List<BinItem>();
            if (bin == null)
                return list;

            bool recentMode = bin.LiveFolderRecentMode;
            string expanded = recentMode
                ? Environment.GetFolderPath(Environment.SpecialFolder.Recent)
                : Environment.ExpandEnvironmentVariables((bin.LiveFolderPath ?? string.Empty).Trim().Trim('"'));

            if (string.IsNullOrWhiteSpace(expanded) || !Directory.Exists(expanded))
                return list;

            int cap = Math.Max(1, Math.Min(48, maxOverride ?? bin.LiveFolderMaxItems));
            string filterSpec = bin.LiveFolderFilter;

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
                        if (recentMode && !n.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                            return false;
                        string matchName = recentMode ? Path.GetFileNameWithoutExtension(n) : n;
                        if (!MatchesLiveFolderFilter(matchName, filterSpec))
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

                var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (FileSystemInfo fsi in entries)
                {
                    if (list.Count >= cap)
                        break;

                    string targetPath = fsi.FullName;
                    string displayName = null;

                    if (recentMode && targetPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!ShortcutService.TryResolveShortcutTarget(targetPath, out string resolved, out _))
                            continue;
                        if (string.IsNullOrWhiteSpace(resolved) || (!File.Exists(resolved) && !Directory.Exists(resolved)))
                            continue;
                        if (!seenTargets.Add(resolved))
                            continue;
                        targetPath = resolved;
                        displayName = Path.GetFileName(resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    }

                    BinItem liveItem = BinItem.FromPath(targetPath);
                    if (!string.IsNullOrWhiteSpace(displayName))
                        liveItem.Name = displayName;
                    liveItem.Id = "live_" + targetPath.GetHashCode().ToString("x8");
                    liveItem.IsLiveFolderItem = true;
                    liveItem.IncludeInOpenAll = false;
                    liveItem.Section = recentMode ? "Recent Files" : "Live Folder";
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

            if (targets.Count > 0)
            {
                try { WorkspaceLaunched?.Invoke(); } catch { }
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

        private static readonly object StartMenuCacheLock = new object();
        private static List<DiscoveredAppEntry> _cachedStartMenuApps;
        private static DateTime _cachedStartMenuAppsUtc = DateTime.MinValue;
        private static readonly TimeSpan StartMenuCacheTtl = TimeSpan.FromSeconds(45);

        public static List<DiscoveredAppEntry> TryGetCachedStartMenuApps()
        {
            lock (StartMenuCacheLock)
            {
                if (_cachedStartMenuApps != null && (DateTime.UtcNow - _cachedStartMenuAppsUtc) < StartMenuCacheTtl)
                {
                    return new List<DiscoveredAppEntry>(_cachedStartMenuApps);
                }
            }
            return null;
        }

        public static List<DiscoveredAppEntry> DiscoverStartMenuApps(bool forceRefresh = false)
        {
            if (!forceRefresh)
            {
                List<DiscoveredAppEntry> cached = TryGetCachedStartMenuApps();
                if (cached != null)
                    return cached;
            }

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

            List<DiscoveredAppEntry> sorted = results.Values
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            lock (StartMenuCacheLock)
            {
                _cachedStartMenuApps = sorted;
                _cachedStartMenuAppsUtc = DateTime.UtcNow;
            }

            return new List<DiscoveredAppEntry>(sorted);
        }

        private static readonly HashSet<string> IgnoredTaskbarProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Bins",
            "ShellExperienceHost",
            "StartMenuExperienceHost",
            "SearchHost",
            "SearchApp",
            "TextInputHost",
            "LockApp",
            "ShellHost",
            "WidgetBoard",
            "Widgets",
            "TaskmgrHost"
        };

        /// <summary>
        /// Discovers apps and folders currently running on or pinned to the Windows 11 taskbar,
        /// excluding Bins itself and Windows shell chrome. Resolves pinned taskbar shortcuts to their
        /// underlying target paths or Store AUMIDs so unpinning from the taskbar later does not break the Bin item.
        /// </summary>
        public static List<DiscoveredAppEntry> DiscoverTaskbarAppsAndFolders()
        {
            var entriesByKey = new Dictionary<string, DiscoveredAppEntry>(StringComparer.OrdinalIgnoreCase);
            var runningKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pinnedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int selfPid = Process.GetCurrentProcess().Id;
            string selfExe = Assembly.GetExecutingAssembly().Location ?? string.Empty;

            // Optional lookup of packaged Store apps (by name) so running ApplicationFrameHost UWP windows resolve cleanly
            Dictionary<string, DiscoveredAppEntry> packagedByName = null;
            void EnsurePackagedLookup()
            {
                if (packagedByName != null)
                    return;
                packagedByName = new Dictionary<string, DiscoveredAppEntry>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var tmp = new Dictionary<string, DiscoveredAppEntry>(StringComparer.OrdinalIgnoreCase);
                    DiscoverPackagedAppsInto(tmp);
                    foreach (var kv in tmp)
                    {
                        if (kv.Value.IsPackagedApp && !packagedByName.ContainsKey(kv.Key))
                        {
                            packagedByName[kv.Key] = kv.Value;
                        }
                    }
                }
                catch
                {
                }
            }

            string NormalizeDedupKey(string path, string args)
            {
                string p = (path ?? string.Empty).Trim().TrimEnd('\\');
                string a = (args ?? string.Empty).Trim();
                return string.IsNullOrEmpty(a) ? p : (p + "|" + a);
            }

            // 1. Open File Explorer folders currently on the taskbar
            object shell = null;
            object shellWindows = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    shell = Activator.CreateInstance(shellType);
                    shellWindows = shellType.InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null);
                    if (shellWindows != null)
                    {
                        Type winsType = shellWindows.GetType();
                        int winCount = Convert.ToInt32(winsType.InvokeMember("Count", BindingFlags.GetProperty, null, shellWindows, null));
                        for (int i = 0; i < winCount; i++)
                        {
                            object sw = null;
                            try
                            {
                                sw = winsType.InvokeMember("Item", BindingFlags.InvokeMethod, null, shellWindows, new object[] { i });
                                if (sw == null)
                                    continue;

                                string locUrl = sw.GetType().InvokeMember("LocationURL", BindingFlags.GetProperty, null, sw, null) as string;
                                if (string.IsNullOrWhiteSpace(locUrl) || !locUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (Uri.TryCreate(locUrl, UriKind.Absolute, out Uri uri))
                                {
                                    string localPath = uri.LocalPath;
                                    if (!string.IsNullOrWhiteSpace(localPath) && Directory.Exists(localPath))
                                    {
                                        string trimmed = localPath.TrimEnd('\\');
                                        string folderName = Path.GetFileName(trimmed);
                                        if (string.IsNullOrWhiteSpace(folderName))
                                            folderName = localPath;

                                        string key = NormalizeDedupKey(localPath, null);
                                        runningKeys.Add(key);
                                        folderKeys.Add(key);
                                        if (!entriesByKey.ContainsKey(key))
                                        {
                                            entriesByKey[key] = new DiscoveredAppEntry
                                            {
                                                Name = folderName,
                                                ShortcutPath = localPath,
                                                Category = "Open Folder on Taskbar • " + localPath
                                            };
                                        }
                                    }
                                }
                            }
                            catch
                            {
                            }
                            finally
                            {
                                if (sw != null && Marshal.IsComObject(sw)) try { Marshal.ReleaseComObject(sw); } catch { }
                            }
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (shellWindows != null && Marshal.IsComObject(shellWindows)) try { Marshal.ReleaseComObject(shellWindows); } catch { }
                if (shell != null && Marshal.IsComObject(shell)) try { Marshal.ReleaseComObject(shell); } catch { }
            }

            // 2. Currently running top-level taskbar windows
            try
            {
                const int GWL_EXSTYLE = -20;
                const long WS_EX_TOOLWINDOW = 0x00000080L;
                const long WS_EX_APPWINDOW = 0x00040000L;
                const uint GW_OWNER = 4;
                const int DWMWA_CLOAKED = 14;

                Win32Interop.EnumWindows((hwnd, _) =>
                {
                    try
                    {
                        if (!Win32Interop.IsWindowVisible(hwnd))
                            return true;

                        long exStyle = Win32Interop.GetWindowLongPtrW(hwnd, GWL_EXSTYLE).ToInt64();
                        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
                            return true;

                        IntPtr owner = Win32Interop.GetWindow(hwnd, GW_OWNER);
                        if (owner != IntPtr.Zero && (exStyle & WS_EX_APPWINDOW) == 0)
                            return true;

                        if (Win32Interop.DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                            return true;

                        int titleLen = Win32Interop.GetWindowTextLengthW(hwnd);
                        if (titleLen <= 0)
                            return true;

                        var titleSb = new System.Text.StringBuilder(titleLen + 1);
                        Win32Interop.GetWindowTextW(hwnd, titleSb, titleSb.Capacity);
                        string title = titleSb.ToString().Trim();
                        if (string.IsNullOrWhiteSpace(title))
                            return true;

                        Win32Interop.GetWindowThreadProcessId(hwnd, out uint pid);
                        if (pid == 0 || (int)pid == selfPid)
                            return true;

                        string cls = Win32Interop.GetWindowClassNameSafe(hwnd);
                        if (string.Equals(cls, "Progman", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(cls, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(cls, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(cls, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(cls, "CabinetWClass", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        if (!Win32Interop.TryGetProcessExecutablePath(pid, out string exePath) ||
                            string.IsNullOrWhiteSpace(exePath))
                            return true;

                        if (string.Equals(exePath, selfExe, StringComparison.OrdinalIgnoreCase))
                            return true;

                        string procName = Path.GetFileNameWithoutExtension(exePath);
                        if (string.IsNullOrWhiteSpace(procName) ||
                            IgnoredTaskbarProcessNames.Contains(procName) ||
                            string.Equals(procName, "explorer", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        // Handle UWP / Microsoft Store windows hosted inside ApplicationFrameHost.exe
                        if (string.Equals(procName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
                        {
                            EnsurePackagedLookup();
                            DiscoveredAppEntry matchedPkg = null;
                            if (packagedByName.TryGetValue(title, out DiscoveredAppEntry exact))
                            {
                                matchedPkg = exact;
                            }
                            else
                            {
                                foreach (var kv in packagedByName)
                                {
                                    if (title.EndsWith(" - " + kv.Key, StringComparison.OrdinalIgnoreCase) ||
                                        title.StartsWith(kv.Key + " - ", StringComparison.OrdinalIgnoreCase) ||
                                        title.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        matchedPkg = kv.Value;
                                        break;
                                    }
                                }
                            }

                            if (matchedPkg != null)
                            {
                                string pkgKey = NormalizeDedupKey(matchedPkg.ShortcutPath, null);
                                runningKeys.Add(pkgKey);
                                if (!entriesByKey.ContainsKey(pkgKey))
                                {
                                    entriesByKey[pkgKey] = new DiscoveredAppEntry
                                    {
                                        Name = matchedPkg.Name,
                                        ShortcutPath = matchedPkg.ShortcutPath,
                                        Category = "Running on Taskbar",
                                        IsPackagedApp = true
                                    };
                                }
                            }
                            return true;
                        }

                        string displayName = GetFriendlyExecutableDisplayName(exePath, procName);
                        string key = NormalizeDedupKey(exePath, null);
                        runningKeys.Add(key);
                        if (!entriesByKey.ContainsKey(key))
                        {
                            entriesByKey[key] = new DiscoveredAppEntry
                            {
                                Name = displayName,
                                ShortcutPath = exePath,
                                Category = "Running on Taskbar"
                            };
                        }
                    }
                    catch
                    {
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
            }

            // 3. Pinned taskbar shortcuts (non-Bins), resolved to persistent targets/AUMIDs
            try
            {
                foreach (string lnkFile in ShortcutService.GetPinnedNonBinTaskbarShortcutFiles())
                {
                    try
                    {
                        string lnkName = Path.GetFileNameWithoutExtension(lnkFile) ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(lnkName))
                            continue;

                        ShortcutService.TryReadShortcutDetails(lnkFile, out string targetPath, out string targetArgs, out string aumid);

                        string resolvedPath = null;
                        string resolvedArgs = string.IsNullOrWhiteSpace(targetArgs) ? null : targetArgs.Trim();
                        bool isPackaged = false;
                        bool isFolder = false;

                        if (!string.IsNullOrWhiteSpace(targetPath) && Directory.Exists(targetPath))
                        {
                            resolvedPath = targetPath;
                            resolvedArgs = null;
                            isFolder = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(aumid) && aumid.Contains("!") &&
                                 (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath)))
                        {
                            resolvedPath = BinItem.ShellAppsPrefix + aumid.Trim();
                            resolvedArgs = null;
                            isPackaged = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
                        {
                            // Handle shortcuts pointing to explorer.exe with a folder argument
                            if (string.Equals(Path.GetFileName(targetPath), "explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(resolvedArgs))
                            {
                                string cleanedArg = resolvedArgs.Trim().Trim('"');
                                if (Directory.Exists(cleanedArg))
                                {
                                    resolvedPath = cleanedArg;
                                    resolvedArgs = null;
                                    isFolder = true;
                                }
                                else
                                {
                                    resolvedPath = targetPath;
                                }
                            }
                            else
                            {
                                resolvedPath = targetPath;
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(aumid) && aumid.Contains("!"))
                        {
                            resolvedPath = BinItem.ShellAppsPrefix + aumid.Trim();
                            resolvedArgs = null;
                            isPackaged = true;
                        }
                        else
                        {
                            resolvedPath = lnkFile;
                            resolvedArgs = null;
                        }

                        if (string.IsNullOrWhiteSpace(resolvedPath))
                            continue;

                        string key = NormalizeDedupKey(resolvedPath, resolvedArgs);
                        pinnedKeys.Add(key);
                        if (isFolder)
                            folderKeys.Add(key);

                        if (entriesByKey.TryGetValue(key, out DiscoveredAppEntry existing))
                        {
                            existing.Name = lnkName;
                        }
                        else
                        {
                            entriesByKey[key] = new DiscoveredAppEntry
                            {
                                Name = lnkName,
                                ShortcutPath = resolvedPath,
                                Arguments = resolvedArgs,
                                Category = isFolder ? ("Pinned Folder on Taskbar • " + resolvedPath) : "Pinned on Taskbar",
                                IsPackagedApp = isPackaged
                            };
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            // 4. Pinned Microsoft Store / UWP apps stored directly in Taskband\Favorites (without a .lnk file)
            try
            {
                EnsurePackagedLookup();
                if (packagedByName != null && packagedByName.Count > 0)
                {
                    var aumidToEntry = new Dictionary<string, DiscoveredAppEntry>(StringComparer.OrdinalIgnoreCase);
                    foreach (DiscoveredAppEntry pkg in packagedByName.Values)
                    {
                        if (pkg.ShortcutPath != null &&
                            pkg.ShortcutPath.StartsWith(BinItem.ShellAppsPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            string aumid = pkg.ShortcutPath.Substring(BinItem.ShellAppsPrefix.Length).Trim();
                            if (!string.IsNullOrWhiteSpace(aumid) && !aumidToEntry.ContainsKey(aumid))
                            {
                                aumidToEntry[aumid] = pkg;
                            }
                        }
                    }

                    HashSet<string> pinnedAumids = ShortcutService.FindPinnedTaskbandAumids(aumidToEntry.Keys);
                    foreach (string aumid in pinnedAumids)
                    {
                        if (aumidToEntry.TryGetValue(aumid, out DiscoveredAppEntry pkg))
                        {
                            string key = NormalizeDedupKey(pkg.ShortcutPath, null);
                            pinnedKeys.Add(key);
                            if (!entriesByKey.ContainsKey(key))
                            {
                                entriesByKey[key] = new DiscoveredAppEntry
                                {
                                    Name = pkg.Name,
                                    ShortcutPath = pkg.ShortcutPath,
                                    Category = "Pinned on Taskbar",
                                    IsPackagedApp = true
                                };
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            // Finalize clean status labels and sort: Running & Pinned -> Running / Open Folders -> Pinned
            var finalList = new List<Tuple<int, DiscoveredAppEntry>>(entriesByKey.Count);
            foreach (var kv in entriesByKey)
            {
                string key = kv.Key;
                DiscoveredAppEntry entry = kv.Value;
                bool isRunning = runningKeys.Contains(key);
                bool isPinned = pinnedKeys.Contains(key);
                bool isFolder = folderKeys.Contains(key);

                int rank;
                if (isRunning && isPinned)
                {
                    entry.Category = isFolder
                        ? ("Open & Pinned Folder • " + entry.ShortcutPath)
                        : "Running & Pinned on Taskbar";
                    rank = 0;
                }
                else if (isRunning)
                {
                    entry.Category = isFolder
                        ? ("Open Folder on Taskbar • " + entry.ShortcutPath)
                        : "Running on Taskbar";
                    rank = isFolder ? 1 : 0;
                }
                else
                {
                    entry.Category = isFolder
                        ? ("Pinned Folder on Taskbar • " + entry.ShortcutPath)
                        : "Pinned on Taskbar";
                    rank = 2;
                }

                finalList.Add(Tuple.Create(rank, entry));
            }

            return finalList
                .OrderBy(t => t.Item1)
                .ThenBy(t => t.Item2.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(t => t.Item2)
                .ToList();
        }

        private static string GetFriendlyExecutableDisplayName(string exePath, string fallbackName)
        {
            try
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(exePath);
                string desc = (fvi.FileDescription ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(desc) && desc.Length <= 48 &&
                    !desc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return desc;
                }

                string prod = (fvi.ProductName ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(prod) && prod.Length <= 48)
                {
                    return prod;
                }
            }
            catch
            {
            }

            return fallbackName;
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
