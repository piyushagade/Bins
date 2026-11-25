using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WinBins.Interop;
using WinBins.Models;

namespace WinBins.Services
{
    public sealed class ShelfTriggerManager : IDisposable
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;

        private readonly Dispatcher _dispatcher;
        private Win32Interop.LowLevelMouseProc _mouseProc;
        private IntPtr _mouseHook = IntPtr.Zero;

        private bool _enabled;
        private bool _isShelfOpen;
        private ShelfMouseTrigger _mouseButton = ShelfMouseTrigger.None;
        private bool _requireCtrl;
        private bool _requireAlt;
        private bool _requireShift;
        private bool _swallowNextMiddleUp;
        private bool _swallowNextXUp;
        private bool _disposed;

        public event Action<int, int> MouseTriggered;
        public event Action ClickedOutsideShelf;
        public Func<int, int, bool> IsPointInsideOpenShelf;

        public ShelfTriggerManager(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public void SetShelfOpen(bool isOpen)
        {
            if (_disposed)
                return;

            _isShelfOpen = isOpen;
            if (_isShelfOpen || (_enabled && _mouseButton != ShelfMouseTrigger.None))
            {
                EnsureHook();
            }
            else
            {
                UninstallHook();
            }
        }

        public void Sync(AppConfig config)
        {
            if (_disposed)
                return;

            if (config == null || !config.ShelfEnabled || config.ShelfMouseButton == ShelfMouseTrigger.None)
            {
                _enabled = false;
                _mouseButton = ShelfMouseTrigger.None;
                if (!_isShelfOpen)
                {
                    UninstallHook();
                }
                return;
            }

            _enabled = true;
            _mouseButton = config.ShelfMouseButton;
            _requireCtrl = config.ShelfMouseRequireCtrl;
            _requireAlt = config.ShelfMouseRequireAlt;
            _requireShift = config.ShelfMouseRequireShift;

            EnsureHook();
        }

        private void EnsureHook()
        {
            if (_mouseHook != IntPtr.Zero || _disposed)
                return;

            try
            {
                _mouseProc = MouseHookCallback;
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    IntPtr hMod = curModule != null
                        ? Win32Interop.GetModuleHandleW(curModule.ModuleName)
                        : IntPtr.Zero;
                    _mouseHook = Win32Interop.SetWindowsHookExW(Win32Interop.WH_MOUSE_LL, _mouseProc, hMod, 0);
                }
            }
            catch
            {
                _mouseHook = IntPtr.Zero;
            }
        }

        private void UninstallHook()
        {
            _swallowNextMiddleUp = false;
            _swallowNextXUp = false;

            if (_mouseHook != IntPtr.Zero)
            {
                try
                {
                    Win32Interop.UnhookWindowsHookEx(_mouseHook);
                }
                catch
                {
                }
                _mouseHook = IntPtr.Zero;
            }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();

                if (msg == Win32Interop.WM_MBUTTONUP && _swallowNextMiddleUp)
                {
                    _swallowNextMiddleUp = false;
                    return new IntPtr(1);
                }

                if (msg == Win32Interop.WM_XBUTTONUP && _swallowNextXUp)
                {
                    _swallowNextXUp = false;
                    return new IntPtr(1);
                }

                if (_isShelfOpen && (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN))
                {
                    var clickInfo = Marshal.PtrToStructure<Win32Interop.MSLLHOOKSTRUCT>(lParam);
                    var checker = IsPointInsideOpenShelf;
                    if (checker != null && !checker(clickInfo.pt.X, clickInfo.pt.Y))
                    {
                        _dispatcher.BeginInvoke(
                            DispatcherPriority.Send,
                            new Action(() => ClickedOutsideShelf?.Invoke()));
                    }
                }

                if (_enabled)
                {
                    bool isMatch = false;
                    bool isMiddle = false;

                    if (msg == Win32Interop.WM_MBUTTONDOWN && _mouseButton == ShelfMouseTrigger.MiddleButton)
                    {
                        isMatch = ModifiersMatch();
                        isMiddle = true;
                    }
                    else if (msg == Win32Interop.WM_XBUTTONDOWN &&
                             (_mouseButton == ShelfMouseTrigger.XButton1 || _mouseButton == ShelfMouseTrigger.XButton2))
                    {
                        var info = Marshal.PtrToStructure<Win32Interop.MSLLHOOKSTRUCT>(lParam);
                        uint xButton = (info.mouseData >> 16) & 0xFFFF;
                        if ((_mouseButton == ShelfMouseTrigger.XButton1 && xButton == 1) ||
                            (_mouseButton == ShelfMouseTrigger.XButton2 && xButton == 2))
                        {
                            isMatch = ModifiersMatch();
                        }
                    }

                    if (isMatch)
                    {
                        var info = Marshal.PtrToStructure<Win32Interop.MSLLHOOKSTRUCT>(lParam);
                        if (isMiddle)
                            _swallowNextMiddleUp = true;
                        else
                            _swallowNextXUp = true;

                        int x = info.pt.X;
                        int y = info.pt.Y;
                        _dispatcher.BeginInvoke(
                            DispatcherPriority.Send,
                            new Action(() => MouseTriggered?.Invoke(x, y)));

                        return new IntPtr(1);
                    }
                }
            }

            return Win32Interop.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private bool ModifiersMatch()
        {
            if (_requireCtrl && (Win32Interop.GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0)
                return false;
            if (_requireAlt && (Win32Interop.GetAsyncKeyState(VK_MENU) & 0x8000) == 0)
                return false;
            if (_requireShift && (Win32Interop.GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0)
                return false;
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            UninstallHook();
        }
    }
}
