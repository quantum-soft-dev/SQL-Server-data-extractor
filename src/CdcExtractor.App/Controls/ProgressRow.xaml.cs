using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CdcExtractor.App.Controls;

/// <summary>
/// A row displaying a table name, progress bar, percentage, and status chip.
/// </summary>
public partial class ProgressRow : UserControl
{
    public static readonly DependencyProperty TableNameProperty =
        DependencyProperty.Register(
            nameof(TableName),
            typeof(string),
            typeof(ProgressRow),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(
            nameof(Progress),
            typeof(int),
            typeof(ProgressRow),
            new PropertyMetadata(0, OnProgressChanged));

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(
            nameof(Status),
            typeof(string),
            typeof(ProgressRow),
            new PropertyMetadata(string.Empty));

    // Read-only property for formatted percentage text
    private static readonly DependencyPropertyKey PercentageTextPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(PercentageText),
            typeof(string),
            typeof(ProgressRow),
            new PropertyMetadata("0%"));

    public static readonly DependencyProperty PercentageTextProperty =
        PercentageTextPropertyKey.DependencyProperty;

    public ProgressRow()
    {
        InitializeComponent();
    }

    public string TableName
    {
        get => (string)GetValue(TableNameProperty);
        set => SetValue(TableNameProperty, value);
    }

    public int Progress
    {
        get => (int)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string PercentageText => (string)GetValue(PercentageTextProperty);

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressRow row)
        {
            row.UpdateProgress();
        }
    }

    private void UpdateProgress()
    {
        var clamped = Math.Clamp(Progress, 0, 100);
        SetValue(PercentageTextPropertyKey, clamped.ToString(CultureInfo.InvariantCulture) + "%");

        // Update the fill width as a proportion of the parent border
        ProgressFill.Width = double.NaN; // Reset
        ProgressFill.SetValue(WidthProperty, DependencyProperty.UnsetValue);

        // Use a percentage-based approach via binding the width
        if (ProgressFill.Parent is Border parentBorder)
        {
            parentBorder.SizeChanged -= OnParentSizeChanged;
            parentBorder.SizeChanged += OnParentSizeChanged;
        }

        UpdateFillWidth();
    }

    private void OnParentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFillWidth();
    }

    private void UpdateFillWidth()
    {
        if (ProgressFill.Parent is Border parentBorder && parentBorder.ActualWidth > 0)
        {
            var clamped = Math.Clamp(Progress, 0, 100);
            ProgressFill.Width = parentBorder.ActualWidth * clamped / 100.0;
        }
    }
}
