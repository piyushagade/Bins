using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace WinBins.Interop
{
    public static class Win32Interop
    {
        #region Constants

        public const int WM_DESTROY = 0x0002;
        public const int WM_ACTIVATE = 0x0006;
        public const int WM_CLOSE = 0x0010;
        public const int WM_QUERYOPEN = 0x0013;
        public const int WM_ERASEBKGND = 0x0014;
        public const int WM_SETICON = 0x0080;
        public const int WM_SYSCOMMAND = 0x0112;
        public const int WM_DWMSENDICONICTHUMBNAIL = 0x0323;
        public const int WM_DWMSENDICONICLIVEPREVIEWBITMAP = 0x0326;
        public const int WM_USER = 0x0400;

        public const int WA_INACTIVE = 0;
        public const int WA_ACTIVE = 1;
        public const int WA_CLICKACTIVE = 2;

        public const int ICON_SMALL = 0;
        public const int ICON_BIG = 1;

        public const int SC_MINIMIZE = 0xF020;
        public const int SC_MAXIMIZE = 0xF030;
        public const int SC_RESTORE = 0xF120;

        public const uint WS_OVERLAPPED = 0x00000000;
        public const uint WS_CAPTION = 0x00C00000;
        public const uint WS_SYSMENU = 0x00080000;
        public const uint WS_MINIMIZEBOX = 0x00020000;
        public const uint WS_POPUP = 0x80000000;

        public const uint WS_EX_TOOLWINDOW = 0x00000080;
        public const uint WS_EX_APPWINDOW = 0x00040000;
        public const uint WS_EX_NOACTIVATE = 0x08000000;

        public const int SW_HIDE = 0;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_SHOW = 5;
        public const int SW_MINIMIZE = 6;
        public const int SW_SHOWMINNOACTIVE = 7;
        public const int SW_RESTORE = 9;

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const uint GW_OWNER = 4;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        public const int DWMWA_FORCE_ICONIC_REPRESENTATION = 7;
        public const int DWMWA_HAS_ICONIC_BITMAP = 10;
        public const int DWMWA_DISALLOW_PEEK = 11;
        public const int DWMWA_EXCLUDED_FROM_PEEK = 12;
        public const int DWMWA_CLOAKED = 14;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWA_CAPTION_COLOR = 35;
        public const int DWMWA_TEXT_COLOR = 36;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        public const uint EVENT_OBJECT_SHOW = 0x8002;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        public const int OBJID_WINDOW = 0;

        public const int DWMWCP_DEFAULT = 0;
        public const int DWMWCP_DONOTROUND = 1;
        public const int DWMWCP_ROUND = 2;
        public const int DWMWCP_ROUNDSMALL = 3;

        public const int DWMSBT_AUTO = 0;
        public const int DWMSBT_NONE = 1;
        public const int DWMSBT_MAINWINDOW = 2;      // Mica
        public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic

        public const uint SHGFI_ICON = 0x000000100;
        public const uint SHGFI_LARGEICON = 0x000000000;
        public const uint SHGFI_SYSICONINDEX = 0x000004000;
        public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        public const int SHIL_EXTRALARGE = 0x02;
        public const int SHIL_JUMBO = 0x04;
        public const int ILD_TRANSPARENT = 0x00000001;

        public const uint SHCNE_UPDATEITEM = 0x00002000;
        public const uint SHCNE_ASSOCCHANGED = 0x08000000;
        public const uint SHCNF_IDLIST = 0x0000;
        public const uint SHCNF_PATHW = 0x0005;

        #endregion

        #region Structs & Delegates

        public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;

            public SIZE(int cx, int cy)
            {
                this.cx = cx;
                this.cy = cy;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEXW
        {
            public uint cbSize;
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public uint bmiColors;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFOW
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;

            public PROPERTYKEY(string guid, uint id)
            {
                fmtid = new Guid(guid);
                pid = id;
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        public struct PropVariant : IDisposable
        {
            [FieldOffset(0)]
            public ushort vt;

            [FieldOffset(8)]
            public IntPtr pointerValue;

            [FieldOffset(16)]
            public IntPtr reservedPadding;

            public static PropVariant FromString(string value)
            {
                var pv = new PropVariant();
                pv.vt = 31; // VT_LPWSTR
                pv.pointerValue = Marshal.StringToCoTaskMemUni(value ?? string.Empty);
                return pv;
            }

            public string AsString()
            {
                if (vt == 31 && pointerValue != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(pointerValue);
                }
                return null;
            }

            public void Dispose()
            {
                PropVariantClear(ref this);
            }
        }

        public static readonly PROPERTYKEY PKEY_AppUserModel_ID =
            new PROPERTYKEY("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 5);
        public static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchCommand =
            new PROPERTYKEY("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 2);
        public static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchIconResource =
            new PROPERTYKEY("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 3);
        public static readonly PROPERTYKEY PKEY_AppUserModel_RelaunchDisplayNameResource =
            new PROPERTYKEY("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 4);

        #endregion

        #region COM Interfaces

        [Flags]
        public enum SIIGBF : int
        {
            SIIGBF_RESIZETOFIT = 0x00,
            SIIGBF_BIGGERSIZEOK = 0x01,
            SIIGBF_MEMORYONLY = 0x02,
            SIIGBF_ICONONLY = 0x04,
            SIIGBF_THUMBNAILONLY = 0x08,
            SIIGBF_INCACHEONLY = 0x10
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage([In, MarshalAs(UnmanagedType.Struct)] SIZE size, [In] SIIGBF flags, out IntPtr phbm);
        }

        [ComImport]
        [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
            int ReplaceIcon(int i, IntPtr hicon, ref int pi);
            int SetOverlayImage(int iImage, int iOverlay);
            int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
            int Draw(IntPtr pimldp);
            int Remove(int i);
            [PreserveSig]
            int GetIcon(int i, int flags, out IntPtr picon);
        }

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyStore
        {
            int GetCount(out uint cProps);
            int GetAt(uint iProp, out PROPERTYKEY pkey);
            int GetValue(ref PROPERTYKEY key, out PropVariant pv);
            [PreserveSig]
            int SetValue(ref PROPERTYKEY key, ref PropVariant pv);
            [PreserveSig]
            int Commit();
        }

        [ComImport]
        [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
        private class TaskbarListCoClass
        {
        }

        [ComImport]
        [Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ITaskbarList
        {
            [PreserveSig]
            int HrInit();
            [PreserveSig]
            int AddTab(IntPtr hwnd);
            [PreserveSig]
            int DeleteTab(IntPtr hwnd);
            [PreserveSig]
            int ActivateTab(IntPtr hwnd);
            [PreserveSig]
            int SetActiveAlt(IntPtr hwnd);
        }

        public static ITaskbarList CreateTaskbarList()
        {
            return (ITaskbarList)new TaskbarListCoClass();
        }

        #endregion

        #region P/Invoke Signatures

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowExW(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowExW(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassNameW(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLengthW(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        public static void ClearAppStartingCursor()
        {
            try
            {
                PeekMessageW(out _, IntPtr.Zero, 0, 0, 0); // PM_NOREMOVE = 0 signals input-idle to Explorer immediately
            }
            catch
            {
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateIconIndirect(ref ICONINFO icon);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint ExtractIconExW(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHDefExtractIconW(string pszIconFile, int iIcon, uint uFlags, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIconSize);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

        [DllImport("shell32.dll", EntryPoint = "#727")]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

        [DllImport("shell32.dll")]
        public static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitsPerPel, IntPtr lpvBits);

        [DllImport("gdi32.dll")]
        public static extern int GetObject(IntPtr hObject, int nCount, ref BITMAP lpObject);

        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetIconicThumbnail(IntPtr hwnd, IntPtr hbmp, uint dwSITFlags);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetIconicLivePreviewBitmap(IntPtr hwnd, IntPtr hbmp, IntPtr pptClient, uint dwSITFlags);

        [DllImport("dwmapi.dll")]
        public static extern int DwmInvalidateIconicBitmaps(IntPtr hwnd);

        #endregion

        #region Mosaic Icon Generation (128x128 Transparent Canvas, Exact Coordinates)

        /// <summary>
        /// Creates a 32-bit ARGB HICON handle representing the grouped mosaic icon on a transparent 128x128 canvas
        /// with no bordered squircle tile:
        /// - 2 icons: 62x62 each at (0, 0) and (66, 66).
        /// - 3 icons: 58x58 each at (0, 0), (70, 0), and (35, 70) (12px separation).
        /// - 4+ icons (2x2 grid): 58x58 each at (0, 0), (70, 0), (0, 70), and (70, 70) (12px horizontal and vertical padding).
        /// Caller is responsible for calling DestroyIcon on the returned handle when no longer needed.
        /// </summary>
        public static IntPtr CreateGroupMosaicIconHandle(IList<Bitmap> subIcons)
        {
            using (Bitmap mosaic = CreateGroupMosaicBitmap(subIcons))
            {
                return CreateAlphaIconHandle(mosaic);
            }
        }

        /// <summary>
        /// Renders the group mosaic onto a completely transparent 128x128 32bpp ARGB Bitmap (no squircle tile or border).
        /// </summary>
        public static Bitmap CreateGroupMosaicBitmap(IList<Bitmap> subIcons)
        {
            const int canvasSize = 128;
            var canvas = new Bitmap(canvasSize, canvasSize, PixelFormat.Format32bppArgb);

            var validIcons = new List<Bitmap>(4);
            if (subIcons != null)
            {
                for (int i = 0; i < subIcons.Count && validIcons.Count < 4; i++)
                {
                    if (subIcons[i] != null)
                    {
                        validIcons.Add(subIcons[i]);
                    }
                }
            }

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                using (var imageAttrs = new ImageAttributes())
                {
                    imageAttrs.SetWrapMode(WrapMode.TileFlipXY);

                    switch (validIcons.Count)
                    {
                        case 0:
                            DrawEmptyPlaceholderGrid(g);
                            break;

                        case 1:
                            // Single icon centered cleanly on the 128x128 transparent canvas
                            DrawSubIcon(g, validIcons[0], new Rectangle(12, 12, 104, 104), imageAttrs);
                            break;

                        case 2:
                            // 2 icons: 62x62 each at (0, 0) and (66, 66)
                            DrawSubIcon(g, validIcons[0], new Rectangle(0, 0, 62, 62), imageAttrs);
                            DrawSubIcon(g, validIcons[1], new Rectangle(66, 66, 62, 62), imageAttrs);
                            break;

                        case 3:
                            // 3 icons: 58x58 each at (0, 0), (70, 0), and (35, 70) (12px separation)
                            DrawSubIcon(g, validIcons[0], new Rectangle(0, 0, 58, 58), imageAttrs);
                            DrawSubIcon(g, validIcons[1], new Rectangle(70, 0, 58, 58), imageAttrs);
                            DrawSubIcon(g, validIcons[2], new Rectangle(35, 70, 58, 58), imageAttrs);
                            break;

                        default:
                            // 4 icons (2x2 grid): 58x58 each at (0, 0), (70, 0), (0, 70), and (70, 70) (12px padding)
                            DrawSubIcon(g, validIcons[0], new Rectangle(0, 0, 58, 58), imageAttrs);
                            DrawSubIcon(g, validIcons[1], new Rectangle(70, 0, 58, 58), imageAttrs);
                            DrawSubIcon(g, validIcons[2], new Rectangle(0, 70, 58, 58), imageAttrs);
                            DrawSubIcon(g, validIcons[3], new Rectangle(70, 70, 58, 58), imageAttrs);
                            break;
                    }
                }
            }

            return canvas;
        }

        private static void DrawSubIcon(Graphics g, Bitmap icon, Rectangle destRect, ImageAttributes attrs)
        {
            if (icon == null)
                return;

            g.DrawImage(
                icon,
                destRect,
                0,
                0,
                icon.Width,
                icon.Height,
                GraphicsUnit.Pixel,
                attrs);
        }

        private static void DrawEmptyPlaceholderGrid(Graphics g)
        {
            // Minimal subtle 2x2 glyph at the exact 58x58 mosaic slots when a bin has 0 items
            Rectangle[] slots =
            {
                new Rectangle(0, 0, 58, 58),
                new Rectangle(70, 0, 58, 58),
                new Rectangle(0, 70, 58, 58),
                new Rectangle(70, 70, 58, 58)
            };

            Color[] accents =
            {
                Color.FromArgb(215, 96, 165, 250),
                Color.FromArgb(185, 129, 140, 248),
                Color.FromArgb(185, 56, 189, 248),
                Color.FromArgb(160, 148, 163, 184)
            };

            for (int i = 0; i < slots.Length; i++)
            {
                using (GraphicsPath path = CreateRoundedRectPath(slots[i], 14))
                using (var brush = new SolidBrush(accents[i]))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        public static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// Creates a lossless 32bpp alpha HICON from an ARGB Bitmap using CreateDIBSection + CreateIconIndirect.
        /// </summary>
        public static IntPtr CreateAlphaIconHandle(Bitmap bitmap)
        {
            if (bitmap == null)
                return IntPtr.Zero;

            int width = bitmap.Width;
            int height = bitmap.Height;

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height; // top-down DIB
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB

            IntPtr ppvBits;
            IntPtr hbmColor = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out ppvBits, IntPtr.Zero, 0);
            if (hbmColor == IntPtr.Zero || ppvBits == IntPtr.Zero)
            {
                return bitmap.GetHicon();
            }

            IntPtr hbmMask = IntPtr.Zero;
            try
            {
                BitmapData srcData = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes = width * 4;
                    byte[] rowBuffer = new byte[rowBytes];
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr srcRow = IntPtr.Add(srcData.Scan0, y * srcData.Stride);
                        IntPtr dstRow = IntPtr.Add(ppvBits, y * rowBytes);
                        Marshal.Copy(srcRow, rowBuffer, 0, rowBytes);
                        Marshal.Copy(rowBuffer, 0, dstRow, rowBytes);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(srcData);
                }

                hbmMask = CreateBitmap(width, height, 1, 1, IntPtr.Zero);
                var iconInfo = new ICONINFO
                {
                    fIcon = true,
                    xHotspot = 0,
                    yHotspot = 0,
                    hbmMask = hbmMask,
                    hbmColor = hbmColor
                };

                return CreateIconIndirect(ref iconInfo);
            }
            finally
            {
                if (hbmColor != IntPtr.Zero) DeleteObject(hbmColor);
                if (hbmMask != IntPtr.Zero) DeleteObject(hbmMask);
            }
        }

        /// <summary>
        /// Creates a top-down 32bpp premultiplied ARGB HBITMAP suitable for DwmSetIconicThumbnail / DwmSetIconicLivePreviewBitmap.
        /// </summary>
        public static IntPtr CreateDwmArgbHBitmap(Bitmap bitmap)
        {
            if (bitmap == null)
                return IntPtr.Zero;

            int width = bitmap.Width;
            int height = bitmap.Height;

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height; // top-down DIB required by DWM
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;

            IntPtr ppvBits;
            IntPtr hBitmap = CreateDIBSection(IntPtr.Zero, ref bmi, 0, out ppvBits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || ppvBits == IntPtr.Zero)
                return IntPtr.Zero;

            BitmapData srcData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppPArgb);
            try
            {
                int rowBytes = width * 4;
                byte[] rowBuffer = new byte[rowBytes];
                for (int y = 0; y < height; y++)
                {
                    IntPtr srcRow = IntPtr.Add(srcData.Scan0, y * srcData.Stride);
                    IntPtr dstRow = IntPtr.Add(ppvBits, y * rowBytes);
                    Marshal.Copy(srcRow, rowBuffer, 0, rowBytes);
                    Marshal.Copy(rowBuffer, 0, dstRow, rowBytes);
                }
            }
            finally
            {
                bitmap.UnlockBits(srcData);
            }

            return hBitmap;
        }

        #endregion

        #region High-DPI Shell & Resource Icon Extraction

        public static Bitmap ExtractHighResIconBitmap(string path, int resourceIndex = 0, int desiredSize = 256)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

            // Handle "path,index" syntax if provided in string
            if (resourceIndex == 0 && expanded.Contains(",") && !File.Exists(expanded) && !Directory.Exists(expanded))
            {
                int lastComma = expanded.LastIndexOf(',');
                if (lastComma > 1 && int.TryParse(expanded.Substring(lastComma + 1).Trim(), out int parsedIdx))
                {
                    expanded = expanded.Substring(0, lastComma).Trim().Trim('"');
                    resourceIndex = parsedIdx;
                }
            }

            // Direct image files (.png, .jpg, .bmp, .ico)
            string ext = Path.GetExtension(expanded)?.ToLowerInvariant() ?? string.Empty;
            if (File.Exists(expanded))
            {
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp")
                {
                    try
                    {
                        using (var fs = new FileStream(expanded, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var img = Image.FromStream(fs))
                        {
                            return ResizeToSquareBitmap(img, desiredSize);
                        }
                    }
                    catch { }
                }
                else if (ext == ".ico")
                {
                    Bitmap icoBmp = LoadBestFrameFromIcoFile(expanded, desiredSize);
                    if (icoBmp != null)
                        return icoBmp;
                }
            }

            // Indexed icon from .dll / .exe (e.g. imageres.dll, shell32.dll)
            if (resourceIndex != 0 || ext == ".dll" || ext == ".mun")
            {
                Bitmap indexed = ExtractIndexedIconBitmap(expanded, resourceIndex, desiredSize);
                if (indexed != null)
                    return indexed;
            }

            // 1. Primary Windows 11 High-DPI path: IShellItemImageFactory (handles .exe, .lnk, UWP shortcuts, folders, files at 256x256)
            if (File.Exists(expanded) || Directory.Exists(expanded))
            {
                Bitmap shellFactoryBmp = TryExtractViaShellItemImageFactory(expanded, desiredSize);
                if (shellFactoryBmp != null)
                    return NormalizeJumboIconPadding(shellFactoryBmp);
            }

            // 2. Secondary fallback: SHGetFileInfo + SHIL_JUMBO ImageList
            Bitmap jumboBmp = TryExtractViaJumboImageList(expanded);
            if (jumboBmp != null)
                return NormalizeJumboIconPadding(jumboBmp);

            // 3. Final fallback: Icon.ExtractAssociatedIcon
            if (File.Exists(expanded))
            {
                try
                {
                    using (Icon assoc = Icon.ExtractAssociatedIcon(expanded))
                    {
                        if (assoc != null)
                            return assoc.ToBitmap();
                    }
                }
                catch { }
            }

            return null;
        }

        public static Bitmap ExtractIndexedIconBitmap(string filePath, int iconIndex, int desiredSize = 256)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            string expanded = Environment.ExpandEnvironmentVariables(filePath);
            IntPtr hLarge = IntPtr.Zero;
            IntPtr hSmall = IntPtr.Zero;
            try
            {
                int hr = SHDefExtractIconW(expanded, iconIndex, 0, out hLarge, out hSmall, (uint)desiredSize);
                if (hr == 0 && hLarge != IntPtr.Zero)
                {
                    return BitmapFromHIconAlpha(hLarge, desiredSize);
                }
            }
            catch { }
            finally
            {
                if (hLarge != IntPtr.Zero) DestroyIcon(hLarge);
                if (hSmall != IntPtr.Zero) DestroyIcon(hSmall);
            }

            return null;
        }

        private static Bitmap TryExtractViaShellItemImageFactory(string path, int size)
        {
            Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            IShellItemImageFactory factory = null;
            IntPtr hBitmap = IntPtr.Zero;

            try
            {
                int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
                if (hr != 0 || factory == null)
                    return null;

                hr = factory.GetImage(new SIZE(size, size), SIIGBF.SIIGBF_ICONONLY | SIIGBF.SIIGBF_BIGGERSIZEOK, out hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero)
                    return null;

                return BitmapFromHBitmap32(hBitmap);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hBitmap != IntPtr.Zero)
                    DeleteObject(hBitmap);
                if (factory != null)
                    Marshal.ReleaseComObject(factory);
            }
        }

        private static Bitmap TryExtractViaJumboImageList(string path)
        {
            var shinfo = new SHFILEINFOW();
            uint flags = SHGFI_SYSICONINDEX;
            uint attrs = Directory.Exists(path) ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                flags |= SHGFI_USEFILEATTRIBUTES;
            }

            IntPtr res = SHGetFileInfoW(path, attrs, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
            if (res == IntPtr.Zero)
                return null;

            Guid iidImageList = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
            IImageList imageList = null;
            IntPtr hIcon = IntPtr.Zero;
            try
            {
                int hr = SHGetImageList(SHIL_JUMBO, ref iidImageList, out imageList);
                if (hr != 0 || imageList == null)
                {
                    hr = SHGetImageList(SHIL_EXTRALARGE, ref iidImageList, out imageList);
                }

                if (hr != 0 || imageList == null)
                    return null;

                hr = imageList.GetIcon(shinfo.iIcon, ILD_TRANSPARENT, out hIcon);
                if (hr != 0 || hIcon == IntPtr.Zero)
                    return null;

                return BitmapFromHIconAlpha(hIcon, 256);
            }
            catch
            {
                return null;
            }
            finally
            {
                if (hIcon != IntPtr.Zero)
                    DestroyIcon(hIcon);
                if (imageList != null)
                    Marshal.ReleaseComObject(imageList);
            }
        }

        private static Bitmap BitmapFromHBitmap32(IntPtr hBitmap)
        {
            var bm = new BITMAP();
            if (GetObject(hBitmap, Marshal.SizeOf(typeof(BITMAP)), ref bm) == 0)
                return null;

            int width = bm.bmWidth;
            int height = Math.Abs(bm.bmHeight);
            if (width <= 0 || height <= 0)
                return null;

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height; // top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0;

            byte[] pixels = new byte[width * height * 4];
            IntPtr hdc = CreateCompatibleDC(IntPtr.Zero);
            try
            {
                int lines = GetDIBits(hdc, hBitmap, 0, (uint)height, pixels, ref bmi, 0);
                if (lines == 0)
                    return null;
            }
            finally
            {
                if (hdc != IntPtr.Zero)
                    DeleteDC(hdc);
            }

            bool hasAnyAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    hasAnyAlpha = true;
                    break;
                }
            }

            if (!hasAnyAlpha)
            {
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }
            }

            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = width * 4;
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(pixels, y * rowBytes, IntPtr.Add(data.Scan0, y * data.Stride), rowBytes);
                }
            }
            finally
            {
                result.UnlockBits(data);
            }

            return result;
        }

        private static Bitmap BitmapFromHIconAlpha(IntPtr hIcon, int fallbackSize)
        {
            if (hIcon == IntPtr.Zero)
                return null;

            try
            {
                using (Icon ic = Icon.FromHandle(hIcon))
                {
                    int w = ic.Width > 0 ? ic.Width : fallbackSize;
                    int h = ic.Height > 0 ? ic.Height : fallbackSize;
                    var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.DrawIcon(ic, new Rectangle(0, 0, w, h));
                    }
                    return bmp;
                }
            }
            catch
            {
                return null;
            }
        }

        private static Bitmap LoadBestFrameFromIcoFile(string icoPath, int desiredSize)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(icoPath);
                if (bytes.Length > 22 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0)
                {
                    int count = bytes[4] | (bytes[5] << 8);
                    int bestOffset = -1;
                    int bestLength = 0;
                    int bestScore = -1;

                    for (int i = 0; i < count; i++)
                    {
                        int entryPos = 6 + i * 16;
                        if (entryPos + 16 > bytes.Length)
                            break;

                        int w = bytes[entryPos] == 0 ? 256 : bytes[entryPos];
                        int h = bytes[entryPos + 1] == 0 ? 256 : bytes[entryPos + 1];
                        int bpp = bytes[entryPos + 6] | (bytes[entryPos + 7] << 8);
                        int len = BitConverter.ToInt32(bytes, entryPos + 8);
                        int offset = BitConverter.ToInt32(bytes, entryPos + 12);

                        if (offset >= 0 && len > 0 && offset + len <= bytes.Length)
                        {
                            int sizeScore = w >= desiredSize ? 10000 - (w - desiredSize) : w * 20;
                            int score = sizeScore + bpp;
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestOffset = offset;
                                bestLength = len;
                            }
                        }
                    }

                    // Check if the best entry is a modern PNG-compressed frame (\x89PNG)
                    if (bestOffset >= 0 && bestLength > 8 &&
                        bytes[bestOffset] == 0x89 && bytes[bestOffset + 1] == 0x50 &&
                        bytes[bestOffset + 2] == 0x4E && bytes[bestOffset + 3] == 0x47)
                    {
                        using (var ms = new MemoryStream(bytes, bestOffset, bestLength, false))
                        using (var png = Image.FromStream(ms))
                        {
                            return ResizeToSquareBitmap(png, desiredSize);
                        }
                    }
                }

                using (var fs = new FileStream(icoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var icon = new Icon(fs, desiredSize, desiredSize))
                {
                    return icon.ToBitmap();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Fixes the classic Windows SHIL_JUMBO quirk where legacy apps lacking 256x256 icons render as a tiny 48x48
        /// image in the top-left corner of an otherwise empty 256x256 canvas.
        /// </summary>
        private static Bitmap NormalizeJumboIconPadding(Bitmap bmp)
        {
            if (bmp == null || bmp.Width < 128 || bmp.Height < 128)
                return bmp;

            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
            try
            {
                byte[] row = new byte[bmp.Width * 4];
                for (int y = 0; y < bmp.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        byte alpha = row[x * 4 + 3];
                        if (alpha > 8)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            if (maxX < minX || maxY < minY)
                return bmp;

            // Detect top-left-quadrant-only legacy icon in a 256x256 jumbo buffer
            if (maxX < bmp.Width / 2 && maxY < bmp.Height / 2 && minX < 24 && minY < 24)
            {
                int side = Math.Max(maxX + minX + 1, maxY + minY + 1);
                side = Math.Max(32, Math.Min(bmp.Width, side));
                var cropped = new Bitmap(side, side, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(cropped))
                {
                    g.Clear(Color.Transparent);
                    g.DrawImage(bmp, new Rectangle(0, 0, side, side), new Rectangle(0, 0, side, side), GraphicsUnit.Pixel);
                }
                bmp.Dispose();
                return cropped;
            }

            return bmp;
        }

        public static Bitmap ResizeToSquareBitmap(Image img, int size)
        {
            var dest = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(dest))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                float scale = Math.Min((float)size / img.Width, (float)size / img.Height);
                int w = Math.Max(1, (int)Math.Round(img.Width * scale));
                int h = Math.Max(1, (int)Math.Round(img.Height * scale));
                int x = (size - w) / 2;
                int y = (size - h) / 2;

                using (var attrs = new ImageAttributes())
                {
                    attrs.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(img, new Rectangle(x, y, w, h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);
                }
            }
            return dest;
        }

        #endregion

        #region Multi-Resolution PNG .ICO Writer

        public static void SaveMultiFrameIconFile(Bitmap masterBitmap, string outputIcoPath)
        {
            if (masterBitmap == null)
                throw new ArgumentNullException(nameof(masterBitmap));

            string dir = Path.GetDirectoryName(outputIcoPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            int[] sizes = { 256, 128, 64, 48, 32, 24, 16 };
            var pngPayloads = new List<byte[]>(sizes.Length);

            foreach (int sz in sizes)
            {
                using (Bitmap frame = ResizeToSquareBitmap(masterBitmap, sz))
                using (var ms = new MemoryStream())
                {
                    frame.Save(ms, ImageFormat.Png);
                    pngPayloads.Add(ms.ToArray());
                }
            }

            using (var fs = new FileStream(outputIcoPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var bw = new BinaryWriter(fs))
            {
                // ICONDIR header
                bw.Write((ushort)0); // Reserved
                bw.Write((ushort)1); // Type = 1 (Icon)
                bw.Write((ushort)sizes.Length);

                int dataOffset = 6 + (16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int sz = sizes[i];
                    byte dimByte = sz >= 256 ? (byte)0 : (byte)sz;
                    byte[] payload = pngPayloads[i];

                    bw.Write(dimByte);           // Width
                    bw.Write(dimByte);           // Height
                    bw.Write((byte)0);           // ColorCount
                    bw.Write((byte)0);           // Reserved
                    bw.Write((ushort)1);         // Planes
                    bw.Write((ushort)32);        // BitCount
                    bw.Write(payload.Length);    // BytesInRes
                    bw.Write(dataOffset);        // ImageOffset

                    dataOffset += payload.Length;
                }

                for (int i = 0; i < pngPayloads.Count; i++)
                {
                    bw.Write(pngPayloads[i]);
                }
            }
        }

        #endregion

        #region Windows 11 DWM & Taskbar Window Helpers

        public static void ApplyWindows11WindowStyling(
            IntPtr hwnd,
            bool useMica = false,
            bool smallRound = false,
            int? captionColorRef = null)
        {
            if (hwnd == IntPtr.Zero)
                return;

            try
            {
                int dark = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

                int corner = smallRound ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

                if (captionColorRef.HasValue)
                {
                    int backdropNone = DWMSBT_NONE;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropNone, sizeof(int));

                    int color = captionColorRef.Value;
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
                }
                else if (useMica)
                {
                    int backdrop = DWMSBT_MAINWINDOW;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                }
            }
            catch
            {
                // Gracefully ignore on older builds
            }
        }

        public static void SetWindowAppUserModelProperties(
            IntPtr hwnd,
            string appUserModelId,
            string relaunchCommand,
            string displayName,
            string iconResourcePath)
        {
            if (hwnd == IntPtr.Zero)
                return;

            Guid iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
            IPropertyStore store = null;
            try
            {
                if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store == null)
                    return;

                SetPropString(store, PKEY_AppUserModel_ID, appUserModelId);
                if (!string.IsNullOrWhiteSpace(relaunchCommand))
                    SetPropString(store, PKEY_AppUserModel_RelaunchCommand, relaunchCommand);
                if (!string.IsNullOrWhiteSpace(displayName))
                    SetPropString(store, PKEY_AppUserModel_RelaunchDisplayNameResource, displayName);
                if (!string.IsNullOrWhiteSpace(iconResourcePath))
                    SetPropString(store, PKEY_AppUserModel_RelaunchIconResource, iconResourcePath + ",0");

                store.Commit();
            }
            catch
            {
            }
            finally
            {
                if (store != null)
                    Marshal.ReleaseComObject(store);
            }
        }

        private static void SetPropString(IPropertyStore store, PROPERTYKEY key, string value)
        {
            PropVariant pv = PropVariant.FromString(value);
            try
            {
                store.SetValue(ref key, ref pv);
            }
            finally
            {
                pv.Dispose();
            }
        }

        public static void NotifyShellItemUpdated(string filePath)
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                IntPtr pathPtr = Marshal.StringToHGlobalUni(filePath);
                try
                {
                    SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW, pathPtr, IntPtr.Zero);
                }
                finally
                {
                    Marshal.FreeHGlobal(pathPtr);
                }
            }

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// Immediately hides and dismisses any active Windows Taskbar hover preview popup (TaskListThumbnailWnd).
        /// </summary>
        public static bool DismissTaskbarThumbnailWindows()
        {
            bool dismissedAny = false;
            IntPtr thumb = IntPtr.Zero;
            int safety = 0;
            while (safety++ < 16)
            {
                thumb = FindWindowExW(IntPtr.Zero, thumb, "TaskListThumbnailWnd", null);
                if (thumb == IntPtr.Zero)
                    break;

                if (IsWindowVisible(thumb))
                {
                    ShowWindow(thumb, SW_HIDE);
                    PostMessageW(thumb, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    dismissedAny = true;
                }
            }

            return dismissedAny;
        }

        public static bool IsWindowCloaked(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
            {
                return cloaked != 0;
            }
            return false;
        }

        public static string GetWindowTextSafe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return string.Empty;

            int len = GetWindowTextLengthW(hwnd);
            if (len <= 0)
                return string.Empty;

            var sb = new System.Text.StringBuilder(len + 2);
            int read = GetWindowTextW(hwnd, sb, sb.Capacity);
            return read > 0 ? sb.ToString() : string.Empty;
        }

        public static string GetWindowClassNameSafe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return string.Empty;

            var sb = new System.Text.StringBuilder(256);
            int read = GetClassNameW(hwnd, sb, sb.Capacity);
            return read > 0 ? sb.ToString() : string.Empty;
        }

        public static bool TryGetProcessExecutablePath(uint pid, out string exePath)
        {
            exePath = null;
            if (pid == 0)
                return false;

            IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProc == IntPtr.Zero)
                return false;

            try
            {
                var sb = new System.Text.StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageNameW(hProc, 0, sb, ref size) && size > 0)
                {
                    exePath = sb.ToString(0, size);
                    return !string.IsNullOrWhiteSpace(exePath);
                }
            }
            catch
            {
            }
            finally
            {
                CloseHandle(hProc);
            }

            return false;
        }

        public static void ActivateAndRestoreWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                return;

            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }

            SetForegroundWindow(hwnd);
        }

        #endregion
    }
}
