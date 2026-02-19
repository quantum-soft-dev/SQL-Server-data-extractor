using System.Windows;
using System.Windows.Controls;
using CdcExtractor.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CdcExtractor.App.Views.Manager;

public partial class RunDetailsPage : UserControl
{
    public RunDetailsPage()
    {
        DataContext = App.Services.GetRequiredService<RunDetailsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is RunDetailsViewModel vm)
        {
            await vm.LoadDetailsCommand.ExecuteAsync(null);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        // Navigate back by finding the parent Frame or NavigationService
        var navService = System.Windows.Navigation.NavigationService.GetNavigationService(this);
        if (navService is { CanGoBack: true })
        {
            navService.GoBack();
        }
    }
}
