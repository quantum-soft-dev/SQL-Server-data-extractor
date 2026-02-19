using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CdcExtractor.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CdcExtractor.App.Views.Manager;

public partial class DiagnosticsPage : UserControl
{
    public DiagnosticsPage()
    {
        DataContext = App.Services.GetRequiredService<DiagnosticsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiagnosticsViewModel vm)
        {
            await vm.RunDiagnosticsCommand.ExecuteAsync(null);
        }
    }
}

/// <summary>
/// Converts a bool value to its inverse.
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value!;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value!;
}

/// <summary>
/// Converts a null or empty string to Collapsed, otherwise Visible.
/// </summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null or "" ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
