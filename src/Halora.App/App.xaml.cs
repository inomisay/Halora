using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Halora.Core;
using Forms = System.Windows.Forms;

namespace Halora.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private Settings _settings = new();
    private DragWatcher? _watcher;
    private DropCatcherWindow? _catcher;
    private RadialMenuWindow? _menu;
    private Forms.NotifyIcon? _tray;
    private ContextMenu? _trayMenu;
    private MenuItem? _ffmpegItem;
    private ConversionService _service = new(null);
    private readonly List<ProgressWindow> _cards = [];
    private HelpWindow? _help;
    private readonly DispatcherTimer _dropGrace = new() { Interval = TimeSpan.FromMilliseconds(600) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Halora-SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write($"Unhandled: {args.Exception}");
            args.Handled = true;
        };

        _settings = Settings.Load();
        RefreshFfmpeg();

        var catcher = _catcher = new DropCatcherWindow();
        catcher.FilesDropped += OnFilesDropped;

        var radial = _menu = new RadialMenuWindow();
        radial.ActionChosen += OnActionChosen;

        _dropGrace.Tick += (_, _) =>
        {
            _dropGrace.Stop();
            if (catcher.IsVisible && !catcher.ReceivedDrop)
            {
                catcher.HideNow();
            }
        };

        _watcher = new DragWatcher(Dispatcher);
        _watcher.ShiftDragStarted += pt =>
        {
            if (_settings.Enabled && !radial.IsVisible)
            {
                _dropGrace.Stop();
                catcher.ShowAt(pt);
            }
        };
        _watcher.ShiftDragMoved += pt => catcher.MoveTo(pt);
        _watcher.ShiftReleased += () => catcher.HideNow();
        _watcher.ShiftDragEnded += () => _dropGrace.Start(); // the OLE drop arrives a moment after the button-up
        _watcher.MouseDown += pt =>
        {
            if (radial.IsVisible && !Native.Contains(radial, pt))
            {
                radial.Dismiss();
            }
        };

        if (!_watcher.Start())
        {
            Log.Write("Mouse hook could not be installed.");
            System.Windows.MessageBox.Show(
                "Halora couldn't start watching the mouse, so Shift + drag won't work. Try restarting your PC.",
                "Halora", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        CreateTray();

        if (!_settings.SeenWelcome)
        {
            _settings.SeenWelcome = true;
            _settings.Save();
            ShowHelp();
        }
    }

    private void ShowHelp()
    {
        if (_help is not null)
        {
            _help.Activate();
            return;
        }

        _help = new HelpWindow(_service.HasFfmpeg, ShowFfmpegHelp);
        _help.Closed += (_, _) => _help = null;
        _help.Show();
        _help.Activate();
    }

    private void RefreshFfmpeg()
    {
        _service = new ConversionService(Ffmpeg.Locate(), new WindowsPdfRenderer());
    }

    private void OnFilesDropped(string[] files, Native.POINT point)
    {
        // Leave the OLE drop callback first so Explorer isn't kept waiting while the menu opens.
        Dispatcher.BeginInvoke(() =>
        {
            var actions = ActionCatalog.For(files);
            if (actions.Count == 0)
            {
                _tray?.ShowBalloonTip(3000, "Halora", "These files can't be converted yet.", Forms.ToolTipIcon.None);
                return;
            }

            _menu!.ShowAt(point, files, actions);
        });
    }

    private async void OnActionChosen(ConversionAction action, IReadOnlyList<string> files)
    {
        if (action.Engine != Engine.RenderPdf && !_service.HasFfmpeg)
        {
            RefreshFfmpeg(); // maybe it was installed since startup
            if (!_service.HasFfmpeg)
            {
                ShowFfmpegHelp();
                return;
            }
        }

        var card = new ProgressWindow();
        var slot = Enumerable.Range(0, 10).FirstOrDefault(i => _cards.All(c => (int)c.Tag != i));
        card.Tag = slot;
        _cards.Add(card);
        card.Closed += (_, _) => _cards.Remove(card);

        try
        {
            await card.RunAsync(_service, action, files, slot);
        }
        catch (Exception ex)
        {
            Log.Write($"Job failed: {ex}");
        }
    }

    private void CreateTray()
    {
        // Switches stay open on click so you see them flip.
        var enabled = new MenuItem { Header = "On (Shift + drag)", IsCheckable = true, IsChecked = _settings.Enabled, StaysOpenOnClick = true };
        enabled.Click += (_, _) =>
        {
            _settings.Enabled = enabled.IsChecked;
            _settings.Save();
        };

        var autostart = new MenuItem { Header = "Start with Windows", IsCheckable = true, StaysOpenOnClick = true };
        autostart.Click += (_, _) => Autostart.Set(autostart.IsChecked);

        var howTo = new MenuItem { Header = "How to use" };
        howTo.Click += (_, _) => ShowHelp();

        _ffmpegItem = new MenuItem { Header = "Install FFmpeg…" };
        _ffmpegItem.Click += (_, _) => ShowFfmpegHelp();

        var logs = new MenuItem { Header = "Open log folder" };
        logs.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "explorer.exe",
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Halora")) { UseShellExecute = true });

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Quit();

        var separator = (Style)FindResource("TrayMenuSeparator");
        var menu = _trayMenu = new ContextMenu { Style = (Style)FindResource("TrayMenu") };
        foreach (var item in new Control[]
        {
            enabled, autostart, new Separator { Style = separator },
            howTo, _ffmpegItem, logs, new Separator { Style = separator }, quit,
        })
        {
            menu.Items.Add(item);
        }

        menu.Opened += (_, _) =>
        {
            autostart.IsChecked = Autostart.IsOn;
            _ffmpegItem.Visibility = _service.HasFfmpeg ? Visibility.Collapsed : Visibility.Visible;

            // A menu opened from the tray has no owner window. Bringing it to the front is what
            // lets a click anywhere else close it, like a normal menu.
            if (PresentationSource.FromVisual(menu) is HwndSource source)
            {
                Native.SetForegroundWindow(source.Handle);
            }
        };

        var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/halora.ico"))?.Stream;
        _tray = new Forms.NotifyIcon
        {
            // Ask for the tray size so Windows picks the sharp frame instead of scaling a 32 px one.
            Icon = iconStream is null ? System.Drawing.SystemIcons.Application : new System.Drawing.Icon(iconStream, Forms.SystemInformation.SmallIconSize),
            Text = "Halora · Shift + drag to convert",
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowHelp();
        _tray.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                menu.IsOpen = true;
            }
        };
    }

    private void ShowFfmpegHelp()
    {
        const string command = "winget install Gyan.FFmpeg";
        try
        {
            System.Windows.Clipboard.SetText(command);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Clipboard busy; the message still shows the command.
        }

        System.Windows.MessageBox.Show(
            "Halora uses FFmpeg, a free open-source tool, for images, audio and video.\n\n" +
            "Open Terminal and run:\n\n    " + command + "\n\n" +
            "(It's already copied to your clipboard.) Then try again. You don't need to restart Halora.",
            "Halora needs FFmpeg", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Quit()
    {
        _watcher?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _tray?.Dispose();
        if (_singleInstance is not null)
        {
            try
            {
                _singleInstance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned (second instance exiting).
            }

            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }
}
