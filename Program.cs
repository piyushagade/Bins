using System;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WinBins.Interop;
using WinBins.Models;
using WinBins.Services;
using WinBins.Views;

namespace WinBins
{
    public static class Program
    {
        private const string MutexName = @"Local\WinBins_SingleInstance_Mutex_v1";
        private const string PipeName = @"WinBins_IPC_Pipe_v1";

        [STAThread]
        public static void Main(string[] args)
        {
            // Enable Per-Monitor V2 DPI Awareness (-4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)
            try
            {
                Win32Interop.SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch { }

            Win32Interop.ClearAppStartingCursor();
            Win32Interop.GetCursorPos(out Win32Interop.POINT launchCursor);
            string commandPayload = ParseArgsToPayload(args, launchCursor, out bool startInBackground, out bool requestQuit);

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    if (requestQuit)
                    {
                        SendIpcMessage("EXIT_APP");
                        return;
                    }

                    // Grant foreground activation rights to the running Bins tray instance before signaling IPC
                    Win32Interop.AllowSetForegroundWindow(-1);
                    SendIpcMessage(commandPayload);
                    return;
                }

                if (requestQuit)
                {
                    return;
                }

                Win32Interop.SetCurrentProcessExplicitAppUserModelID("WinBins.Manager.App");

                var app = new BinsApplication(commandPayload, startInBackground);
                app.Run();
            }
        }

