using System.Windows;
using System.Windows.Controls;
using CdcExtractor.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CdcExtractor.App.Views.Manager;

public partial class DashboardPage : UserControl
{
    public DashboardPage()
    {
        DataContext = App.Services.GetRequiredService<DashboardViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DashboardViewModel vm)
        {
            await vm.LoadStatusCommand.ExecuteAsync(null);
            await vm.LoadRecentBatchesCommand.ExecuteAsync(null);
        }
    }
}
