using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;
using WinBins.Interop;
using WinBins.Models;

namespace WinBins.Services
{
    public static class ShortcutService
    {
        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCoClass
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        private const string TaskbandRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";

        private sealed class PinnedShortcutRecord
        {
            public string LnkPath;
            public string Arguments;
            public string Aumid;
        }

        private static readonly object PinnedCacheLock = new object();
        private static long _cachedPinnedStamp = long.MinValue;
        private static List<PinnedShortcutRecord> _cachedPinnedWinBinsShortcuts;

        public static void InvalidatePinnedCache()
        {
            lock (PinnedCacheLock)
            {
                _cachedPinnedStamp = long.MinValue;
                _cachedPinnedWinBinsShortcuts = null;
            }
        }

        public static string GetUserPinnedTaskbarFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        }

        public static string GetImplicitAppShortcutsFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\ImplicitAppShortcuts");
        }

        public static string GetStartMenuBinsFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "Bins");
        }

        /// <summary>
        /// Reads Windows Explorer's pin change counters (read-only) so pin/unpin transitions on ImplicitAppShortcuts
        /// (where Windows Explorer leaves unpinned .lnk files on disk) can be detected promptly.
        /// </summary>
        public static long GetTaskbandPinStamp()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(TaskbandRegistryPath, writable: false))
                {
                    if (key == null)
                        return 0;

                    int favChanges = Convert.ToInt32(key.GetValue("FavoritesChanges", 0));
                    int removedChanges = Convert.ToInt32(key.GetValue("FavoritesRemovedChanges", 0));
                    if (favChanges == 0 && removedChanges == 0)
                    {
                        byte[] favBytes = key.GetValue("Favorites") as byte[];
                        return favBytes?.Length ?? 0;
                    }

                    return ((long)(uint)favChanges << 32) ^ (uint)removedChanges;
                }
            }
            catch
            {
                return 0;
            }
        }

        private static byte[] ReadTaskbandFavoritesBytes()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(TaskbandRegistryPath, writable: false))
                {
                    return key?.GetValue("Favorites") as byte[];
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool ContainsAsciiOrUtf16IgnoreCase(byte[] blob, string token)
        {
            if (blob == null || blob.Length == 0 || string.IsNullOrEmpty(token))
                return false;

            if (Encoding.ASCII.GetString(blob).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            int evenLen = blob.Length - (blob.Length % 2);
            if (evenLen >= 2 &&
                Encoding.Unicode.GetString(blob, 0, evenLen).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (blob.Length >= 3)
            {
                int oddLen = (blob.Length - 1) - ((blob.Length - 1) % 2);
                if (oddLen >= 2 &&
                    Encoding.Unicode.GetString(blob, 1, oddLen).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static string GetAppUserModelId(BinGroup bin)
        {
            return ResolveActiveAppUserModelId(bin);
        }

        /// <summary>
        /// Returns a deterministic AppUserModelID across restarts.
        /// If the Bin is already pinned on the Windows Taskbar, reuses the exact AppUserModelID stamped on that pinned
        /// shortcut so Windows 11 always keeps the Bin in its exact pinned taskbar position across restarts and icon edits.
        /// </summary>
        public static string ResolveActiveAppUserModelId(BinGroup bin)
        {
            if (bin == null || string.IsNullOrWhiteSpace(bin.Id))
                return "WinBins.Group.default";

            if (TryFindPinnedTaskbarShortcut(bin.Id, out _, out string pinnedAumid) &&
                !string.IsNullOrWhiteSpace(pinnedAumid))
            {
                return pinnedAumid;
            }

            return $"WinBins.Group.{bin.Id}.r{Math.Max(1, bin.IconRevision)}";
        }

        public static bool IsBinPinnedOnTaskbar(BinGroup bin)
        {
            if (bin == null || string.IsNullOrWhiteSpace(bin.Id))
                return false;

            return TryFindPinnedTaskbarShortcut(bin.Id, out _, out _);
        }

        public static bool TryFindPinnedTaskbarShortcut(string binId, out string matchedLnkPath, out string pinnedAumid)
        {
            matchedLnkPath = null;
            pinnedAumid = null;

            List<Tuple<string, string>> matches = FindAllActivePinnedShortcuts(binId);
            if (matches.Count > 0)
            {
                matchedLnkPath = matches[0].Item1;
                pinnedAumid = matches[0].Item2;
                return true;
            }

            return false;
        }

        private static bool ContainsDecodedIgnoreCase(string ascii, string utf16Even, string utf16Odd, string token)
        {
            if (string.IsNullOrEmpty(token))
                return false;
            if (!string.IsNullOrEmpty(ascii) && ascii.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(utf16Even) && utf16Even.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(utf16Odd) && utf16Odd.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private static bool TryReadWinBinsShortcutMetadata(string lnkPath, out string arguments, out string aumid)
        {
            arguments = null;
            aumid = null;
            var link = (IShellLinkW)new ShellLinkCoClass();
            try
            {
                ((IPersistFile)link).Load(lnkPath, 0); // STGM_READ

                var argsSb = new StringBuilder(512);
                link.GetArguments(argsSb, argsSb.Capacity);
                string args = argsSb.ToString() ?? string.Empty;

                string readAumid = null;
                if (link is Win32Interop.IPropertyStore propStore)
                {
                    var key = Win32Interop.PKEY_AppUserModel_ID;
                    if (propStore.GetValue(ref key, out Win32Interop.PropVariant pv) == 0)
                    {
                        try
                        {
                            readAumid = pv.AsString();
                        }
                        finally
                        {
                            pv.Dispose();
                        }
                    }
                }

                bool isWinBins =
                    args.IndexOf("--bin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrWhiteSpace(readAumid) &&
                     readAumid.StartsWith("WinBins.", StringComparison.OrdinalIgnoreCase));

                if (isWinBins)
                {
                    arguments = args;
                    aumid = readAumid;
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }

            return false;
        }

        private static List<PinnedShortcutRecord> GetCachedActiveWinBinsShortcuts()
        {
            long currentStamp = GetTaskbandPinStamp();
            lock (PinnedCacheLock)
            {
                if (_cachedPinnedWinBinsShortcuts != null && _cachedPinnedStamp == currentStamp)
                {
                    return _cachedPinnedWinBinsShortcuts;
                }
            }

            var scanned = new List<PinnedShortcutRecord>();
            byte[] favoritesBytes = ReadTaskbandFavoritesBytes();

            string favAscii = null;
            string favUtf16Even = null;
            string favUtf16Odd = null;
            bool hasFavBlob = favoritesBytes != null && favoritesBytes.Length > 0;
            if (hasFavBlob)
            {
                favAscii = Encoding.ASCII.GetString(favoritesBytes);
                int evenLen = favoritesBytes.Length - (favoritesBytes.Length % 2);
                if (evenLen >= 2)
                    favUtf16Even = Encoding.Unicode.GetString(favoritesBytes, 0, evenLen);
                if (favoritesBytes.Length >= 3)
                {
                    int oddLen = (favoritesBytes.Length - 1) - ((favoritesBytes.Length - 1) % 2);
                    if (oddLen >= 2)
                        favUtf16Odd = Encoding.Unicode.GetString(favoritesBytes, 1, oddLen);
                }
            }

            // 1. Direct pinned shortcuts under User Pinned\TaskBar\
            try
            {
                string pinnedDir = GetUserPinnedTaskbarFolder();
                if (Directory.Exists(pinnedDir))
                {
                    foreach (string file in Directory.GetFiles(pinnedDir, "*.lnk"))
                    {
                        if (TryReadWinBinsShortcutMetadata(file, out string args, out string foundAumid))
                        {
                            bool activeInFavorites =
                                !hasFavBlob ||
                                ContainsDecodedIgnoreCase(favAscii, favUtf16Even, favUtf16Odd, Path.GetFileName(file)) ||
                                (!string.IsNullOrWhiteSpace(foundAumid) && ContainsDecodedIgnoreCase(favAscii, favUtf16Even, favUtf16Odd, foundAumid));

                            if (activeInFavorites)
                            {
                                scanned.Add(new PinnedShortcutRecord
                                {
                                    LnkPath = file,
                                    Arguments = args,
                                    Aumid = foundAumid
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            // 2. Windows 11 custom-AUMID pinned shortcuts under User Pinned\ImplicitAppShortcuts\<hash>\
            try
            {
                string implicitDir = GetImplicitAppShortcutsFolder();
                if (Directory.Exists(implicitDir) && hasFavBlob)
                {
                    foreach (string subDir in Directory.GetDirectories(implicitDir))
                    {
                        string hashFolder = Path.GetFileName(subDir);
                        if (string.IsNullOrWhiteSpace(hashFolder) ||
                            !ContainsDecodedIgnoreCase(favAscii, favUtf16Even, favUtf16Odd, hashFolder))
                        {
                            continue;
                        }

                        foreach (string file in Directory.GetFiles(subDir, "*.lnk"))
                        {
                            if (TryReadWinBinsShortcutMetadata(file, out string args, out string foundAumid))
                            {
                                scanned.Add(new PinnedShortcutRecord
                                {
                                    LnkPath = file,
                                    Arguments = args,
                                    Aumid = foundAumid
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            lock (PinnedCacheLock)
            {
                _cachedPinnedStamp = currentStamp;
                _cachedPinnedWinBinsShortcuts = scanned;
            }

            return scanned;
        }

        public static List<Tuple<string, string>> FindAllActivePinnedShortcuts(string binId)
        {
            var results = new List<Tuple<string, string>>();
            if (string.IsNullOrWhiteSpace(binId))
                return results;

            List<PinnedShortcutRecord> cached = GetCachedActiveWinBinsShortcuts();
            string quotedArg = $"--bin \"{binId}\"";
            string plainArg = $"--bin {binId}";

            for (int i = 0; i < cached.Count; i++)
            {
                PinnedShortcutRecord rec = cached[i];
                bool argsMatch = !string.IsNullOrEmpty(rec.Arguments) &&
                                 (rec.Arguments.IndexOf(quotedArg, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  rec.Arguments.IndexOf(plainArg, StringComparison.OrdinalIgnoreCase) >= 0);
                bool aumidMatch = !string.IsNullOrWhiteSpace(rec.Aumid) &&
                                  rec.Aumid.IndexOf(binId, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                  rec.Aumid.StartsWith("WinBins.", StringComparison.OrdinalIgnoreCase);

                if (argsMatch || aumidMatch)
                {
                    results.Add(Tuple.Create(rec.LnkPath, rec.Aumid));
                }
            }

            return results;
        }

        public static bool TryResolveShortcutTarget(string lnkPath, out string targetPath, out string arguments)
        {
            return TryReadShortcutDetails(lnkPath, out targetPath, out arguments, out _);
        }

        public static bool TryReadShortcutDetails(string lnkPath, out string targetPath, out string arguments, out string aumid)
        {
            targetPath = null;
            arguments = null;
            aumid = null;
            if (string.IsNullOrWhiteSpace(lnkPath) || !File.Exists(lnkPath))
                return false;

            var link = (IShellLinkW)new ShellLinkCoClass();
            try
            {
                ((IPersistFile)link).Load(lnkPath, 0); // STGM_READ

                var pathSb = new StringBuilder(1024);
                link.GetPath(pathSb, pathSb.Capacity, IntPtr.Zero, 0);
                string rawTarget = pathSb.ToString();

                var argsSb = new StringBuilder(1024);
                link.GetArguments(argsSb, argsSb.Capacity);
                arguments = argsSb.ToString() ?? string.Empty;

                if (link is Win32Interop.IPropertyStore propStore)
                {
                    var key = Win32Interop.PKEY_AppUserModel_ID;
                    if (propStore.GetValue(ref key, out Win32Interop.PropVariant pv) == 0)
                    {
                        try
                        {
                            aumid = pv.AsString();
                        }
                        finally
                        {
                            pv.Dispose();
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(rawTarget))
                {
                    targetPath = Environment.ExpandEnvironmentVariables(rawTarget.Trim().Trim('"'));
                }

                return !string.IsNullOrWhiteSpace(targetPath) || !string.IsNullOrWhiteSpace(aumid);
            }
            catch
            {
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }

            return false;
        }

        /// <summary>
        /// Returns all packaged AUMIDs from the provided list that are currently referenced in Windows Explorer's
        /// Taskband Favorites registry blob (pinned Store/UWP apps on the Windows 11 taskbar).
        /// </summary>
        public static HashSet<string> FindPinnedTaskbandAumids(IEnumerable<string> candidateAumids)
        {
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (candidateAumids == null)
                return matched;

            byte[] favoritesBytes = ReadTaskbandFavoritesBytes();
            if (favoritesBytes == null || favoritesBytes.Length == 0)
                return matched;

            string favAscii = Encoding.ASCII.GetString(favoritesBytes);
            string favUtf16Even = null;
            string favUtf16Odd = null;
            int evenLen = favoritesBytes.Length - (favoritesBytes.Length % 2);
            if (evenLen >= 2)
                favUtf16Even = Encoding.Unicode.GetString(favoritesBytes, 0, evenLen);
            if (favoritesBytes.Length >= 3)
            {
                int oddLen = (favoritesBytes.Length - 1) - ((favoritesBytes.Length - 1) % 2);
                if (oddLen >= 2)
                    favUtf16Odd = Encoding.Unicode.GetString(favoritesBytes, 1, oddLen);
            }

            foreach (string aumid in candidateAumids)
            {
                if (string.IsNullOrWhiteSpace(aumid) ||
                    aumid.StartsWith("WinBins.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ContainsDecodedIgnoreCase(favAscii, favUtf16Even, favUtf16Odd, aumid))
                {
                    matched.Add(aumid);
                }
            }

            return matched;
        }

        /// <summary>
        /// Enumerates all non-Bins shortcuts currently pinned to the Windows 11 taskbar
        /// (both User Pinned\TaskBar and active User Pinned\ImplicitAppShortcuts).
        /// </summary>
        public static List<string> GetPinnedNonBinTaskbarShortcutFiles()
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            byte[] favoritesBytes = ReadTaskbandFavoritesBytes();
            string favAscii = null;
            string favUtf16Even = null;
            string favUtf16Odd = null;
            bool hasFavBlob = favoritesBytes != null && favoritesBytes.Length > 0;
            if (hasFavBlob)
            {
                favAscii = Encoding.ASCII.GetString(favoritesBytes);
                int evenLen = favoritesBytes.Length - (favoritesBytes.Length % 2);
                if (evenLen >= 2)
                    favUtf16Even = Encoding.Unicode.GetString(favoritesBytes, 0, evenLen);
                if (favoritesBytes.Length >= 3)
                {
                    int oddLen = (favoritesBytes.Length - 1) - ((favoritesBytes.Length - 1) % 2);
                    if (oddLen >= 2)
                        favUtf16Odd = Encoding.Unicode.GetString(favoritesBytes, 1, oddLen);
                }
            }

            string selfExe = Assembly.GetExecutingAssembly().Location;

            bool IsBinShortcut(string lnkFile)
            {
                if (!TryReadShortcutDetails(lnkFile, out string target, out string args, out string aumid))
                    return false;

                if (!string.IsNullOrWhiteSpace(aumid) &&
                    aumid.StartsWith("WinBins.", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!string.IsNullOrWhiteSpace(args) &&
                    args.IndexOf("--bin", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (!string.IsNullOrWhiteSpace(target))
                {
                    if (string.Equals(target, selfExe, StringComparison.OrdinalIgnoreCase))
                        return true;
                    string exeName = Path.GetFileName(target);
                    if (string.Equals(exeName, "Bins.exe", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }

            try
            {
                string pinnedDir = GetUserPinnedTaskbarFolder();
                if (Directory.Exists(pinnedDir))
                {
                    foreach (string file in Directory.GetFiles(pinnedDir, "*.lnk"))
                    {
                        if (IsBinShortcut(file))
                            continue;

                        if (seen.Add(file))
                            results.Add(file);
                    }
                }
            }
            catch
            {
            }

            try
            {
                string implicitDir = GetImplicitAppShortcutsFolder();
                if (Directory.Exists(implicitDir) && hasFavBlob)
                {
                    foreach (string subDir in Directory.GetDirectories(implicitDir))
                    {
                        string hashFolder = Path.GetFileName(subDir);
                        if (string.IsNullOrWhiteSpace(hashFolder) ||
                            !ContainsDecodedIgnoreCase(favAscii, favUtf16Even, favUtf16Odd, hashFolder))
                        {
                            continue;
                        }

                        foreach (string file in Directory.GetFiles(subDir, "*.lnk"))
                        {
                            if (IsBinShortcut(file))
                                continue;

                            if (seen.Add(file))
                                results.Add(file);
                        }
                    }
                }
            }
            catch
            {
            }

            return results;
        }

        private static bool InspectShortcutForBin(string lnkPath, string binId, out string aumid)
        {
            aumid = null;
            var link = (IShellLinkW)new ShellLinkCoClass();
            try
            {
                ((IPersistFile)link).Load(lnkPath, 0); // STGM_READ

                var argsSb = new StringBuilder(512);
                link.GetArguments(argsSb, argsSb.Capacity);
                string args = argsSb.ToString();

                string readAumid = null;
                if (link is Win32Interop.IPropertyStore propStore)
                {
                    var key = Win32Interop.PKEY_AppUserModel_ID;
                    if (propStore.GetValue(ref key, out Win32Interop.PropVariant pv) == 0)
                    {
                        try
                        {
                            readAumid = pv.AsString();
                        }
                        finally
                        {
                            pv.Dispose();
                        }
                    }
                }

                bool argsMatch = args.IndexOf($"--bin \"{binId}\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 args.IndexOf($"--bin {binId}", StringComparison.OrdinalIgnoreCase) >= 0;
                bool aumidMatch = !string.IsNullOrWhiteSpace(readAumid) &&
                                  readAumid.IndexOf(binId, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                  readAumid.StartsWith("WinBins.", StringComparison.OrdinalIgnoreCase);

                if (argsMatch || aumidMatch)
                {
                    aumid = readAumid;
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }

            return false;
        }

        public static void SyncStartMenuAndPinnedShortcuts(BinGroup bin, string icoFilePath, string activeAumid)
        {
            if (bin == null || string.IsNullOrWhiteSpace(bin.Id))
                return;

            string exePath = Assembly.GetExecutingAssembly().Location;
            string workDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;

            // 1. Keep Start Menu shortcut registered so Windows 11 "Pin to taskbar" resolves custom icon & RelaunchCommand cleanly
            try
            {
                string startFolder = GetStartMenuBinsFolder();
                Directory.CreateDirectory(startFolder);

                // Clean any renamed older shortcut for this same binId
                foreach (string existing in Directory.GetFiles(startFolder, "*.lnk"))
                {
                    if (InspectShortcutForBin(existing, bin.Id, out _))
                    {
                        string expectedName = $"{SanitizeFileName(bin.Name)}.lnk";
                        if (!string.Equals(Path.GetFileName(existing), expectedName, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(existing); } catch { }
                        }
                    }
                }

                WriteShortcutFile(
                    Path.Combine(startFolder, $"{SanitizeFileName(bin.Name)}.lnk"),
                    exePath,
                    $"--bin \"{bin.Id}\"",
                    workDir,
                    $"Open {bin.Name} Bin",
                    icoFilePath,
                    activeAumid);
            }
            catch
            {
            }

            // 2. If already pinned on the Windows 11 Taskbar (in TaskBar\ or ImplicitAppShortcuts\),
            // update its icon in-place without changing its pinned AppUserModelID
            try
            {
                foreach (Tuple<string, string> match in FindAllActivePinnedShortcuts(bin.Id))
                {
                    string pinnedFile = match.Item1;
                    string existingPinnedAumid = match.Item2;
                    string aumidToKeep = !string.IsNullOrWhiteSpace(existingPinnedAumid)
                        ? existingPinnedAumid
                        : activeAumid;

                    UpdatePinnedShortcutInPlace(
                        pinnedFile,
                        exePath,
                        $"--bin \"{bin.Id}\"",
                        workDir,
                        $"Open {bin.Name} Bin",
                        icoFilePath,
                        aumidToKeep);
                }
            }
            catch
            {
            }
        }

        public static void RemoveStartMenuShortcut(string binId)
        {
            if (string.IsNullOrWhiteSpace(binId))
                return;

            try
            {
                string startFolder = GetStartMenuBinsFolder();
                if (!Directory.Exists(startFolder))
                    return;

                foreach (string existing in Directory.GetFiles(startFolder, "*.lnk"))
                {
                    if (InspectShortcutForBin(existing, binId, out _))
                    {
                        try { File.Delete(existing); } catch { }
                    }
                }
            }
            catch
            {
            }
        }

        public static string CreateBinShortcut(BinGroup bin, string icoFilePath, string targetFolder)
        {
            if (bin == null)
                throw new ArgumentNullException(nameof(bin));

            Directory.CreateDirectory(targetFolder);

            string safeName = SanitizeFileName(bin.Name);
            string lnkPath = Path.Combine(targetFolder, $"{safeName}.lnk");
            string exePath = Assembly.GetExecutingAssembly().Location;
            string workDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;
            string aumid = ResolveActiveAppUserModelId(bin);

            WriteShortcutFile(
                lnkPath,
                exePath,
                $"--bin \"{bin.Id}\"",
                workDir,
                $"Open {bin.Name} Bin",
                icoFilePath,
                aumid);

            Win32Interop.NotifyShellItemUpdated(lnkPath);
            return lnkPath;
        }

        private static void WriteShortcutFile(
            string lnkPath,
            string exePath,
            string arguments,
            string workDir,
            string description,
            string icoFilePath,
            string aumid)
        {
            var link = (IShellLinkW)new ShellLinkCoClass();
            try
            {
                link.SetPath(exePath);
                link.SetArguments(arguments);
                link.SetWorkingDirectory(workDir);
                link.SetDescription(description);
                if (!string.IsNullOrWhiteSpace(icoFilePath) && File.Exists(icoFilePath))
                {
                    link.SetIconLocation(icoFilePath, 0);
                }

                if (!string.IsNullOrWhiteSpace(aumid) && link is Win32Interop.IPropertyStore propStore)
                {
                    var key = Win32Interop.PKEY_AppUserModel_ID;
                    var pv = Win32Interop.PropVariant.FromString(aumid);
                    try
                    {
                        propStore.SetValue(ref key, ref pv);
                        propStore.Commit();
                    }
                    finally
                    {
                        pv.Dispose();
                    }
                }

                ((IPersistFile)link).Save(lnkPath, true);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }

        private static void UpdatePinnedShortcutInPlace(
            string lnkPath,
            string exePath,
            string arguments,
            string workDir,
            string description,
            string icoFilePath,
            string aumidToKeep)
        {
            var link = (IShellLinkW)new ShellLinkCoClass();
            try
            {
                ((IPersistFile)link).Load(lnkPath, 2); // STGM_READWRITE

                var iconSb = new StringBuilder(512);
                link.GetIconLocation(iconSb, iconSb.Capacity, out _);
                bool iconChanged = !string.Equals(iconSb.ToString(), icoFilePath ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                link.SetPath(exePath);
                link.SetArguments(arguments);
                link.SetWorkingDirectory(workDir);
                link.SetDescription(description);
                if (!string.IsNullOrWhiteSpace(icoFilePath) && File.Exists(icoFilePath))
                {
                    link.SetIconLocation(icoFilePath, 0);
                }

                if (!string.IsNullOrWhiteSpace(aumidToKeep) && link is Win32Interop.IPropertyStore propStore)
                {
                    var key = Win32Interop.PKEY_AppUserModel_ID;
                    var pv = Win32Interop.PropVariant.FromString(aumidToKeep);
                    try
                    {
                        propStore.SetValue(ref key, ref pv);
                        propStore.Commit();
                    }
                    finally
                    {
                        pv.Dispose();
                    }
                }

                ((IPersistFile)link).Save(lnkPath, true);
                if (iconChanged)
                {
                    File.SetLastWriteTimeUtc(lnkPath, DateTime.UtcNow);
                    Win32Interop.NotifyShellItemUpdated(lnkPath);
                }
            }
            catch
            {
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Bin";

            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (Array.IndexOf(invalid, c) < 0)
                    sb.Append(c);
            }

            string result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? "Bin" : result;
        }
    }
}