        private static string ParseArgsToPayload(
            string[] args,
            Win32Interop.POINT launchCursor,
            out bool background,
            out bool requestQuit)
        {
            background = false;
            requestQuit = false;
            if (args == null || args.Length == 0)
                return "SHOW_MANAGER";

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? string.Empty;
                if (string.Equals(a, "--bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "--open-bin", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                        return $"OPEN_BIN|{args[++i]}|{launchCursor.X}|{launchCursor.Y}";
                }
                else if (string.Equals(a, "--launch-item", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 2 < args.Length)
                    {
                        string binId = args[++i];
                        string itemId = args[++i];
                        return $"LAUNCH_ITEM|{binId}|{itemId}";
                    }
                }
                else if (string.Equals(a, "--open-all", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                        return $"OPEN_ALL|{args[++i]}";
                }
                else if (string.Equals(a, "--add-item", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                        return $"ADD_ITEM|{args[++i]}";
                }
                else if (string.Equals(a, "--flyout-options", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                        return $"FLYOUT_OPTIONS|{args[++i]}";
                }
                else if (string.Equals(a, "--customize", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                        return $"CUSTOMIZE_BIN|{args[++i]}";
                }
                else if (string.Equals(a, "--new-bin", StringComparison.OrdinalIgnoreCase))
                {
                    return "NEW_BIN";
                }
                else if (string.Equals(a, "--search", StringComparison.OrdinalIgnoreCase))
                {
                    return "OPEN_SEARCH";
                }
                else if (string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase))
                {
                    return "SHOW_SETTINGS";
                }
                else if (string.Equals(a, "--background", StringComparison.OrdinalIgnoreCase))
                {
                    background = true;
                }
                else if (string.Equals(a, "--quit", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--exit", StringComparison.OrdinalIgnoreCase))
                {
                    requestQuit = true;
                }
            }

            return background ? string.Empty : "SHOW_MANAGER";
        }

        private static void SendIpcMessage(string message)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(400);
                    using (var writer = new StreamWriter(client) { AutoFlush = true })
                    {
                        writer.WriteLine(message);
                    }
                }
            }
            catch
            {
            }
        }
    }

    public class BinsApplication : Application
    {
        private const string PipeName = @"WinBins_IPC_Pipe_v1";

        private readonly string _initialPayload;
        private readonly bool _startInBackground;

        private ConfigService _configService;
        private IconService _iconService;
        private TaskbarBinHostManager _taskbarManager;
        private RunningAppsTaskbarManager _runningAppsManager;
        private TaskbarIndicatorOverlayManager _indicatorOverlayManager;
        private GlobalHotkeyManager _hotkeyManager;
        private BinPopupWindow _popupWindow;
        private BinRunningPeekWindow _peekWindow;
        private OmniSearchWindow _omniSearchWindow;
        private MainWindow _mainWindow;
        private System.Windows.Forms.NotifyIcon _trayIcon;
        private Icon _trayDrawingIcon;
        private CancellationTokenSource _ipcCts;

        public BinsApplication(string initialPayload, bool startInBackground)
        {
            _initialPayload = initialPayload;
            _startInBackground = startInBackground;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            DispatcherUnhandledException += (s, e) =>
            {
                try
                {
                    string errPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WinBins",
                        "error.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(errPath));
                    File.AppendAllText(errPath, $"[{DateTime.Now:O}] {e.Exception}\r\n");
                }
                catch { }
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            FluentTheme.ApplyGlobalResources(Resources);

            _configService = new ConfigService();
            _configService.Load();
            if (_configService.Current != null)
            {
                FluentTheme.ApplyTheme(_configService.Current.ThemeMode, _configService.Current.UseAcrylicFlyout);
            }

            _iconService = new IconService(_configService);
            _taskbarManager = new TaskbarBinHostManager(_configService, _iconService, Dispatcher);
            _runningAppsManager = new RunningAppsTaskbarManager(_configService, Dispatcher);
            _indicatorOverlayManager = new TaskbarIndicatorOverlayManager(_configService, _taskbarManager, _runningAppsManager, Dispatcher);
            _hotkeyManager = new GlobalHotkeyManager();

            _peekWindow = new BinRunningPeekWindow(
                _configService,
                _iconService,
                _runningAppsManager,
                _taskbarManager);

            _popupWindow = new BinPopupWindow(
                _configService,
                _iconService,
                _runningAppsManager,
                _taskbarManager,
                onCustomizeRequested: bin => ShowManager(bin),
                onBinModified: bin =>
                {
                    _taskbarManager.RefreshBin(bin);
                    _runningAppsManager.SyncRunningWindows();
                    _hotkeyManager.SyncHotkeys(_configService.Current?.Bins, _configService.Current?.GlobalSearchHotkey);
                    _mainWindow?.RefreshFullUi();
                    RebuildTrayMenu();
                });

            _omniSearchWindow = new OmniSearchWindow(
                _configService,
                _iconService,
                _runningAppsManager,
                onOpenBinEditor: bin => ShowManager(bin));

            _mainWindow = new MainWindow(
                _configService,
                _iconService,
                _taskbarManager,
                _popupWindow,
                _runningAppsManager,
                _hotkeyManager,
                _omniSearchWindow);

            _taskbarManager.BinTaskbarIconClicked += (bin, cursorPt) =>
            {
                _peekWindow?.HidePeek();
                _popupWindow.ToggleAtScreenPoint(bin, cursorPt);
            };

            _taskbarManager.BinTaskbarHoverTriggered += (bin, anchorPt, isDragHover) =>
            {
                _peekWindow?.HidePeek();
                // When triggered by dragging a file over the taskbar icon, keep popup open until drop/dismiss;
                // when triggered by normal cursor hover, enable auto-dismiss when cursor leaves safe zone.
                _popupWindow.ShowForBin(bin, anchorPt, openedByHover: true);
            };

            _taskbarManager.BinTaskbarPeekTriggered += (bin, anchorPt) =>
            {
                if (_popupWindow.IsVisible || _omniSearchWindow.IsVisible)
                    return;

                _peekWindow?.TryShowForBin(bin, anchorPt);
            };

            _hotkeyManager.BinHotkeyPressed += bin =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _peekWindow?.HidePeek();
                    _omniSearchWindow?.HideSearch();
                    if (!_taskbarManager.TryGetBinTaskbarAnchorPoint(bin.Id, out System.Drawing.Point anchorPt))
                    {
                        Win32Interop.GetCursorPos(out Win32Interop.POINT cursorPt);
                        anchorPt = new System.Drawing.Point(cursorPt.X, cursorPt.Y);
                    }
                    _popupWindow.ToggleAtScreenPoint(bin, anchorPt);
                }));
            };

