using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CdcExtractor.App.Controls;

/// <summary>
/// A small colored status badge that maps a status string to an appropriate color.
/// </summary>
public partial class StatusChip : UserControl
{
    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(
            nameof(Status),
            typeof(string),
            typeof(StatusChip),
            new PropertyMetadata(string.Empty, OnStatusChanged));

    public StatusChip()
    {
        InitializeComponent();
        UpdateAppearance();
    }

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusChip chip)
        {
            chip.UpdateAppearance();
        }
    }

    private void UpdateAppearance()
    {
        var status = (Status ?? string.Empty).Trim().ToUpperInvariant();
        ChipText.Text = Status ?? string.Empty;

        var (background, foreground) = ResolveColors(status);
        ChipBorder.Background = background;
        ChipText.Foreground = foreground;
    }

    private static (SolidColorBrush Background, SolidColorBrush Foreground) ResolveColors(string status)
    {
        return status switch
        {
            "OK" or "PASS" or "SUCCEEDED" or "COMPLETE" =>
                (new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7)), new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A))),
            "WARN" or "REBOOTSTRAP" or "RE_BOOTSTRAP" =>
                (new SolidColorBrush(Color.FromRgb(0xFE, 0xF9, 0xC3)), new SolidColorBrush(Color.FromRgb(0xCA, 0x8A, 0x04))),
            "FAIL" or "FAILED" or "ERROR" =>
                (new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)), new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))),
            "RUNNING" =>
                (new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE)), new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))),
            "PENDING" =>
                (new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)), new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))),
            _ =>
                (new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)), new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))),
        };
    }
}
