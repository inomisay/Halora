using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Animation;

namespace Halora.App;

/// <summary>
/// An invisible, always-on-top drop target that follows the cursor during a Shift + drag.
/// When the user lets go, the files land here instead of wherever they were heading.
/// </summary>
public partial class DropCatcherWindow : Window
{
    private bool _pulsing;

    public DropCatcherWindow()
    {
        InitializeComponent();
        Native.MakeToolWindow(this, noActivate: true);

        DragEnter += OnDragOver;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    /// <summary>Files dropped on the catcher, and where (physical screen pixels).</summary>
    internal event Action<string[], Native.POINT>? FilesDropped;

    public bool ReceivedDrop { get; private set; }

    internal void ShowAt(Native.POINT point)
    {
        ReceivedDrop = false;
        HintText.Text = "Let go to open Halora";
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }

        Native.CenterOn(this, point);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        StartPulse();
    }

    internal void MoveTo(Native.POINT point)
    {
        if (IsVisible)
        {
            Native.CenterOn(this, point);
        }
    }

    internal void HideNow()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Hide();
    }

    private void StartPulse()
    {
        if (_pulsing)
        {
            return;
        }

        _pulsing = true;
        var pulse = new DoubleAnimation(1, 1.08, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        RingScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, pulse);
        RingScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, pulse);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        // Always Copy. Explorer treats Shift + drag as "move", and a Move result would make
        // Explorer delete the original file after the drop.
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Effects == DragDropEffects.None)
        {
            HintText.Text = "Only files can be converted";
        }

        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        ReceivedDrop = true;

        var files = (e.Data.GetData(DataFormats.FileDrop) as string[] ?? [])
            .Where(File.Exists) // folders are ignored in this version
            .ToArray();

        Native.GetCursorPos(out var point);
        HideNow();

        if (files.Length > 0)
        {
            FilesDropped?.Invoke(files, point);
        }
    }
}
