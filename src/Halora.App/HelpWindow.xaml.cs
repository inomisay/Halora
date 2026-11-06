using System;
using System.Windows;
using System.Windows.Input;

namespace Halora.App;

/// <summary>The "How to use" guide. Opens on first run and from the tray.</summary>
public partial class HelpWindow : Window
{
    public HelpWindow(bool hasFfmpeg, Action showFfmpegHelp, bool hasMagick, Action showMagickHelp)
    {
        InitializeComponent();

        FfmpegPanel.Visibility = hasFfmpeg ? Visibility.Collapsed : Visibility.Visible;
        FfmpegButton.Click += (_, _) => showFfmpegHelp();
        MagickPanel.Visibility = hasMagick ? Visibility.Collapsed : Visibility.Visible;
        MagickButton.Click += (_, _) => showMagickHelp();
        CloseButton.Click += (_, _) => Close();
        DoneButton.Click += (_, _) => Close();

        // Borderless, so the whole card is the title bar.
        Card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }
}