            _hotkeyManager.GlobalSearchHotkeyPressed += () =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _peekWindow?.HidePeek();
                    _popupWindow?.HidePopup();
                    _omniSearchWindow?.ToggleSearch();
                }));
            };

            _taskbarManager.BinClosedFromTaskbar += () =>
            {
                _runningAppsManager?.SyncRunningWindows();
                _mainWindow?.RefreshFullUi();
                RebuildTrayMenu();
            };

            _taskbarManager.TaskbarPinStateChanged += () =>
            {
                _runningAppsManager?.SyncRunningWindows();
                _mainWindow?.RefreshFullUi();
            };

            _taskbarManager.SyncAllBins();
            _runningAppsManager.SyncRunningWindows();
            _indicatorOverlayManager.SyncIndicators();
            _hotkeyManager.SyncHotkeys(_configService.Current?.Bins, _configService.Current?.GlobalSearchHotkey);
            InitializeSystemTray();
            StartIpcServer();

            if (!string.IsNullOrWhiteSpace(_initialPayload))
            {
                HandleIpcCommand(_initialPayload, isInitialStartup: true);
            }
            else if (!_startInBackground)
            {
                ShowManager(null);
            }
        }

        private void ShowManager(BinGroup focusBin)
        {
            if (_mainWindow == null)
                return;

            _mainWindow.SelectAndFocusBin(focusBin);
            RebuildTrayMenu();
        }

        private void InitializeSystemTray()
        {
            IntPtr hIcon = Win32Interop.CreateGroupMosaicIconHandle(null);
            if (hIcon != IntPtr.Zero)
            {
                _trayDrawingIcon = (Icon)Icon.FromHandle(hIcon).Clone();
                Win32Interop.DestroyIcon(hIcon);
            }

            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "Bins for Windows 11",
                Icon = _trayDrawingIcon ?? SystemIcons.Application,
                Visible = true
            };

            _trayIcon.DoubleClick += (_, __) => Dispatcher.BeginInvoke(new Action(() => ShowManager(null)));
            RebuildTrayMenu();
        }

        private void RebuildTrayMenu()
        {
            if (_trayIcon == null)
                return;

            var menu = new System.Windows.Forms.ContextMenuStrip();

            var bins = _configService.Current?.Bins;
            if (bins != null && bins.Count > 0)
            {
                foreach (BinGroup bin in bins)
                {
                    BinGroup captured = bin;
                    var binItem = new System.Windows.Forms.ToolStripMenuItem($"Open \"{captured.Name}\"");
                    binItem.Click += (_, __) => Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Win32Interop.GetCursorPos(out Win32Interop.POINT pt);
                        _popupWindow.ShowForBin(captured, new System.Drawing.Point(pt.X, pt.Y));
                    }));
                    menu.Items.Add(binItem);
                }
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            }

            var searchTrayItem = new System.Windows.Forms.ToolStripMenuItem("Search All Bins...");
            searchTrayItem.Click += (_, __) => Dispatcher.BeginInvoke(new Action(() =>
            {
                _popupWindow?.HidePopup();
                _omniSearchWindow?.ShowSearch();
            }));
            menu.Items.Add(searchTrayItem);

            var managerItem = new System.Windows.Forms.ToolStripMenuItem("Bins Manager...")
            {
                Font = new Font(menu.Font, System.Drawing.FontStyle.Bold)
            };
            managerItem.Click += (_, __) => Dispatcher.BeginInvoke(new Action(() => ShowManager(null)));
            menu.Items.Add(managerItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit Bins");
            exitItem.Click += (_, __) => Dispatcher.BeginInvoke(new Action(ExitCleanly));
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
        }

        private void StartIpcServer()
        {
            _ipcCts = new CancellationTokenSource();
            CancellationToken token = _ipcCts.Token;

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                        {
                            await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                            using (var reader = new StreamReader(server))
                            {
                                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    _ = Dispatcher.BeginInvoke(new Action(() => HandleIpcCommand(line)));
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        await Task.Delay(150).ConfigureAwait(false);
                    }
                }
            }, token);
        }

        private void HandleIpcCommand(string command, bool isInitialStartup = false)
        {
            string[] parts = command.Split('|');
            string verb = parts[0] ?? string.Empty;

            if (string.Equals(verb, "EXIT_APP", StringComparison.OrdinalIgnoreCase))
            {
                ExitCleanly();
                return;
            }

            if (parts.Length >= 2 && string.Equals(verb, "OPEN_BIN", StringComparison.OrdinalIgnoreCase))
            {
                string binId = parts[1];
                Win32Interop.GetCursorPos(out Win32Interop.POINT fallbackPt);
                int x = parts.Length > 2 && int.TryParse(parts[2], out int px) ? px : fallbackPt.X;
                int y = parts.Length > 3 && int.TryParse(parts[3], out int py) ? py : fallbackPt.Y;

                BinGroup bin = FindBin(binId);
                if (bin != null)
                {
                    _peekWindow?.HidePeek();
                    if (isInitialStartup)
                        _popupWindow.ShowForBin(bin, new System.Drawing.Point(x, y));
                    else
                        _popupWindow.ToggleAtScreenPoint(bin, new System.Drawing.Point(x, y));
                }
                else
                {
                    ShowManager(null);
                }
                return;
            }

            if (parts.Length >= 3 && string.Equals(verb, "LAUNCH_ITEM", StringComparison.OrdinalIgnoreCase))
            {
                BinGroup bin = FindBin(parts[1]);
                BinItem item = bin?.Items?.FirstOrDefault(it => string.Equals(it.Id, parts[2], StringComparison.OrdinalIgnoreCase));
                if (item != null)
                {
                    _popupWindow?.HidePopup();
                    LaunchOrActivateItem(item);
                }
                return;
            }

            if (parts.Length >= 2 && string.Equals(verb, "OPEN_ALL", StringComparison.OrdinalIgnoreCase))
            {
                BinGroup bin = FindBin(parts[1]);
                if (bin?.Items != null)
                {
                    _popupWindow?.HidePopup();
                    _ = LauncherService.LaunchBinWorkspaceAsync(bin, _runningAppsManager);
                }
                return;
            }

            if (string.Equals(verb, "OPEN_SEARCH", StringComparison.OrdinalIgnoreCase))
            {
                _popupWindow?.HidePopup();
                _omniSearchWindow?.ShowSearch();
                return;
            }

            if (parts.Length >= 2 && string.Equals(verb, "ADD_ITEM", StringComparison.OrdinalIgnoreCase))
            {
                _popupWindow?.HidePopup();
                BinGroup bin = FindBin(parts[1]);
                _mainWindow?.OpenBinAndPromptAddItem(bin);
                RebuildTrayMenu();
                return;
            }

            if (parts.Length >= 2 && string.Equals(verb, "FLYOUT_OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                _popupWindow?.HidePopup();
                BinGroup bin = FindBin(parts[1]);
                _mainWindow?.OpenBinWithFlyoutOptions(bin);
                RebuildTrayMenu();
                return;
            }

            if (parts.Length >= 2 && string.Equals(verb, "CUSTOMIZE_BIN", StringComparison.OrdinalIgnoreCase))
            {
                _popupWindow?.HidePopup();
                BinGroup bin = FindBin(parts[1]);
                ShowManager(bin);
                return;
            }

            if (string.Equals(verb, "NEW_BIN", StringComparison.OrdinalIgnoreCase))
            {
                _popupWindow?.HidePopup();
                _mainWindow?.CreateAndFocusNewBin();
                RebuildTrayMenu();
                return;
            }

            if (string.Equals(verb, "SHOW_SETTINGS", StringComparison.OrdinalIgnoreCase))
            {
                _popupWindow?.HidePopup();
                _mainWindow?.OpenSettingsPane();
                RebuildTrayMenu();
                return;
            }

            ShowManager(null);
        }

        private void LaunchOrActivateItem(BinItem item)
        {
            if (item == null)
                return;

            var liveWindows = _runningAppsManager?.GetRunningWindowsForItem(item);
            if (liveWindows != null && liveWindows.Count > 0)
            {
                Win32Interop.ActivateAndRestoreWindow(liveWindows[0].Hwnd);
            }
            else
            {
                LauncherService.LaunchItem(item);
            }
        }

        private BinGroup FindBin(string id)
        {
            return _configService.Current?.Bins?.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private void ExitCleanly()
        {
            try { _ipcCts?.Cancel(); } catch { }

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            _trayDrawingIcon?.Dispose();

            _hotkeyManager?.Dispose();
            _indicatorOverlayManager?.Dispose();
            _runningAppsManager?.Dispose();
            _taskbarManager?.Dispose();
            _iconService?.Dispose();

            if (_peekWindow != null)
            {
                _peekWindow.Close();
            }

            if (_omniSearchWindow != null)
            {
                _omniSearchWindow.Close();
            }

            if (_popupWindow != null)
            {
                _popupWindow.Close();
            }

            if (_mainWindow != null)
            {
                _mainWindow.AllowRealClose = true;
                _mainWindow.Close();
            }

            Shutdown();
        }
    }
}
