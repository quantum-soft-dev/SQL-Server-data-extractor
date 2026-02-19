using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CdcExtractor.App.ViewModels;
using CdcExtractor.Contracts.Ipc;
using Microsoft.Extensions.DependencyInjection;

namespace CdcExtractor.App.Views.Manager;

public partial class RunsPage : UserControl
{
    public RunsPage()
    {
        DataContext = App.Services.GetRequiredService<RunsViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is RunsViewModel vm)
        {
            await vm.LoadBatchesCommand.ExecuteAsync(null);
        }
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is RunsViewModel vm && vm.SelectedBatch is not null)
        {
            vm.NavigateToDetailsCommand.Execute(vm.SelectedBatch);
        }
    }
}
