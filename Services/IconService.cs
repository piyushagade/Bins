using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinBins.Interop;
using WinBins.Models;

namespace WinBins.Services
{
    public class PresetIconOption
    {
        public string Label { get; set; }
        public string ResourcePath { get; set; }
        public int ResourceIndex { get; set; }
    }

    public class IconService : IDisposable
    {
        private readonly ConfigService _configService;
        private readonly ConcurrentDictionary<string, Bitmap> _bitmapCache = new ConcurrentDictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, BitmapSource> _wpfImageCache = new ConcurrentDictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        public IconService(ConfigService configService)
        {
            _configService = configService;
        }

        private void ResolveItemIconKey(BinItem item, out string sourcePath, out int resourceIndex, out string cacheKey)
        {
            if (item == null)
            {
                sourcePath = null;
                resourceIndex = 0;
                cacheKey = "__fallback__";
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.CustomIconPath))
            {
                sourcePath = ResolveCustomIconPathWithFallback(item.CustomIconPath);
                resourceIndex = item.CustomIconIndex;
                cacheKey = $"custom|{sourcePath}|{resourceIndex}";
                return;
            }

            if (item.Kind == BinItemKind.WebUrl || BinItem.IsWebUrl(item.TargetPath))
            {
                string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                sourcePath = Path.Combine(sys32, "imageres.dll");
                resourceIndex = -25; // Windows 11 Globe / Web icon
                cacheKey = $"webglobe|{sourcePath}|-25";
                return;
            }

            sourcePath = item.TargetPath;
            resourceIndex = 0;
            cacheKey = string.IsNullOrWhiteSpace(sourcePath) ? "__fallback__" : $"target|{sourcePath}|0";
        }

