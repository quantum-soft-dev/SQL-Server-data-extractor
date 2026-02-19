using System.Collections.ObjectModel;
using CdcExtractor.App.Services;
using CdcExtractor.App.Views.Manager;
using CdcExtractor.Contracts.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CdcExtractor.App.ViewModels;

/// <summary>
/// ViewModel for the Runs page.
/// Displays a list of all batch runs and allows navigation to run details.
/// </summary>
public partial class RunsViewModel : ObservableObject
{
    private readonly IExtractorService _service;
    private readonly NavigationService _navigationService;

    [ObservableProperty]
    private BatchSummaryDto? _selectedBatch;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<BatchSummaryDto> Batches { get; } = [];

    public RunsViewModel(IExtractorService service, NavigationService navigationService)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(navigationService);
        _service = service;
        _navigationService = navigationService;
    }

    [RelayCommand]
    private async Task LoadBatchesAsync(CancellationToken ct = default)
    {
        try
        {
            ErrorMessage = null;
            var result = await _service.GetRecentBatchesAsync(50, ct).ConfigureAwait(false);

            Batches.Clear();
            foreach (var batch in result.Batches)
            {
                Batches.Add(batch);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void NavigateToDetails(BatchSummaryDto? batch)
    {
        if (batch is null)
        {
            return;
        }

        // Set the batch ID on the RunDetailsViewModel before navigating
        var detailsVm = App.Services.GetRequiredService<RunDetailsViewModel>();
        detailsVm.BatchId = batch.BatchId;

        _navigationService.NavigateTo<RunDetailsPage>();
    }
}
