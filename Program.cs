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
            ParseArgs(args, out string targetBinId, out bool startInBackground, out bool requestQuit);

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

                    // Forward command to running instance via Named Pipe
                    string payload = !string.IsNullOrWhiteSpace(targetBinId)
                        ? $"OPEN_BIN|{targetBinId}|{launchCursor.X}|{launchCursor.Y}"
                        : "SHOW_MANAGER";
                    SendIpcMessage(payload);
                    return;
                }

                if (requestQuit)
                {
                    return;
                }

                Win32Interop.SetCurrentProcessExplicitAppUserModelID("WinBins.Manager.App");

                var app = new BinsApplication(targetBinId, startInBackground, new System.Drawing.Point(launchCursor.X, launchCursor.Y));
                app.Run();
            }
        }

        private static void ParseArgs(string[] args, out string targetBinId, out bool background, out bool requestQuit)
        {
            targetBinId = null;
            background = false;
            requestQuit = false;
            if (args == null)
                return;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? string.Empty;
                if (string.Equals(a, "--bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "--open-bin", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        targetBinId = args[++i];
                    }
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

        private readonly string _initialBinId;
        private readonly bool _startInBackground;
        private readonly System.Drawing.Point _launchCursor;

        private ConfigService _configService;
        private IconService _iconService;
        private TaskbarBinHostManager _taskbarManager;
        private RunningAppsTaskbarManager _runningAppsManager;
        private BinPopupWindow _popupWindow;
        private MainWindow _mainWindow;
        private System.Windows.Forms.NotifyIcon _trayIcon;
        private Icon _trayDrawingIcon;
        private CancellationTokenSource _ipcCts;

        public BinsApplication(string initialBinId, bool startInBackground, System.Drawing.Point launchCursor)
        {
            _initialBinId = initialBinId;
            _startInBackground = startInBackground;
            _launchCursor = launchCursor;
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

            _iconService = new IconService(_configService);
            _taskbarManager = new TaskbarBinHostManager(_configService, _iconService, Dispatcher);
            _runningAppsManager = new RunningAppsTaskbarManager(_configService, Dispatcher);

            _popupWindow = new BinPopupWindow(
                _configService,
                _iconService,
                _runningAppsManager,
                onCustomizeRequested: bin => ShowManager(bin),
                onBinModified: bin =>
                {
                    _taskbarManager.RefreshBin(bin);
                    _runningAppsManager.SyncRunningWindows();
                    _mainWindow?.RefreshFullUi();
                    RebuildTrayMenu();
                });

            _mainWindow = new MainWindow(_configService, _iconService, _taskbarManager, _popupWindow, _runningAppsManager);

            _taskbarManager.BinTaskbarIconClicked += (bin, cursorPt) =>
            {
                _popupWindow.ToggleAtScreenPoint(bin, cursorPt);
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
            InitializeSystemTray();
            StartIpcServer();

            if (!string.IsNullOrWhiteSpace(_initialBinId))
            {
                BinGroup target = FindBin(_initialBinId);
                if (target != null)
                {
                    _popupWindow.ShowForBin(target, _launchCursor);
                }
                else
                {
                    ShowManager(null);
                }
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

        private void HandleIpcCommand(string command)
        {
            string[] parts = command.Split('|');
            if (string.Equals(parts[0], "EXIT_APP", StringComparison.OrdinalIgnoreCase))
            {
                ExitCleanly();
                return;
            }

            if (parts.Length >= 2 && string.Equals(parts[0], "OPEN_BIN", StringComparison.OrdinalIgnoreCase))
            {
                string binId = parts[1];
                Win32Interop.GetCursorPos(out Win32Interop.POINT fallbackPt);
                int x = parts.Length > 2 && int.TryParse(parts[2], out int px) ? px : fallbackPt.X;
                int y = parts.Length > 3 && int.TryParse(parts[3], out int py) ? py : fallbackPt.Y;

                BinGroup bin = FindBin(binId);
                if (bin != null)
                {
                    _popupWindow.ToggleAtScreenPoint(bin, new System.Drawing.Point(x, y));
                }
                else
                {
                    ShowManager(null);
                }
            }
            else
            {
                ShowManager(null);
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

            _runningAppsManager?.Dispose();
            _taskbarManager?.Dispose();
            _iconService?.Dispose();

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
