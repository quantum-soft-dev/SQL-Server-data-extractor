using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CdcExtractor.App.Controls;

/// <summary>
/// A small colored tag/badge for batch types and statuses.
/// </summary>
public partial class TagBadge : UserControl
{
    public static new readonly DependencyProperty TagProperty =
        DependencyProperty.Register(
            nameof(Tag),
            typeof(string),
            typeof(TagBadge),
            new PropertyMetadata(string.Empty, OnTagChanged));

    public TagBadge()
    {
        InitializeComponent();
    }

    public new string Tag
    {
        get => (string)GetValue(TagProperty);
        set => SetValue(TagProperty, value);
    }

    private static void OnTagChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TagBadge badge)
        {
            badge.UpdateAppearance();
        }
    }

    private void UpdateAppearance()
    {
        var tag = (Tag ?? string.Empty).Trim().ToUpperInvariant();
        BadgeText.Text = Tag ?? string.Empty;

        var (background, foreground) = ResolveColors(tag);
        BadgeBorder.Background = background;
        BadgeText.Foreground = foreground;
    }

    private static (SolidColorBrush Background, SolidColorBrush Foreground) ResolveColors(string tag)
    {
        return tag switch
        {
            "SUCCEEDED" =>
                (new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7)),
                 new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A))),
            "FAILED" =>
                (new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),
                 new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26))),
            "RUNNING" or "DELTA" =>
                (new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE)),
                 new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))),
            "ABORTED" =>
                (new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                 new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))),
            "SNAPSHOT" =>
                (new SolidColorBrush(Color.FromRgb(0xF3, 0xE8, 0xFF)),
                 new SolidColorBrush(Color.FromRgb(0x93, 0x33, 0xEA))),
            "MANUAL" =>
                (new SolidColorBrush(Color.FromRgb(0xFF, 0xF7, 0xED)),
                 new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C))),
            "SCHEDULED" =>
                (new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                 new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69))),
            _ =>
                (new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                 new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B))),
        };
    }
}
