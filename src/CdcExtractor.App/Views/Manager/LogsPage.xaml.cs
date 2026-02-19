using System.Windows;
using System.Windows.Controls;
using CdcExtractor.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CdcExtractor.App.Views.Manager;

public partial class LogsPage : UserControl
{
    public LogsPage()
    {
        DataContext = App.Services.GetRequiredService<LogsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is LogsViewModel vm)
        {
            await vm.LoadLogsCommand.ExecuteAsync(null);
        }
    }
}
