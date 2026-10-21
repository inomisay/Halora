using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Halora.Core;

namespace Halora.App;

/// <summary>The circle of actions shown where the files were dropped.</summary>
public partial class RadialMenuWindow : Window
{
    private const double CenterXY = 180;
    private string _summary = "";
    private string _summaryHint = "";
    private bool _closing;

    public RadialMenuWindow()
    {
        InitializeComponent();
        Native.MakeToolWindow(this, noActivate: false);

        Center.MouseLeftButtonUp += (_, _) => Dismiss();
        KeyDown += OnKeyDown;
        Deactivated += (_, _) => Dismiss();
    }

    internal event Action<ConversionAction, IReadOnlyList<string>>? ActionChosen;

    internal IReadOnlyList<string> Files { get; private set; } = [];

    internal void ShowAt(Native.POINT point, IReadOnlyList<string> files, IReadOnlyList<ConversionAction> actions)
    {
        Files = files;
        _closing = false;
        _summary = Summarize(files);
        _summaryHint = "Pick a format";
        HeadText.Text = _summary;
        SubText.Text = _summaryHint;
        BuildButtons(actions);

        Opacity = 0;
        Show();
        Native.CenterOn(this, point, keepOnScreen: true);
        Activate();
        Native.SetForegroundWindow(Native.Handle(this));

        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1, duration) { EasingFunction = ease });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1, duration) { EasingFunction = ease });

        if (Ring.Children.Count > 0)
        {
            ((Button)Ring.Children[0]).Focus();
        }
    }

    internal void Dismiss()
    {
        if (_closing || !IsVisible)
        {
            return;
        }

        _closing = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) =>
        {
            if (_closing)
            {
                Hide();
                Ring.Children.Clear();
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void BuildButtons(IReadOnlyList<ConversionAction> actions)
    {
        Ring.Children.Clear();
        var n = actions.Count;

        // One ring for up to 12 actions, two rings beyond that.
        if (n <= 12)
        {
            var (radius, size) = n <= 8 ? (122.0, 64.0) : (132.0, 54.0);
            Place(actions, radius, size);
        }
        else
        {
            Place(actions.Take(8).ToList(), 100, 48);
            Place(actions.Skip(8).ToList(), 152, 46);
        }
    }

    private void Place(IReadOnlyList<ConversionAction> actions, double radius, double size)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            var angle = (-90 + (360.0 * i / actions.Count)) * Math.PI / 180;
            var button = new Button
            {
                Content = action.Label,
                Width = size,
                Height = size,
                Style = (Style)FindResource("HaloButton"),
                FontSize = size < 56 ? 11.5 : 13,
                ToolTip = action.Hint,
                Tag = action,
            };
            AutomationHelpText(button, action);

            button.Click += (_, _) => Choose(action);
            button.MouseEnter += (_, _) => ShowHint(action);
            button.GotKeyboardFocus += (_, _) => ShowHint(action);
            button.MouseLeave += (_, _) => ShowSummary();

            Canvas.SetLeft(button, CenterXY + (radius * Math.Cos(angle)) - (size / 2));
            Canvas.SetTop(button, CenterXY + (radius * Math.Sin(angle)) - (size / 2));
            Ring.Children.Add(button);
        }
    }

    private static void AutomationHelpText(Button button, ConversionAction action) =>
        System.Windows.Automation.AutomationProperties.SetHelpText(button, action.Hint);

    private void Choose(ConversionAction action)
    {
        var files = Files;
        Dismiss();
        ActionChosen?.Invoke(action, files);
    }

    private void ShowHint(ConversionAction action)
    {
        HeadText.Text = action.Label;
        SubText.Text = action.Hint;
    }

    private void ShowSummary()
    {
        HeadText.Text = _summary;
        SubText.Text = _summaryHint;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Dismiss();
            e.Handled = true;
            return;
        }

        // Arrow keys move around the ring.
        if (e.Key is Key.Right or Key.Down or Key.Left or Key.Up)
        {
            var buttons = Ring.Children.OfType<Button>().ToList();
            var current = buttons.FindIndex(b => b.IsKeyboardFocused);
            var step = e.Key is Key.Right or Key.Down ? 1 : -1;
            var next = ((current < 0 ? 0 : current + step) + buttons.Count) % Math.Max(1, buttons.Count);
            if (buttons.Count > 0)
            {
                buttons[next].Focus();
            }

            e.Handled = true;
        }
    }

    private static string Summarize(IReadOnlyList<string> files)
    {
        if (files.Count == 1)
        {
            return System.IO.Path.GetFileName(files[0]);
        }

        var kinds = files.Select(Formats.KindOf).Where(k => k != MediaKind.Unknown).Distinct().ToList();
        var noun = kinds.Count == 1
            ? kinds[0] switch
            {
                MediaKind.Image => "images",
                MediaKind.Audio => "audio files",
                MediaKind.Video => "videos",
                MediaKind.Pdf => "PDFs",
                _ => "files",
            }
            : "files";
        return $"{files.Count} {noun}";
    }
}