        private string ResolveCustomIconPathWithFallback(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return rawPath;

            string expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
            if (File.Exists(expanded) || _configService == null)
                return rawPath;

            string fileName = Path.GetFileName(expanded);
            if (!string.IsNullOrEmpty(fileName))
            {
                string candidate = Path.Combine(_configService.IconsFolder, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return rawPath;
        }

        public Bitmap GetItemBitmap(BinItem item)
        {
            ResolveItemIconKey(item, out string sourcePath, out int resourceIndex, out string cacheKey);
            if (string.IsNullOrWhiteSpace(sourcePath))
                return CreateFallbackItemBitmap();

            return _bitmapCache.GetOrAdd(cacheKey, _ =>
            {
                Bitmap extracted = Win32Interop.ExtractHighResIconBitmap(sourcePath, resourceIndex, 256);
                return extracted ?? CreateFallbackItemBitmap();
            });
        }

        public BitmapSource GetItemImageSource(BinItem item)
        {
            ResolveItemIconKey(item, out _, out _, out string cacheKey);
            return _wpfImageCache.GetOrAdd(cacheKey, _ =>
            {
                Bitmap bmp = GetItemBitmap(item);
                return ConvertBitmapToBitmapSource(bmp);
            });
        }

        public BitmapSource GetPathImageSource(string path, int resourceIndex = 0)
        {
            string cacheKey = $"{path}|{resourceIndex}";
            return _wpfImageCache.GetOrAdd(cacheKey, _ =>
            {
                Bitmap bmp = Win32Interop.ExtractHighResIconBitmap(path, resourceIndex, 128) ?? CreateFallbackItemBitmap();
                return ConvertBitmapToBitmapSource(bmp);
            });
        }

        /// <summary>
        /// Creates the 128x128 transparent ARGB Bitmap for a BinGroup (either GridMosaic or CustomIcon).
        /// </summary>
        public Bitmap CreateBinBitmap(BinGroup bin)
        {
            if (bin == null)
                return Win32Interop.CreateGroupMosaicBitmap(null);

            if (bin.IconMode == BinIconMode.CustomIcon && !string.IsNullOrWhiteSpace(bin.CustomIconPath))
            {
                string resolvedCustom = ResolveCustomIconPathWithFallback(bin.CustomIconPath);
                Bitmap custom = Win32Interop.ExtractHighResIconBitmap(resolvedCustom, bin.CustomIconIndex, 128);
                if (custom != null)
                {
                    Bitmap resized = Win32Interop.ResizeToSquareBitmap(custom, 128);
                    custom.Dispose();
                    return resized;
                }
            }

            var subIcons = new List<Bitmap>(4);
            if (bin.Items != null)
            {
                for (int i = 0; i < bin.Items.Count && subIcons.Count < 4; i++)
                {
                    Bitmap itemBmp = GetItemBitmap(bin.Items[i]);
                    if (itemBmp != null)
                    {
                        subIcons.Add(itemBmp);
                    }
                }
            }

            return Win32Interop.CreateGroupMosaicBitmap(subIcons, bin.ShowMosaicBackplate);
        }

        /// <summary>
        /// Creates a native Win32 HICON handle for the BinGroup.
        /// For GridMosaic mode, delegates directly to Win32Interop.CreateGroupMosaicIconHandle.
        /// Caller owns the returned HICON and must call Win32Interop.DestroyIcon when finished.
        /// </summary>
        public IntPtr CreateBinIconHandle(BinGroup bin)
        {
            if (bin != null && bin.IconMode == BinIconMode.CustomIcon && !string.IsNullOrWhiteSpace(bin.CustomIconPath))
            {
                string resolvedCustom = ResolveCustomIconPathWithFallback(bin.CustomIconPath);
                using (Bitmap custom = Win32Interop.ExtractHighResIconBitmap(resolvedCustom, bin.CustomIconIndex, 128))
                {
                    if (custom != null)
                    {
                        using (Bitmap sized = Win32Interop.ResizeToSquareBitmap(custom, 128))
                        {
                            return Win32Interop.CreateAlphaIconHandle(sized);
                        }
                    }
                }
            }

            var subIcons = new List<Bitmap>(4);
            if (bin?.Items != null)
            {
                for (int i = 0; i < bin.Items.Count && subIcons.Count < 4; i++)
                {
                    Bitmap itemBmp = GetItemBitmap(bin.Items[i]);
                    if (itemBmp != null)
                    {
                        subIcons.Add(itemBmp);
                    }
                }
            }

            return Win32Interop.CreateGroupMosaicIconHandle(subIcons, bin?.ShowMosaicBackplate ?? false);
        }

        public IntPtr CreateBinSmallIconHandle(BinGroup bin, int size = 32)
        {
            using (Bitmap full = CreateBinBitmap(bin))
            using (Bitmap small = Win32Interop.ResizeToSquareBitmap(full, size))
            {
                return Win32Interop.CreateAlphaIconHandle(small);
            }
        }

        public static string ComputeIconSignature(BinGroup bin)
        {
            if (bin == null)
                return string.Empty;

            if (bin.IconMode == BinIconMode.CustomIcon && !string.IsNullOrWhiteSpace(bin.CustomIconPath))
            {
                return $"Custom|{bin.CustomIconPath}|{bin.CustomIconIndex}";
            }

            var sb = new System.Text.StringBuilder(bin.ShowMosaicBackplate ? "GridPlate" : "Grid");
            if (bin.Items != null)
            {
                for (int i = 0; i < bin.Items.Count && i < 4; i++)
                {
                    BinItem it = bin.Items[i];
                    sb.Append('|')
                      .Append(it?.CustomIconPath ?? string.Empty)
                      .Append('#')
                      .Append(it?.CustomIconIndex ?? 0)
                      .Append(':')
                      .Append(it?.TargetPath ?? string.Empty);
                }
            }
            return sb.ToString();
        }

        public BitmapSource CreateBinImageSource(BinGroup bin)
        {
            using (Bitmap bmp = CreateBinBitmap(bin))
            {
                return ConvertBitmapToBitmapSource(bmp);
            }
        }

        /// <summary>
        /// Saves a multi-resolution .ico file for taskbar RelaunchIconResource and .lnk shortcut pinning.
        /// </summary>
        public string EnsureBinIcoFile(BinGroup bin)
        {
            if (bin == null)
                return string.Empty;

            int rev = Math.Max(1, bin.IconRevision);
            string icoPath = Path.Combine(_configService.IconsFolder, $"bin_{bin.Id}_{rev}.ico");
            try
            {
                using (Bitmap bmp = CreateBinBitmap(bin))
                {
                    Win32Interop.SaveMultiFrameIconFile(bmp, icoPath);
                }
            }
            catch
            {
            }

            // Clean older revision icon files (keep current and previous revision so Explorer never races on reload)
            try
            {
                string currentName = $"bin_{bin.Id}_{rev}.ico";
                string prevName = $"bin_{bin.Id}_{Math.Max(1, rev - 1)}.ico";
                string pattern = $"bin_{bin.Id}_*.ico";
                foreach (string oldFile in Directory.GetFiles(_configService.IconsFolder, pattern))
                {
                    string fn = Path.GetFileName(oldFile);
                    if (!string.Equals(fn, currentName, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(fn, prevName, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(oldFile); } catch { }
                    }
                }
            }
            catch { }

            return icoPath;
        }

        /// <summary>
        /// Builds a crisp Windows 11 DWM thumbnail card when hovering over the Bin's taskbar icon.
        /// </summary>
        public Bitmap CreateDwmPreviewCardBitmap(BinGroup bin, int maxWidth, int maxHeight)
        {
            int width = Math.Max(80, Math.Min(maxWidth, 260));
            int height = Math.Max(60, Math.Min(maxHeight, 160));

            var card = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(card))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                g.Clear(System.Drawing.Color.FromArgb(255, 30, 32, 42));

                using (var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(80, 255, 255, 255), 1f))
                {
                    g.DrawRectangle(borderPen, 0, 0, width - 1, height - 1);
                }

                // Draw up to 6 icons in a mini preview row/grid
                int count = bin?.Items?.Count ?? 0;
                if (count == 0)
                {
                    using (var font = new Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Regular))
                    using (var brush = new SolidBrush(System.Drawing.Color.FromArgb(180, 200, 205, 220)))
                    {
                        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        g.DrawString("Empty Bin — Click to add items", font, brush, new RectangleF(8, 8, width - 16, height - 16), sf);
                    }
                }
                else
                {
                    int cols = Math.Min(4, Math.Max(1, count));
                    int rows = (int)Math.Ceiling(Math.Min(count, 8) / (double)cols);
                    int iconSize = Math.Min(36, Math.Min((width - 24) / cols - 8, (height - 24) / rows - 6));
                    iconSize = Math.Max(20, iconSize);

                    int totalW = cols * iconSize + (cols - 1) * 10;
                    int totalH = rows * iconSize + (rows - 1) * 10;
                    int startX = (width - totalW) / 2;
                    int startY = (height - totalH) / 2;

                    using (var attrs = new ImageAttributes())
                    {
                        attrs.SetWrapMode(WrapMode.TileFlipXY);
                        int shown = Math.Min(count, 8);
                        for (int i = 0; i < shown; i++)
                        {
                            int r = i / cols;
                            int c = i % cols;
                            int x = startX + c * (iconSize + 10);
                            int y = startY + r * (iconSize + 10);

                            Bitmap itemBmp = GetItemBitmap(bin.Items[i]);
                            if (itemBmp != null)
                            {
                                g.DrawImage(itemBmp, new Rectangle(x, y, iconSize, iconSize), 0, 0, itemBmp.Width, itemBmp.Height, GraphicsUnit.Pixel, attrs);
                            }
                        }
                    }
                }
            }

