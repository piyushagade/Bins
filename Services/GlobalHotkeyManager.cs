using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;
using WinBins.Interop;
using WinBins.Models;

namespace WinBins.Services
{
    public class GlobalHotkeyManager : IDisposable
    {
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        private HwndSource _hwndSource;
        private readonly Dictionary<int, BinGroup> _registeredBins = new Dictionary<int, BinGroup>();
        private int _globalSearchHotkeyId = -1;
        private int _shelfHotkeyId = -1;
        private int _nextHotkeyId = 9100;
        private bool _disposed;

        public event Action<BinGroup> BinHotkeyPressed;
        public event Action GlobalSearchHotkeyPressed;
        public event Action ShelfHotkeyPressed;

        public GlobalHotkeyManager()
        {
            EnsureMessageWindow();
        }

        private void EnsureMessageWindow()
        {
            if (_hwndSource != null || _disposed)
                return;

            try
            {
                var parameters = new HwndSourceParameters("WinBins_GlobalHotkeySink")
                {
                    Width = 0,
                    Height = 0,
                    PositionX = 0,
                    PositionY = 0,
                    WindowStyle = 0,
                    ParentWindow = HWND_MESSAGE
                };

                _hwndSource = new HwndSource(parameters);
                _hwndSource.AddHook(WndProc);
            }
            catch
            {
                _hwndSource = null;
            }
        }

        public void SyncHotkeys(IEnumerable<BinGroup> bins, string globalSearchHotkey = null, string shelfHotkey = null)
        {
            if (_disposed)
                return;

            EnsureMessageWindow();
            if (_hwndSource == null || _hwndSource.Handle == IntPtr.Zero)
                return;

            UnregisterAll();

            if (!string.IsNullOrWhiteSpace(globalSearchHotkey) &&
                TryParseHotkey(globalSearchHotkey, out uint searchMods, out uint searchVk))
            {
                int searchId = _nextHotkeyId++;
                if (Win32Interop.RegisterHotKey(_hwndSource.Handle, searchId, searchMods | Win32Interop.MOD_NOREPEAT, searchVk))
                {
                    _globalSearchHotkeyId = searchId;
                }
            }

            if (!string.IsNullOrWhiteSpace(shelfHotkey) &&
                TryParseHotkey(shelfHotkey, out uint shelfMods, out uint shelfVk))
            {
                int shelfId = _nextHotkeyId++;
                if (Win32Interop.RegisterHotKey(_hwndSource.Handle, shelfId, shelfMods | Win32Interop.MOD_NOREPEAT, shelfVk))
                {
                    _shelfHotkeyId = shelfId;
                }
            }

            if (bins == null)
                return;

            foreach (BinGroup bin in bins)
            {
                if (bin == null || string.IsNullOrWhiteSpace(bin.Hotkey))
                    continue;

                if (TryParseHotkey(bin.Hotkey, out uint fsModifiers, out uint vk))
                {
                    int id = _nextHotkeyId++;
                    if (Win32Interop.RegisterHotKey(_hwndSource.Handle, id, fsModifiers | Win32Interop.MOD_NOREPEAT, vk))
                    {
                        _registeredBins[id] = bin;
                    }
                }
            }
        }

        private void UnregisterAll()
        {
            if (_hwndSource != null && _hwndSource.Handle != IntPtr.Zero)
            {
                if (_globalSearchHotkeyId != -1)
                {
                    try { Win32Interop.UnregisterHotKey(_hwndSource.Handle, _globalSearchHotkeyId); } catch { }
                }

                if (_shelfHotkeyId != -1)
                {
                    try { Win32Interop.UnregisterHotKey(_hwndSource.Handle, _shelfHotkeyId); } catch { }
                }

                foreach (int id in _registeredBins.Keys)
                {
                    try
                    {
                        Win32Interop.UnregisterHotKey(_hwndSource.Handle, id);
                    }
                    catch
                    {
                    }
                }
            }
            _globalSearchHotkeyId = -1;
            _shelfHotkeyId = -1;
            _registeredBins.Clear();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Interop.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_globalSearchHotkeyId != -1 && id == _globalSearchHotkeyId)
                {
                    handled = true;
                    GlobalSearchHotkeyPressed?.Invoke();
                }
                else if (_shelfHotkeyId != -1 && id == _shelfHotkeyId)
                {
                    handled = true;
                    ShelfHotkeyPressed?.Invoke();
                }
                else if (_registeredBins.TryGetValue(id, out BinGroup bin) && bin != null)
                {
                    handled = true;
                    BinHotkeyPressed?.Invoke(bin);
                }
            }
            return IntPtr.Zero;
        }

        public static string FormatHotkey(ModifierKeys modifiers, Key key)
        {
            if (key == Key.None ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin ||
                key == Key.System)
            {
                return null;
            }

            // Require at least one modifier unless it is a Function key (F1-F12)
            bool isFunctionKey = key >= Key.F1 && key <= Key.F24;
            if (modifiers == ModifierKeys.None && !isFunctionKey)
                return null;

            var parts = new List<string>(4);
            if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

            string keyName;
            if (key >= Key.D0 && key <= Key.D9)
            {
                keyName = ((int)(key - Key.D0)).ToString();
            }
            else if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                keyName = "Num" + ((int)(key - Key.NumPad0));
            }
            else
            {
                keyName = key.ToString();
            }

            parts.Add(keyName);
            return string.Join("+", parts);
        }

        public static bool TryParseHotkey(string hotkeyText, out uint fsModifiers, out uint vk)
        {
            fsModifiers = 0;
            vk = 0;

            if (string.IsNullOrWhiteSpace(hotkeyText))
                return false;

            string[] tokens = hotkeyText.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return false;

            Key parsedKey = Key.None;
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i].Trim();
                if (string.Equals(tok, "Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tok, "Control", StringComparison.OrdinalIgnoreCase))
                {
                    fsModifiers |= Win32Interop.MOD_CONTROL;
                }
                else if (string.Equals(tok, "Alt", StringComparison.OrdinalIgnoreCase))
                {
                    fsModifiers |= Win32Interop.MOD_ALT;
                }
                else if (string.Equals(tok, "Shift", StringComparison.OrdinalIgnoreCase))
                {
                    fsModifiers |= Win32Interop.MOD_SHIFT;
                }
                else if (string.Equals(tok, "Win", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(tok, "Windows", StringComparison.OrdinalIgnoreCase))
                {
                    fsModifiers |= Win32Interop.MOD_WIN;
                }
                else if (tok.Length == 1 && tok[0] >= '0' && tok[0] <= '9')
                {
                    parsedKey = Key.D0 + (tok[0] - '0');
                }
                else if (tok.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
                         tok.Length == 4 && tok[3] >= '0' && tok[3] <= '9')
                {
                    parsedKey = Key.NumPad0 + (tok[3] - '0');
                }
                else if (Enum.TryParse(tok, ignoreCase: true, out Key enumKey))
                {
                    parsedKey = enumKey;
                }
            }

            if (parsedKey == Key.None)
                return false;

            bool isFunctionKey = parsedKey >= Key.F1 && parsedKey <= Key.F24;
            if (fsModifiers == 0 && !isFunctionKey)
                return false;

            int virtualKey = KeyInterop.VirtualKeyFromKey(parsedKey);
            if (virtualKey <= 0)
                return false;

            vk = (uint)virtualKey;
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            UnregisterAll();

            if (_hwndSource != null)
            {
                try
                {
                    _hwndSource.RemoveHook(WndProc);
                    _hwndSource.Dispose();
                }
                catch
                {
                }
                _hwndSource = null;
            }
        }
    }
}
