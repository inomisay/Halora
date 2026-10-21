using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Halora.Core;

namespace Halora.App;

/// <summary>A small card in the bottom-right corner that shows one conversion job.</summary>
public partial class ProgressWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _autoClose = new() { Interval = TimeSpan.FromSeconds(6) };
    private string? _revealPath;
    private string _details = "";

    public ProgressWindow()
    {
        InitializeComponent();
        Native.MakeToolWindow(this, noActivate: true);

        CloseButton.Click += (_, _) => CloseCard();
        CancelButton.Click += (_, _) =>
        {
            _cts.Cancel();
            CancelButton.IsEnabled = false;
            DetailText.Text = "Stopping…";
        };
        OpenButton.Click += (_, _) => Reveal();
        DetailsButton.Click += (_, _) =>
            System.Windows.MessageBox.Show(_details, "Halora", MessageBoxButton.OK, MessageBoxImage.Information);

        _autoClose.Tick += (_, _) =>
        {
            if (!IsMouseOver)
            {
                CloseCard();
            }
        };
        // Closing the card while it runs also stops the job.
        Closed += (_, _) =>
        {
            _autoClose.Stop();
            _cts.Cancel();
        };
    }

    internal async Task RunAsync(ConversionService service, ConversionAction action, IReadOnlyList<string> files, int slot)
    {
        HeadText.Text = action.Engine == Engine.CombinePdf ? "Making a PDF" : $"Converting to {action.Label}";
        DetailText.Text = files.Count == 1 ? Path.GetFileName(files[0]) : $"{files.Count} files";
        Show();
        Native.PlaceBottomRight(this, marginPx: 16, stackIndex: slot);

        var progress = new Progress<ConversionProgress>(p =>
        {
            SetFill(p.Total == 0 ? 0 : (double)p.Completed / p.Total);
            if (p.CurrentFile.Length > 0)
            {
                DetailText.Text = p.Total > 1 ? $"{p.Completed + 1} of {p.Total} · {p.CurrentFile}" : p.CurrentFile;
            }
        });

        IReadOnlyList<ConversionResult> results;
        try
        {
            results = await Task.Run(() => service.RunAsync(action, files, progress, _cts.Token));
        }
        catch (OperationCanceledException)
        {
            HeadText.Text = "Cancelled";
            DetailText.Text = "Unfinished files were removed.";
            CancelButton.Visibility = Visibility.Collapsed;
            _autoClose.Start();
            return;
        }

        ShowResults(results);
    }

    private void ShowResults(IReadOnlyList<ConversionResult> results)
    {
        SetFill(1);
        CancelButton.Visibility = Visibility.Collapsed;

        var done = results.Where(r => r.Status == ResultStatus.Done).ToList();
        var failed = results.Where(r => r.Status == ResultStatus.Failed).ToList();
        var skipped = results.Count(r => r.Status == ResultStatus.Skipped);

        _revealPath = done.Select(r => r.Output).FirstOrDefault(p => p is not null);
        OpenButton.Visibility = _revealPath is null ? Visibility.Collapsed : Visibility.Visible;

        if (failed.Count == 0)
        {
            HeadText.Text = done.Count == 1 ? "Done" : $"Done · {done.Count} files";
            DetailText.Text = done.Count == 1
                ? Path.GetFileName(done[0].Output) + (done[0].Message is { } m ? $" · {m}" : "")
                : skipped > 0 ? $"{skipped} skipped (didn't fit this format)" : "Saved next to the originals";
            _autoClose.Start();
            return;
        }

        HeadText.Text = done.Count == 0 ? "Couldn't convert" : $"{done.Count} done, {failed.Count} failed";
        HeadText.Foreground = (Brush)FindResource("ErrorBrush");
        DetailText.Text = failed[0].Message ?? "Unknown error";
        DetailText.ToolTip = DetailText.Text;

        _details = string.Join(
            Environment.NewLine + Environment.NewLine,
            failed.Select(f => $"{Path.GetFileName(f.Source)}{Environment.NewLine}{f.Message}"));
        DetailsButton.Visibility = Visibility.Visible;
    }

    private void SetFill(double fraction)
    {
        var width = Math.Max(0, Track.ActualWidth * Math.Clamp(fraction, 0, 1));
        Fill.BeginAnimation(WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(width, TimeSpan.FromMilliseconds(200)));
    }

    private void Reveal()
    {
        if (_revealPath is null)
        {
            return;
        }

        var args = Directory.Exists(_revealPath) ? $"\"{_revealPath}\"" : $"/select,\"{_revealPath}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
        CloseCard();
    }

    private void CloseCard()
    {
        if (IsLoaded)
        {
            Close();
        }
    }
}
