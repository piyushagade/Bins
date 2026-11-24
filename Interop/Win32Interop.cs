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
        public const uint WM_MOUSELEAVE = 0x02A3;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_DWMSENDICONICTHUMBNAIL = 0x0323;
        public const int WM_DWMSENDICONICLIVEPREVIEWBITMAP = 0x0326;
        public const int WM_USER = 0x0400;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int VK_LBUTTON = 0x01;
        public const int VK_TAB = 0x09;
        public const int VK_RETURN = 0x0D;
        public const int VK_MENU = 0x12;
        public const int VK_ESCAPE = 0x1B;
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;
        public const int VK_LMENU = 0xA4;
        public const int VK_RMENU = 0xA5;

        public const int WH_KEYBOARD_LL = 13;
        public const int HC_ACTION = 0;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const uint LLKHF_ALTDOWN = 0x0020;

        public const uint FO_MOVE = 0x0001;
        public const uint FO_COPY = 0x0002;
        public const uint FO_DELETE = 0x0003;
        public const ushort FOF_ALLOWUNDO = 0x0040;
        public const ushort FOF_NOCONFIRMMKDIR = 0x0200;

        public const uint SHERB_NOCONFIRMATION = 0x00000001;
        public const uint SHERB_NOPROGRESSUI = 0x00000002;
        public const uint SHERB_NOSOUND = 0x00000004;

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

        public const uint WS_EX_TOPMOST = 0x00000008;
        public const uint WS_EX_TRANSPARENT = 0x00000020;
        public const uint WS_EX_TOOLWINDOW = 0x00000080;
        public const uint WS_EX_APPWINDOW = 0x00040000;
        public const uint WS_EX_LAYERED = 0x00080000;
        public const uint WS_EX_NOACTIVATE = 0x08000000;

        public const uint ULW_ALPHA = 0x00000002;
        public const byte AC_SRC_OVER = 0x00;
        public const byte AC_SRC_ALPHA = 0x01;
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        public const int SW_HIDE = 0;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_SHOW = 5;
        public const int SW_MINIMIZE = 6;
        public const int SW_SHOWMINNOACTIVE = 7;
        public const int SW_RESTORE = 9;

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const uint GW_HWNDNEXT = 2;
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
        public const uint EVENT_SYSTEM_SWITCHSTART = 0x0014;
        public const uint EVENT_SYSTEM_SWITCHEND = 0x0015;
        public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        public const uint EVENT_OBJECT_SHOW = 0x8002;
        public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;
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

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

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
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
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

            [FieldOffset(8)]
            public short boolValue;

            [FieldOffset(16)]
            public IntPtr reservedPadding;

            public static PropVariant FromString(string value)
            {
                var pv = new PropVariant();
                pv.vt = 31; // VT_LPWSTR
                pv.pointerValue = Marshal.StringToCoTaskMemUni(value ?? string.Empty);
                return pv;
            }

            public static PropVariant FromBool(bool value)
            {
                var pv = new PropVariant();
                pv.vt = 11; // VT_BOOL
                pv.boolValue = (short)(value ? -1 : 0);
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

        public static readonly PROPERTYKEY PKEY_Title =
            new PROPERTYKEY("F29F85E0-4FF9-1068-AB91-08002B27B3D9", 2);
        public static readonly PROPERTYKEY PKEY_AppUserModel_ID =
            new PROPERTYKEY("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 5);
        public static readonly PROPERTYKEY PKEY_AppUserModel_IsDestListSeparator =
            new PROPERTYKEY("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 6);
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

        [ComImport]
        [Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IObjectArray
        {
            [PreserveSig]
            int GetCount(out uint pcObjects);
            [PreserveSig]
            int GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        }

        [ComImport]
        [Guid("5632B1A4-E38A-400A-928A-D4CD63230295")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IObjectCollection
        {
            // Inherited IObjectArray vtable slots
            [PreserveSig]
            int GetCount(out uint pcObjects);
            [PreserveSig]
            int GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

            // IObjectCollection vtable slots
            [PreserveSig]
            int AddObject([MarshalAs(UnmanagedType.Interface)] object punk);
            [PreserveSig]
            int AddFromArray([MarshalAs(UnmanagedType.Interface)] IObjectArray poaSource);
            [PreserveSig]
            int RemoveObjectAt(uint uiIndex);
            [PreserveSig]
            int Clear();
        }

        [ComImport]
        [Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A")]
        private class EnumerableObjectCollectionCoClass
        {
        }

        [ComImport]
        [Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ICustomDestinationList
        {
            [PreserveSig]
            int SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            [PreserveSig]
            int BeginList(out uint pcMinSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IObjectArray ppv);
            [PreserveSig]
            int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, [MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
            [PreserveSig]
            int AppendKnownCategory(int category);
            [PreserveSig]
            int AddUserTasks([MarshalAs(UnmanagedType.Interface)] IObjectArray poa);
            [PreserveSig]
            int CommitList();
            [PreserveSig]
            int GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IObjectArray ppv);
            [PreserveSig]
            int DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            [PreserveSig]
            int AbortList();
        }

        [ComImport]
        [Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6")]
        private class DestinationListCoClass
        {
        }

        public static ICustomDestinationList CreateCustomDestinationList()
        {
            return (ICustomDestinationList)new DestinationListCoClass();
        }

        public static IObjectCollection CreateObjectCollection()
        {
            return (IObjectCollection)new EnumerableObjectCollectionCoClass();
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

        [DllImport("user32.dll")]
        public static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

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

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetWindowsHookExW(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

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

        public const int WM_GETMINMAXINFO = 0x0024;

        [StructLayout(LayoutKind.Sequential)]
        public struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);

        public static bool ShellCopyOrMoveFilesToFolder(IEnumerable<string> sourcePaths, string destinationFolder, bool copy)
        {
            if (sourcePaths == null || string.IsNullOrWhiteSpace(destinationFolder) || !Directory.Exists(destinationFolder))
                return false;

            var validSources = new List<string>();
            foreach (string raw in sourcePaths)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                string full = raw.Trim().Trim('"');
                if (File.Exists(full) || Directory.Exists(full))
                {
                    // Avoid moving a folder into itself
                    if (string.Equals(Path.GetFullPath(full).TrimEnd('\\'), Path.GetFullPath(destinationFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        continue;
                    validSources.Add(full);
                }
            }

            if (validSources.Count == 0)
                return false;

            // SHFileOperationW requires double-null-terminated strings
            string pFrom = string.Join("\0", validSources) + "\0\0";
            string pTo = Path.GetFullPath(destinationFolder) + "\0\0";

            var op = new SHFILEOPSTRUCTW
            {
                hwnd = IntPtr.Zero,
                wFunc = copy ? FO_COPY : FO_MOVE,
                pFrom = pFrom,
                pTo = pTo,
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR)
            };

            try
            {
                int res = SHFileOperationW(ref op);
                return res == 0 && !op.fAnyOperationsAborted;
            }
            catch
            {
                return false;
            }
        }

        public static bool ShellSendFilesToRecycleBin(IEnumerable<string> sourcePaths)
        {
            if (sourcePaths == null)
                return false;

            var validSources = new List<string>();
            foreach (string raw in sourcePaths)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                string full = raw.Trim().Trim('"');
                if (File.Exists(full) || Directory.Exists(full))
                {
                    validSources.Add(full);
                }
            }

            if (validSources.Count == 0)
                return false;

            string pFrom = string.Join("\0", validSources) + "\0\0";
            var op = new SHFILEOPSTRUCTW
            {
                hwnd = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = pFrom,
                pTo = null,
                fFlags = FOF_ALLOWUNDO
            };

            try
            {
                int res = SHFileOperationW(ref op);
                return res == 0 && !op.fAnyOperationsAborted;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHEmptyRecycleBinW(IntPtr hwnd, string pszRootPath, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool LockWorkStation();

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        public static bool TryGetRecycleBinStatus(out long itemCount, out long totalBytes)
        {
            itemCount = 0;
            totalBytes = 0;
            try
            {
                var info = new SHQUERYRBINFO
                {
                    cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO))
                };
                if (SHQueryRecycleBinW(null, ref info) == 0)
                {
                    itemCount = info.i64NumItems;
                    totalBytes = info.i64Size;
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        public static bool EmptyRecycleBin(IntPtr ownerHwnd, bool noConfirm = false)
        {
            try
            {
                uint flags = noConfirm ? SHERB_NOCONFIRMATION : 0u;
                int hr = SHEmptyRecycleBinW(ownerHwnd, null, flags);
                return hr == 0;
            }
            catch
            {
                return false;
            }
        }

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

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        public static void TrimProcessWorkingSet()
        {
            try
            {
                SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
            }
            catch
            {
            }
        }

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

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr hdcDst,
            ref POINT pptDst,
            ref SIZE psize,
            IntPtr hdcSrc,
            ref POINT pptSrc,
            uint crKey,
            ref BLENDFUNCTION pblend,
            uint dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

        public static bool IsTaskbarHiddenOrCoveredByFullscreen(IntPtr trayHwnd)
        {
            if (trayHwnd == IntPtr.Zero || !IsWindowVisible(trayHwnd))
                return true;

            if (!GetWindowRect(trayHwnd, out RECT trayRect))
                return true;

            IntPtr hMon = MonitorFromWindow(trayHwnd, MONITOR_DEFAULTTONEAREST);
            if (hMon != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (GetMonitorInfoW(hMon, ref mi))
                {
                    // Auto-hidden taskbar slid off the monitor edge
                    int visibleTop = Math.Max(trayRect.Top, mi.rcMonitor.Top);
                    int visibleBottom = Math.Min(trayRect.Bottom, mi.rcMonitor.Bottom);
                    int visibleLeft = Math.Max(trayRect.Left, mi.rcMonitor.Left);
                    int visibleRight = Math.Min(trayRect.Right, mi.rcMonitor.Right);
                    if ((visibleBottom - visibleTop) < 12 || (visibleRight - visibleLeft) < 12)
                        return true;

                    // Fullscreen app covering the entire monitor (e.g. video player or game)
                    IntPtr fg = GetForegroundWindow();
                    if (fg != IntPtr.Zero && fg != trayHwnd && IsWindowVisible(fg) && !IsIconic(fg))
                    {
                        IntPtr fgMon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
                        if (fgMon == hMon && GetWindowRect(fg, out RECT fgRect))
                        {
                            if (fgRect.Left <= mi.rcMonitor.Left &&
                                fgRect.Top <= mi.rcMonitor.Top &&
                                fgRect.Right >= mi.rcMonitor.Right &&
                                fgRect.Bottom >= mi.rcMonitor.Bottom)
                            {
                                string cls = GetWindowClassNameSafe(fg);
                                if (!string.Equals(cls, "Progman", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(cls, "WorkerW", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(cls, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(cls, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct PSIZE
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fSourceClientAreaOnly;
        }

        public const int DWM_TNP_RECTDESTINATION = 0x00000001;
        public const int DWM_TNP_RECTSOURCE = 0x00000002;
        public const int DWM_TNP_OPACITY = 0x00000004;
        public const int DWM_TNP_VISIBLE = 0x00000008;
        public const int DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

        [DllImport("dwmapi.dll")]
        public static extern int DwmRegisterThumbnail(IntPtr hwndDestination, IntPtr hwndSource, out IntPtr phThumbnailId);

        [DllImport("dwmapi.dll")]
        public static extern int DwmUnregisterThumbnail(IntPtr hThumbnailId);

        [DllImport("dwmapi.dll")]
        public static extern int DwmQueryThumbnailSourceSize(IntPtr hThumbnail, out PSIZE pSize);

        [DllImport("dwmapi.dll")]
        public static extern int DwmUpdateThumbnailProperties(IntPtr hThumbnailId, ref DWM_THUMBNAIL_PROPERTIES ptnProperties);

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
        public static IntPtr CreateGroupMosaicIconHandle(IList<Bitmap> subIcons, bool showBackplate = false, Color? tintColor = null)
        {
            using (Bitmap mosaic = CreateGroupMosaicBitmap(subIcons, showBackplate, tintColor))
            {
                return CreateAlphaIconHandle(mosaic);
            }
        }

        private static int BlendByte(int a, int b, double weightA)
        {
            int val = (int)Math.Round(a * weightA + b * (1.0 - weightA));
            return Math.Max(0, Math.Min(255, val));
        }

        /// <summary>
        /// Renders the group mosaic onto a 128x128 32bpp ARGB Bitmap (transparent or with a frosted/tinted squircle backplate).
        /// </summary>
        public static Bitmap CreateGroupMosaicBitmap(IList<Bitmap> subIcons, bool showBackplate = false, Color? tintColor = null)
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

                if (showBackplate)
                {
                    DrawMosaicBackplate(g, tintColor);
                }

                using (var imageAttrs = new ImageAttributes())
                {
                    imageAttrs.SetWrapMode(WrapMode.TileFlipXY);

                    if (showBackplate)
                    {
                        switch (validIcons.Count)
                        {
                            case 0:
                                DrawEmptyPlaceholderGrid(g, inset: true);
                                break;
                            case 1:
                                DrawSubIcon(g, validIcons[0], new Rectangle(17, 17, 94, 94), imageAttrs);
                                break;
                            case 2:
                                DrawSubIcon(g, validIcons[0], new Rectangle(9, 9, 52, 52), imageAttrs);
                                DrawSubIcon(g, validIcons[1], new Rectangle(67, 67, 52, 52), imageAttrs);
                                break;
                            case 3:
                                DrawSubIcon(g, validIcons[0], new Rectangle(9, 9, 51, 51), imageAttrs);
                                DrawSubIcon(g, validIcons[1], new Rectangle(68, 9, 51, 51), imageAttrs);
                                DrawSubIcon(g, validIcons[2], new Rectangle(38, 68, 51, 51), imageAttrs);
                                break;
                            default:
                                DrawSubIcon(g, validIcons[0], new Rectangle(9, 9, 51, 51), imageAttrs);
                                DrawSubIcon(g, validIcons[1], new Rectangle(68, 9, 51, 51), imageAttrs);
                                DrawSubIcon(g, validIcons[2], new Rectangle(9, 68, 51, 51), imageAttrs);
                                DrawSubIcon(g, validIcons[3], new Rectangle(68, 68, 51, 51), imageAttrs);
                                break;
                        }
                    }
                    else
                    {
                        switch (validIcons.Count)
                        {
                            case 0:
                                DrawEmptyPlaceholderGrid(g, inset: false);
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
            }

            return canvas;
        }

        private static void DrawMosaicBackplate(Graphics g, Color? tintColor)
        {
            // Full-bleed 128x128 backplate so Bins with a backplate match the full footprint of unplated Bins
            var fullRect = new Rectangle(0, 0, 128, 128);
            var strokeRect = new Rectangle(1, 1, 126, 126);
            Color topFill = tintColor.HasValue
                ? Color.FromArgb(232, BlendByte(tintColor.Value.R, 30, 0.72), BlendByte(tintColor.Value.G, 32, 0.72), BlendByte(tintColor.Value.B, 38, 0.72))
                : Color.FromArgb(215, 48, 52, 62);
            Color botFill = tintColor.HasValue
                ? Color.FromArgb(222, BlendByte(tintColor.Value.R, 16, 0.48), BlendByte(tintColor.Value.G, 18, 0.48), BlendByte(tintColor.Value.B, 24, 0.48))
                : Color.FromArgb(205, 30, 32, 40);
            Color strokeCol = tintColor.HasValue
                ? Color.FromArgb(145, BlendByte(tintColor.Value.R, 255, 0.62), BlendByte(tintColor.Value.G, 255, 0.62), BlendByte(tintColor.Value.B, 255, 0.62))
                : Color.FromArgb(76, 255, 255, 255);

            using (GraphicsPath fillPath = CreateRoundedRectPath(fullRect, 18))
            using (GraphicsPath strokePath = CreateRoundedRectPath(strokeRect, 17))
            using (var fillBrush = new LinearGradientBrush(fullRect, topFill, botFill, LinearGradientMode.Vertical))
            using (var borderPen = new Pen(strokeCol, 1.8f))
            {
                g.FillPath(fillBrush, fillPath);
                g.DrawPath(borderPen, strokePath);
            }
        }

        /// <summary>
        /// Renders up to 9 sub-icons into a crisp 3x3 mini-grid on a 128x128 32bpp ARGB Bitmap.
        /// </summary>
        public static Bitmap CreateGroupMosaic3x3Bitmap(IList<Bitmap> subIcons, bool showBackplate = false, Color? tintColor = null)
        {
            const int canvasSize = 128;
            var canvas = new Bitmap(canvasSize, canvasSize, PixelFormat.Format32bppArgb);

            var validIcons = new List<Bitmap>(9);
            if (subIcons != null)
            {
                for (int i = 0; i < subIcons.Count && validIcons.Count < 9; i++)
                {
                    if (subIcons[i] != null)
                        validIcons.Add(subIcons[i]);
                }
            }

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                if (showBackplate)
                {
                    DrawMosaicBackplate(g, tintColor);
                }

                if (validIcons.Count == 0)
                {
                    DrawEmptyPlaceholderGrid(g, inset: showBackplate);
                    return canvas;
                }

                using (var imageAttrs = new ImageAttributes())
                {
                    imageAttrs.SetWrapMode(WrapMode.TileFlipXY);
                    int margin = showBackplate ? 9 : 4;
                    int gap = showBackplate ? 5 : 6;
                    int cell = (canvasSize - 2 * margin - 2 * gap) / 3;

                    for (int i = 0; i < validIcons.Count; i++)
                    {
                        int row = i / 3;
                        int col = i % 3;
                        int x = margin + col * (cell + gap);
                        int y = margin + row * (cell + gap);
                        DrawSubIcon(g, validIcons[i], new Rectangle(x, y, cell, cell), imageAttrs);
                    }
                }
            }

            return canvas;
        }

        /// <summary>
        /// Renders up to 3 sub-icons as a layered, slightly fanned/tilted stack of cards on a 128x128 32bpp ARGB Bitmap.
        /// </summary>
        public static Bitmap CreateFannedStackBitmap(IList<Bitmap> subIcons, bool showBackplate = false, Color? tintColor = null)
        {
            const int canvasSize = 128;
            var canvas = new Bitmap(canvasSize, canvasSize, PixelFormat.Format32bppArgb);

            var validIcons = new List<Bitmap>(3);
            if (subIcons != null)
            {
                for (int i = 0; i < subIcons.Count && validIcons.Count < 3; i++)
                {
                    if (subIcons[i] != null)
                        validIcons.Add(subIcons[i]);
                }
            }

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                if (showBackplate)
                {
                    DrawMosaicBackplate(g, tintColor);
                }

                if (validIcons.Count == 0)
                {
                    DrawEmptyPlaceholderGrid(g, inset: showBackplate);
                    return canvas;
                }

                using (var imageAttrs = new ImageAttributes())
                {
                    imageAttrs.SetWrapMode(WrapMode.TileFlipXY);

                    // Draw back-to-front: index 2 (left/back), index 1 (right/mid), index 0 (front/center)
                    if (validIcons.Count >= 3)
                    {
                        DrawTiltedCardIcon(g, validIcons[2], cx: 44f, cy: 62f, size: 62, angleDeg: -13f, imageAttrs, cardAlpha: 225);
                    }
                    if (validIcons.Count >= 2)
                    {
                        DrawTiltedCardIcon(g, validIcons[1], cx: 84f, cy: 60f, size: 64, angleDeg: 12f, imageAttrs, cardAlpha: 238);
                    }
                    DrawTiltedCardIcon(g, validIcons[0], cx: 64f, cy: 68f, size: validIcons.Count == 1 ? 82 : 70, angleDeg: 0f, imageAttrs, cardAlpha: 255);
                }
            }

            return canvas;
        }

        private static void DrawTiltedCardIcon(
            Graphics g,
            Bitmap icon,
            float cx,
            float cy,
            int size,
            float angleDeg,
            ImageAttributes attrs,
            int cardAlpha)
        {
            var state = g.Save();
            try
            {
                g.TranslateTransform(cx, cy);
                if (Math.Abs(angleDeg) > 0.01f)
                    g.RotateTransform(angleDeg);

                int half = size / 2;
                var shadowRect = new Rectangle(-half + 2, -half + 4, size, size);
                var cardRect = new Rectangle(-half, -half, size, size);

                using (GraphicsPath shadowPath = CreateRoundedRectPath(shadowRect, 14))
                using (var shadowBrush = new SolidBrush(Color.FromArgb(85, 0, 0, 0)))
                {
                    g.FillPath(shadowBrush, shadowPath);
                }

                using (GraphicsPath cardPath = CreateRoundedRectPath(cardRect, 14))
                using (var cardBrush = new SolidBrush(Color.FromArgb(cardAlpha, 38, 42, 52)))
                using (var borderPen = new Pen(Color.FromArgb(110, 255, 255, 255), 1.8f))
                {
                    g.FillPath(cardBrush, cardPath);
                    g.DrawPath(borderPen, cardPath);
                }

                int pad = Math.Max(6, size / 7);
                DrawSubIcon(g, icon, new Rectangle(-half + pad, -half + pad, size - 2 * pad, size - 2 * pad), attrs);
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>
        /// Renders a rounded squircle icon with an accent gradient and centered Emoji or 1-3 character Monogram.
        /// </summary>
        public static Bitmap CreateBadgeMonogramBitmap(string badgeText, Color? tintColor = null)
        {
            const int canvasSize = 128;
            var canvas = new Bitmap(canvasSize, canvasSize, PixelFormat.Format32bppArgb);
            string text = (badgeText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
                text = "B";

            // Limit to at most 3 display characters/grapheme elements
            if (text.Length > 4)
            {
                var si = new System.Globalization.StringInfo(text);
                if (si.LengthInTextElements > 3)
                    text = si.SubstringByTextElements(0, 3);
            }

            Color baseTint = tintColor ?? Color.FromArgb(255, 56, 152, 236);
            Color topFill = Color.FromArgb(255, BlendByte(baseTint.R, 255, 0.85), BlendByte(baseTint.G, 255, 0.85), BlendByte(baseTint.B, 255, 0.85));
            Color botFill = Color.FromArgb(255, BlendByte(baseTint.R, 12, 0.65), BlendByte(baseTint.G, 14, 0.65), BlendByte(baseTint.B, 22, 0.65));
            Color strokeCol = Color.FromArgb(145, 255, 255, 255);

            using (Graphics g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                var fullRect = new Rectangle(0, 0, 128, 128);
                var strokeRect = new Rectangle(1, 1, 126, 126);
                using (GraphicsPath fillPath = CreateRoundedRectPath(fullRect, 18))
                using (GraphicsPath strokePath = CreateRoundedRectPath(strokeRect, 17))
                using (var fillBrush = new LinearGradientBrush(fullRect, topFill, botFill, LinearGradientMode.ForwardDiagonal))
                using (var borderPen = new Pen(strokeCol, 2.0f))
                {
                    g.FillPath(fillBrush, fillPath);
                    g.DrawPath(borderPen, strokePath);
                }

                // Detect if text contains non-ASCII/emoji symbols
                bool isEmojiLike = false;
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] > 127)
                    {
                        isEmojiLike = true;
                        break;
                    }
                }

                int elemLen = new System.Globalization.StringInfo(text).LengthInTextElements;
                float fontSize = isEmojiLike
                    ? (elemLen <= 1 ? 54f : 40f)
                    : (elemLen <= 1 ? 62f : (elemLen == 2 ? 46f : 35f));

                string fontName = isEmojiLike ? "Segoe UI Emoji" : "Segoe UI Variable Display";
                using (var font = new Font(fontName, fontSize, isEmojiLike ? FontStyle.Regular : FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var shadowBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                using (var textBrush = new SolidBrush(Color.White))
                {
                    var shadowRect = new RectangleF(4, 7, 120, 120);
                    var textRect = new RectangleF(4, 4, 120, 120);
                    g.DrawString(text, font, shadowBrush, shadowRect, sf);
                    g.DrawString(text, font, textBrush, textRect, sf);
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

        private static void DrawEmptyPlaceholderGrid(Graphics g, bool inset = false)
        {
            // Minimal subtle 2x2 glyph at the mosaic slots when a bin has 0 items
            Rectangle[] slots = inset
                ? new[]
                {
                    new Rectangle(9, 9, 51, 51),
                    new Rectangle(68, 9, 51, 51),
                    new Rectangle(9, 68, 51, 51),
                    new Rectangle(68, 68, 51, 51)
                }
                : new[]
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

            // 1. Primary Windows 11 High-DPI path: IShellItemImageFactory (handles .exe, .lnk, UWP/Store shell:AppsFolder\, folders, files at 256x256)
            bool isShellAppsPath = expanded.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase);
            if (isShellAppsPath || File.Exists(expanded) || Directory.Exists(expanded))
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

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        private static void SetAccentPolicy(IntPtr hwnd, int accentState, uint gradientColor = 0)
        {
            if (hwnd == IntPtr.Zero)
                return;

            var accent = new AccentPolicy
            {
                AccentState = accentState,
                AccentFlags = accentState == ACCENT_ENABLE_ACRYLICBLURBEHIND ? 2 : 0,
                GradientColor = gradientColor,
                AnimationId = 0
            };

            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            catch
            {
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>
        /// Applies Windows 11 rounded corners + real variable-opacity Acrylic blur (via SetWindowCompositionAttribute),
        /// Mica (DWMSBT_MAINWINDOW), or transparent/solid DWM glass compositing.
        /// </summary>
        public static void ApplyFlyoutBackdropAndOpacity(
            IntPtr hwnd,
            int backdropStyle,
            int opacityPercent,
            bool isLightTheme)
        {
            if (hwnd == IntPtr.Zero)
                return;

            try
            {
                int dark = isLightTheme ? 0 : 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

                int corner = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

                int clampedPct = Math.Max(15, Math.Min(100, opacityPercent));

                if (backdropStyle == 0) // Acrylic
                {
                    // Clear DWM system backdrop so it doesn't paint a fixed-opacity gray layer behind our variable-alpha Acrylic
                    int backdropNone = DWMSBT_NONE;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropNone, sizeof(int));

                    // GradientColor format is 0xAABBGGRR
                    // Clamp alpha between 0x10 and 0xF8 so blur stays active even near 100%
                    uint alpha = clampedPct >= 100
                        ? 0xFFu
                        : (uint)Math.Max(0x10, Math.Min(0xF5, (int)Math.Round(255.0 * clampedPct / 100.0)));
                    uint bgr = isLightTheme ? 0x00F6F6F6u : 0x00202020u;
                    SetAccentPolicy(hwnd, ACCENT_ENABLE_ACRYLICBLURBEHIND, (alpha << 24) | bgr);
                }
                else if (backdropStyle == 1) // Mica
                {
                    SetAccentPolicy(hwnd, ACCENT_DISABLED, 0);
                    int backdropMica = DWMSBT_MAINWINDOW;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropMica, sizeof(int));
                }
                else // Solid (or clear per-pixel alpha glass via WPF PopupBg)
                {
                    SetAccentPolicy(hwnd, ACCENT_DISABLED, 0);
                    int backdropNone = DWMSBT_NONE;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropNone, sizeof(int));
                }
            }
            catch
            {
            }
        }

        public static void ApplyWindows11WindowStyling(
            IntPtr hwnd,
            bool useMica = false,
            bool smallRound = false,
            int? captionColorRef = null,
            bool useAcrylic = false,
            bool? forceDarkMode = null)
        {
            if (hwnd == IntPtr.Zero)
                return;

            try
            {
                int dark = (forceDarkMode ?? true) ? 1 : 0;
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
                else if (useAcrylic)
                {
                    int backdrop = DWMSBT_TRANSIENTWINDOW;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                }
                else if (useMica)
                {
                    int backdrop = DWMSBT_MAINWINDOW;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                }
                else
                {
                    int backdropNone = DWMSBT_NONE;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropNone, sizeof(int));
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
        /// Immediately hides and dismisses any active Windows Taskbar hover preview popup (TaskListThumbnailWnd)
        /// as well as Windows 11 XAML taskbar text tooltips (e.g. PopupHost / XamlExplorerHostIslandWindow / tooltips_class32)
        /// near the hovered Bin button so native button title tooltips never overlap Bins' hover UI.
        /// </summary>
        public static bool DismissTaskbarThumbnailWindows(System.Drawing.Point? anchorPx = null, bool cancelTrayHoverTooltip = false)
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

            IntPtr trayHwnd = FindWindowExW(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            if (trayHwnd == IntPtr.Zero)
                return dismissedAny;

            GetWindowThreadProcessId(trayHwnd, out uint trayPid);
            if (trayPid == 0)
                return dismissedAny;

            if (cancelTrayHoverTooltip && GetCursorPos(out POINT cursorPt))
            {
                IntPtr under = WindowFromPoint(cursorPt);
                if (under != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(under, out uint underPid);
                    if (underPid == trayPid)
                    {
                        PostMessageW(under, WM_MOUSELEAVE, IntPtr.Zero, IntPtr.Zero);
                    }
                }
            }

            GetWindowRect(trayHwnd, out RECT trayRect);

            EnumWindows((h, _) =>
            {
                if (TryDismissIfTaskbarTooltipWindow(h, trayHwnd, trayPid, trayRect, anchorPx))
                {
                    dismissedAny = true;
                }
                return true;
            }, IntPtr.Zero);

            return dismissedAny;
        }

        /// <summary>
        /// Immediately hides a single HWND if it belongs to explorer.exe's taskbar process and matches
        /// a native taskbar thumbnail/tooltip or Windows 11 XAML taskbar title tooltip near the hovered Bin.
        /// Used both by EnumWindows sweeps and event-driven EVENT_OBJECT_SHOW / EVENT_OBJECT_UNCLOAKED hooks.
        /// </summary>
        public static bool TryDismissIfTaskbarTooltipWindow(
            IntPtr h,
            IntPtr trayHwnd,
            uint trayPid,
            RECT trayRect,
            System.Drawing.Point? anchorPx)
        {
            if (h == IntPtr.Zero || h == trayHwnd || trayPid == 0 || !IsWindowVisible(h) || IsWindowCloaked(h))
                return false;

            GetWindowThreadProcessId(h, out uint pid);
            if (pid != trayPid)
                return false;

            string cls = GetWindowClassNameSafe(h);
            if (string.IsNullOrEmpty(cls) ||
                string.Equals(cls, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "Progman", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "MultitaskingViewFrame", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cls, "MSTaskSwWClass", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(cls, "TaskListThumbnailWnd", StringComparison.OrdinalIgnoreCase))
            {
                ShowWindow(h, SW_HIDE);
                PostMessageW(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return true;
            }

            if (string.Equals(cls, "tooltips_class32", StringComparison.OrdinalIgnoreCase))
            {
                ShowWindow(h, SW_HIDE);
                return true;
            }

            // Windows 11 XAML Taskbar button text tooltips (PopupHost / XamlExplorerHostIslandWindow / PopupWindowSiteBridge)
            // have small tooltip dimensions positioned right above/beside the hovered taskbar icon.
            bool isKnownIslandPopup =
                cls.IndexOf("Popup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cls.IndexOf("Island", StringComparison.OrdinalIgnoreCase) >= 0 ||
                cls.IndexOf("Bridge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(cls, "Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase);

            if ((isKnownIslandPopup || GetWindowTextLengthW(h) == 0) && GetWindowRect(h, out RECT r))
            {
                int w = r.Right - r.Left;
                int ht = r.Bottom - r.Top;
                if (w >= 16 && w <= 480 && ht >= 16 && ht <= 115)
                {
                    bool nearTarget;
                    if (anchorPx.HasValue)
                    {
                        int cx = (r.Left + r.Right) / 2;
                        int cy = (r.Top + r.Bottom) / 2;
                        nearTarget = Math.Abs(cx - anchorPx.Value.X) <= 220 && Math.Abs(cy - anchorPx.Value.Y) <= 180;
                    }
                    else
                    {
                        nearTarget = r.Bottom >= trayRect.Top - 150 && r.Top <= trayRect.Bottom + 150;
                    }

                    if (nearTarget)
                    {
                        ShowWindow(h, SW_HIDE);
                        return true;
                    }
                }
            }

            return false;
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

        public static void MinimizeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                return;

            ShowWindow(hwnd, SW_MINIMIZE);
        }

        public static void CloseWindowGracefully(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
                return;

            PostMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public const byte VK_VOLUME_MUTE = 0xAD;
        public const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        public const byte VK_MEDIA_PREV_TRACK = 0xB1;
        public const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeoutW(
            IntPtr hWnd,
            uint Msg,
            UIntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out UIntPtr lpdwResult);

        public static void SendMediaKey(byte vk)
        {
            try
            {
                keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                keybd_event(vk, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch
            {
            }
        }

        public static void BroadcastThemeChange()
        {
            try
            {
                SendMessageTimeoutW(
                    new IntPtr(0xFFFF), // HWND_BROADCAST
                    WM_SETTINGCHANGE,
                    UIntPtr.Zero,
                    "ImmersiveColorSet",
                    SMTO_ABORTIFHUNG,
                    500,
                    out _);
            }
            catch
            {
            }
        }

        #endregion
    }
}
