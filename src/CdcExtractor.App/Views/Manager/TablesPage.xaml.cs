using System.Windows;
using System.Windows.Controls;
using CdcExtractor.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CdcExtractor.App.Views.Manager;

public partial class TablesPage : UserControl
{
    public TablesPage()
    {
        DataContext = App.Services.GetRequiredService<TablesViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TablesViewModel vm)
        {
            await vm.LoadTablesCommand.ExecuteAsync(null);
        }
    }
}
