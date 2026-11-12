using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WinBins.Interop;
using WinBins.Models;

namespace WinBins.Services
{
    public static class JumpListService
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

        private static readonly Guid IID_IObjectArray = new Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9");

        public static void SyncBinJumpList(ConfigService configService, IconService iconService, BinGroup bin, string activeAumid)
        {
            if (configService == null || iconService == null || bin == null || string.IsNullOrWhiteSpace(bin.Id))
                return;

            var targetAumids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(activeAumid))
            {
                targetAumids.Add(activeAumid);
            }

            string resolved = ShortcutService.ResolveActiveAppUserModelId(bin);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                targetAumids.Add(resolved);
            }

            foreach (Tuple<string, string> match in ShortcutService.FindAllActivePinnedShortcuts(bin.Id))
            {
                if (!string.IsNullOrWhiteSpace(match.Item2))
                {
                    targetAumids.Add(match.Item2);
                }
            }

            foreach (string aumid in targetAumids)
            {
                WriteBinJumpListForAumid(configService, iconService, bin, aumid);
            }
        }

        private static void WriteBinJumpListForAumid(ConfigService configService, IconService iconService, BinGroup bin, string aumid)
        {
            Win32Interop.ICustomDestinationList destList = null;
            Win32Interop.IObjectArray removed = null;
            var comObjectsToRelease = new List<object>();

            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                string workDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;

                destList = Win32Interop.CreateCustomDestinationList();
                if (destList.SetAppID(aumid) != 0)
                    return;

                Guid iidArray = IID_IObjectArray;
                if (destList.BeginList(out uint minSlots, ref iidArray, out removed) != 0)
                    return;

                int maxItems = Math.Max(1, Math.Min(12, (int)Math.Min(minSlots > 0 ? minSlots : 10u, 12u)));
                var items = bin.Items ?? new List<BinItem>();
                int itemCountToInclude = Math.Min(items.Count, maxItems);

                bool categoryAdded = false;
                if (itemCountToInclude > 0)
                {
                    Win32Interop.IObjectCollection catCollection = Win32Interop.CreateObjectCollection();
                    comObjectsToRelease.Add(catCollection);

                    for (int i = 0; i < itemCountToInclude; i++)
                    {
                        BinItem item = items[i];
                        if (item == null)
                            continue;

                        string itemIco = EnsureItemJumpListIco(configService, iconService, item);
                        object itemLink = CreateJumpListLink(
                            exePath,
                            $"--launch-item \"{bin.Id}\" \"{item.Id}\"",
                            workDir,
                            string.IsNullOrWhiteSpace(item.Name) ? "Item" : item.Name,
                            $"Open {item.Name}",
                            itemIco,
                            0);
                        catCollection.AddObject(itemLink);
                        comObjectsToRelease.Add(itemLink);
                    }

                    if (catCollection is Win32Interop.IObjectArray catArray)
                    {
                        int hrCat = destList.AppendCategory("Items", catArray);
                        categoryAdded = (hrCat == 0);
                    }
                }

                // Build Tasks section
                Win32Interop.IObjectCollection tasksCollection = Win32Interop.CreateObjectCollection();
                comObjectsToRelease.Add(tasksCollection);

                // Fallback: if Windows Jump List "Show recently opened items" is disabled (AppendCategory denied),
                // include the Bin's items directly at the top of User Tasks so they always appear on right-click.
                if (!categoryAdded && itemCountToInclude > 0)
                {
                    for (int i = 0; i < itemCountToInclude; i++)
                    {
                        BinItem item = items[i];
                        if (item == null)
                            continue;

                        string itemIco = EnsureItemJumpListIco(configService, iconService, item);
                        object itemLink = CreateJumpListLink(
                            exePath,
                            $"--launch-item \"{bin.Id}\" \"{item.Id}\"",
                            workDir,
                            string.IsNullOrWhiteSpace(item.Name) ? "Item" : item.Name,
                            $"Open {item.Name}",
                            itemIco,
                            0);
                        tasksCollection.AddObject(itemLink);
                        comObjectsToRelease.Add(itemLink);
                    }
                }

                var workspaceItems = items.FindAll(it => it != null && it.IncludeInOpenAll && !BinItem.IsSystemCommand(it.TargetPath));
                if (workspaceItems.Count >= 1 && workspaceItems.Count <= 16 && (items.Count >= 2 || workspaceItems.Count != items.Count))
                {
                    string openAllIco = EnsureGlyphJumpListIco(configService, "open_all", "\uE8A7");
                    bool subset = workspaceItems.Count < items.Count;
                    string openLabel = subset
                        ? $"Open workspace ({workspaceItems.Count} items)"
                        : $"Open all ({workspaceItems.Count} items)";
                    object openAllLink = CreateJumpListLink(
                        exePath,
                        $"--open-all \"{bin.Id}\"",
                        workDir,
                        openLabel,
                        $"Launch workspace items in {bin.Name}",
                        openAllIco,
                        0);
                    tasksCollection.AddObject(openAllLink);
                    comObjectsToRelease.Add(openAllLink);
                }

                string editIco = EnsureGlyphJumpListIco(configService, "customize_bin", "\uE70F");
                object customizeLink = CreateJumpListLink(
                    exePath,
                    $"--customize \"{bin.Id}\"",
                    workDir,
                    $"Edit \"{bin.Name}\"...",
                    $"Manage items in {bin.Name}",
                    editIco,
                    0);
                tasksCollection.AddObject(customizeLink);
                comObjectsToRelease.Add(customizeLink);

                string settingsIco = EnsureGlyphJumpListIco(configService, "settings", "\uE713");
                object settingsLink = CreateJumpListLink(
                    exePath,
                    "--settings",
                    workDir,
                    "Bins settings",
                    "Open global Bins settings",
                    settingsIco,
                    0);
                tasksCollection.AddObject(settingsLink);
                comObjectsToRelease.Add(settingsLink);

                if (tasksCollection is Win32Interop.IObjectArray tasksArray)
                {
                    destList.AddUserTasks(tasksArray);
                }

                destList.CommitList();
            }
            catch
            {
            }
            finally
            {
                for (int i = comObjectsToRelease.Count - 1; i >= 0; i--)
                {
                    TryReleaseCom(comObjectsToRelease[i]);
                }
                TryReleaseCom(removed);
                TryReleaseCom(destList);
            }
        }

        public static void SyncManagerJumpList(ConfigService configService, IconService iconService)
        {
            if (configService == null || iconService == null)
                return;

            Win32Interop.ICustomDestinationList destList = null;
            Win32Interop.IObjectArray removed = null;
            var comObjectsToRelease = new List<object>();

            try
            {
                string exePath = Assembly.GetExecutingAssembly().Location;
                string workDir = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory;

                destList = Win32Interop.CreateCustomDestinationList();
                if (destList.SetAppID("WinBins.Manager.App") != 0)
                    return;

                Guid iidArray = IID_IObjectArray;
                if (destList.BeginList(out uint minSlots, ref iidArray, out removed) != 0)
                    return;

                var bins = configService.Current?.Bins ?? new List<BinGroup>();
                int count = Math.Min(bins.Count, (int)Math.Min(minSlots > 0 ? minSlots : 10u, 10u));

                bool catAdded = false;
                if (count > 0)
                {
                    Win32Interop.IObjectCollection binsCollection = Win32Interop.CreateObjectCollection();
                    comObjectsToRelease.Add(binsCollection);

                    for (int i = 0; i < count; i++)
                    {
                        BinGroup b = bins[i];
                        string binIco = iconService.EnsureBinIcoFile(b);
                        object link = CreateJumpListLink(
                            exePath,
                            $"--customize \"{b.Id}\"",
                            workDir,
                            b.Name ?? "Bin",
                            $"Customize {b.Name}",
                            binIco,
                            0);
                        binsCollection.AddObject(link);
                        comObjectsToRelease.Add(link);
                    }

                    if (binsCollection is Win32Interop.IObjectArray binsArray)
                    {
                        catAdded = (destList.AppendCategory("Bins", binsArray) == 0);
                    }
                }

                Win32Interop.IObjectCollection tasks = Win32Interop.CreateObjectCollection();
                comObjectsToRelease.Add(tasks);

                if (!catAdded && count > 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        BinGroup b = bins[i];
                        string binIco = iconService.EnsureBinIcoFile(b);
                        object link = CreateJumpListLink(
                            exePath,
                            $"--customize \"{b.Id}\"",
                            workDir,
                            b.Name ?? "Bin",
                            $"Customize {b.Name}",
                            binIco,
                            0);
                        tasks.AddObject(link);
                        comObjectsToRelease.Add(link);
                    }
                }

                string searchIco = EnsureGlyphJumpListIco(configService, "omni_search", "\uE721");
                object searchLink = CreateJumpListLink(
                    exePath,
                    "--search",
                    workDir,
                    "Search all Bins",
                    "Quickly search and launch items across all Bins",
                    searchIco,
                    0);
                tasks.AddObject(searchLink);
                comObjectsToRelease.Add(searchLink);

                string newIco = EnsureGlyphJumpListIco(configService, "add_item", "\uE710");
                object newBinLink = CreateJumpListLink(
                    exePath,
                    "--new-bin",
                    workDir,
                    "Create new Bin",
                    "Create a new taskbar Bin",
                    newIco,
                    0);
                tasks.AddObject(newBinLink);
                comObjectsToRelease.Add(newBinLink);

                string settingsIco = EnsureGlyphJumpListIco(configService, "settings", "\uE713");
                object settingsLink = CreateJumpListLink(
                    exePath,
                    "--settings",
                    workDir,
                    "Bins settings",
                    "Open global Bins settings",
                    settingsIco,
                    0);
                tasks.AddObject(settingsLink);
                comObjectsToRelease.Add(settingsLink);

                if (tasks is Win32Interop.IObjectArray tasksArray)
                {
                    destList.AddUserTasks(tasksArray);
                }

                destList.CommitList();
            }
            catch
            {
            }
            finally
            {
                for (int i = comObjectsToRelease.Count - 1; i >= 0; i--)
                {
                    TryReleaseCom(comObjectsToRelease[i]);
                }
                TryReleaseCom(removed);
                TryReleaseCom(destList);
            }
        }

        public static void DeleteBinJumpList(string aumid)
        {
            if (string.IsNullOrWhiteSpace(aumid))
                return;

            Win32Interop.ICustomDestinationList destList = null;
            try
            {
                destList = Win32Interop.CreateCustomDestinationList();
                destList.DeleteList(aumid);
            }
            catch
            {
            }
            finally
            {
                TryReleaseCom(destList);
            }
        }

        private static object CreateJumpListLink(
            string exePath,
            string arguments,
            string workDir,
            string title,
            string description,
            string iconPath,
            int iconIndex)
        {
            var link = (IShellLinkW)new ShellLinkCoClass();
            link.SetPath(exePath);
            link.SetArguments(arguments ?? string.Empty);
            link.SetWorkingDirectory(workDir ?? string.Empty);
            link.SetDescription(description ?? title ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                link.SetIconLocation(iconPath, iconIndex);
            }

            if (link is Win32Interop.IPropertyStore propStore)
            {
                var key = Win32Interop.PKEY_Title;
                var pv = Win32Interop.PropVariant.FromString(title ?? string.Empty);
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

            return link;
        }

        private static object CreateJumpListSeparator()
        {
            var link = (IShellLinkW)new ShellLinkCoClass();
            if (link is Win32Interop.IPropertyStore propStore)
            {
                var key = Win32Interop.PKEY_AppUserModel_IsDestListSeparator;
                var pv = Win32Interop.PropVariant.FromBool(true);
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
            return link;
        }

        private static string EnsureItemJumpListIco(ConfigService configService, IconService iconService, BinItem item)
        {
            try
            {
                string jumpDir = Path.Combine(configService.IconsFolder, "JumpList");
                Directory.CreateDirectory(jumpDir);

                string rawSig = $"{item.CustomIconPath}|{item.CustomIconIndex}|{(int)item.Kind}|{item.TargetPath}";
                uint hash = ComputeFnv1aHash(rawSig);
                string icoPath = Path.Combine(jumpDir, $"item_{hash:x8}.ico");

                if (!File.Exists(icoPath))
                {
                    Bitmap cachedBmp = iconService.GetItemBitmap(item);
                    if (cachedBmp != null)
                    {
                        using (Bitmap sized = Win32Interop.ResizeToSquareBitmap(cachedBmp, 64))
                        {
                            Win32Interop.SaveMultiFrameIconFile(sized, icoPath);
                        }
                    }
                }

                return File.Exists(icoPath) ? icoPath : null;
            }
            catch
            {
                return null;
            }
        }

        private static string EnsureGlyphJumpListIco(ConfigService configService, string nameKey, string glyph)
        {
            try
            {
                string jumpDir = Path.Combine(configService.IconsFolder, "JumpList");
                Directory.CreateDirectory(jumpDir);

                bool lightTaskbar = IsSystemLightTaskbar();
                string themeSuffix = lightTaskbar ? "light" : "dark";
                string icoPath = Path.Combine(jumpDir, $"glyph_{nameKey}_{themeSuffix}.ico");

                if (!File.Exists(icoPath))
                {
                    Color glyphColor = lightTaskbar
                        ? Color.FromArgb(245, 30, 30, 30)
                        : Color.FromArgb(245, 245, 245, 245);

                    using (Bitmap bmp = RenderFluentGlyphBitmap(glyph, glyphColor, 64))
                    {
                        Win32Interop.SaveMultiFrameIconFile(bmp, icoPath);
                    }
                }

                return File.Exists(icoPath) ? icoPath : null;
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap RenderFluentGlyphBitmap(string glyph, Color fgColor, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                Font font = CreateBestIconFont(size * 0.56f);
                try
                {
                    using (var brush = new SolidBrush(fgColor))
                    using (var sf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip
                    })
                    {
                        g.DrawString(glyph, font, brush, new RectangleF(0, 1, size, size), sf);
                    }
                }
                finally
                {
                    font.Dispose();
                }
            }
            return bmp;
        }

        private static Font CreateBestIconFont(float emSize)
        {
            try
            {
                var fluent = new Font("Segoe Fluent Icons", emSize, FontStyle.Regular, GraphicsUnit.Pixel);
                if (string.Equals(fluent.Name, "Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase))
                {
                    return fluent;
                }
                fluent.Dispose();
            }
            catch
            {
            }

            return new Font("Segoe MDL2 Assets", emSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private static bool IsSystemLightTaskbar()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", writable: false))
                {
                    if (key != null)
                    {
                        int val = Convert.ToInt32(key.GetValue("SystemUsesLightTheme", 0));
                        return val != 0;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private static uint ComputeFnv1aHash(string input)
        {
            uint hash = 2166136261u;
            if (string.IsNullOrEmpty(input))
                return hash;

            for (int i = 0; i < input.Length; i++)
            {
                hash ^= char.ToLowerInvariant(input[i]);
                hash *= 16777619u;
            }
            return hash;
        }

        private static void TryReleaseCom(object obj)
        {
            if (obj != null && Marshal.IsComObject(obj))
            {
                try { Marshal.ReleaseComObject(obj); } catch { }
            }
        }
    }
}