            return card;
        }

        public static List<PresetIconOption> GetWindows11PresetIcons()
        {
            string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string imageres = Path.Combine(sys32, "imageres.dll");
            string shell32 = Path.Combine(sys32, "shell32.dll");

            return new List<PresetIconOption>
            {
                new PresetIconOption { Label = "Folder", ResourcePath = imageres, ResourceIndex = -3 },
                new PresetIconOption { Label = "Downloads", ResourcePath = imageres, ResourceIndex = -184 },
                new PresetIconOption { Label = "Documents", ResourcePath = imageres, ResourceIndex = -112 },
                new PresetIconOption { Label = "Pictures", ResourcePath = imageres, ResourceIndex = -113 },
                new PresetIconOption { Label = "Music", ResourcePath = imageres, ResourceIndex = -108 },
                new PresetIconOption { Label = "Videos", ResourcePath = imageres, ResourceIndex = -189 },
                new PresetIconOption { Label = "Desktop", ResourcePath = imageres, ResourceIndex = -183 },
                new PresetIconOption { Label = "Star / Favorites", ResourcePath = imageres, ResourceIndex = -1024 },
                new PresetIconOption { Label = "Settings Gear", ResourcePath = imageres, ResourceIndex = -114 },
                new PresetIconOption { Label = "Apps Window", ResourcePath = imageres, ResourceIndex = -15 },
                new PresetIconOption { Label = "PC / System", ResourcePath = imageres, ResourceIndex = -109 },
                new PresetIconOption { Label = "Shield / Admin", ResourcePath = imageres, ResourceIndex = -78 },
                new PresetIconOption { Label = "Network / Globe", ResourcePath = imageres, ResourceIndex = -25 },
                new PresetIconOption { Label = "Games / Controller", ResourcePath = imageres, ResourceIndex = -186 },
                new PresetIconOption { Label = "Tools / Control", ResourcePath = shell32, ResourceIndex = -21 },
                new PresetIconOption { Label = "Terminal / Prompt", ResourcePath = Path.Combine(sys32, "cmd.exe"), ResourceIndex = 0 }
            };
        }

        public void InvalidateCacheForPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            foreach (string key in _bitmapCache.Keys)
            {
                if (key.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    _bitmapCache.TryRemove(key, out Bitmap old))
                {
                    old?.Dispose();
                }
            }

            foreach (string key in _wpfImageCache.Keys)
            {
                if (key.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _wpfImageCache.TryRemove(key, out _);
                }
            }
        }

        /// <summary>
        /// Asynchronously downloads a high-resolution favicon for a WebUrl item (if the user hasn't already chosen a custom icon),
        /// caches it locally as a PNG in IconsFolder, and invokes onFetched on a background thread (caller marshals to Dispatcher).
        /// </summary>
        public void TryFetchWebsiteFaviconAsync(BinItem item, Action<string> onSavedPath)
        {
            if (item == null || !string.IsNullOrWhiteSpace(item.CustomIconPath) || !BinItem.IsWebUrl(item.TargetPath))
                return;

            if (!Uri.TryCreate(item.TargetPath.Trim(), UriKind.Absolute, out Uri uri) || string.IsNullOrWhiteSpace(uri.Host))
                return;

            string host = uri.Host.ToLowerInvariant();
            if (host == "localhost" || uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)
                return;

            Task.Run(() =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                    char[] invalid = Path.GetInvalidFileNameChars();
                    var safeHostSb = new System.Text.StringBuilder(host.Length);
                    foreach (char c in host)
                    {
                        safeHostSb.Append(Array.IndexOf(invalid, c) < 0 ? c : '_');
                    }

                    string targetPng = Path.Combine(_configService.IconsFolder, $"favicon_{safeHostSb}.png");
                    if (File.Exists(targetPng))
                    {
                        InvalidateCacheForPath(targetPng);
                        onSavedPath?.Invoke(targetPng);
                        return;
                    }

                    string[] candidateUrls =
                    {
                        $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=128",
                        $"{uri.Scheme}://{uri.Host}/favicon.ico"
                    };

                    foreach (string faviconUrl in candidateUrls)
                    {
                        byte[] bytes = TryDownloadBytes(faviconUrl, 3200);
                        if (bytes == null || bytes.Length < 48)
                            continue;

                        try
                        {
                            using (var ms = new MemoryStream(bytes))
                            using (var img = Image.FromStream(ms))
                            {
                                if (img.Width >= 16 && img.Height >= 16)
                                {
                                    using (Bitmap square = Win32Interop.ResizeToSquareBitmap(img, 128))
                                    {
                                        square.Save(targetPng, ImageFormat.Png);
                                    }
                                    InvalidateCacheForPath(targetPng);
                                    onSavedPath?.Invoke(targetPng);
                                    return;
                                }
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
            });
        }

        private static byte[] TryDownloadBytes(string url, int timeoutMs)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var stream = resp.GetResponseStream())
                {
                    if (stream == null)
                        return null;

                    using (var ms = new MemoryStream())
                    {
                        byte[] buf = new byte[4096];
                        int read;
                        while ((read = stream.Read(buf, 0, buf.Length)) > 0)
                        {
                            ms.Write(buf, 0, read);
                            if (ms.Length > 1024 * 1024)
                                return null;
                        }
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            if (bitmap == null)
                return null;

            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                BitmapSource source = BitmapSource.Create(
                    bitmap.Width,
                    bitmap.Height,
                    96.0,
                    96.0,
                    PixelFormats.Bgra32,
                    null,
                    data.Scan0,
                    data.Stride * bitmap.Height,
                    data.Stride);

                source.Freeze();
                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static Bitmap CreateFallbackItemBitmap()
        {
            var bmp = new Bitmap(128, 128, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                using (GraphicsPath path = Win32Interop.CreateRoundedRectPath(new Rectangle(12, 12, 104, 104), 22))
                using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Rectangle(12, 12, 104, 104),
                    System.Drawing.Color.FromArgb(240, 79, 140, 255),
                    System.Drawing.Color.FromArgb(240, 56, 98, 210),
                    45f))
                {
                    g.FillPath(brush, path);
                }

                using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(220, 255, 255, 255), 5f))
                {
                    g.DrawRectangle(pen, 40, 40, 48, 48);
                }
            }
            return bmp;
        }

        public void Dispose()
        {
            foreach (var kv in _bitmapCache)
            {
                try { kv.Value?.Dispose(); } catch { }
            }
            _bitmapCache.Clear();
            _wpfImageCache.Clear();
        }
    }
}
