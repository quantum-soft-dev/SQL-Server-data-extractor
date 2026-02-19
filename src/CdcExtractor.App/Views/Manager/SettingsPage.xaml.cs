using System.Windows;
using System.Windows.Controls;
using CdcExtractor.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CdcExtractor.App.Views.Manager;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        DataContext = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            await vm.LoadCommand.ExecuteAsync(null);
        }
    }
}
