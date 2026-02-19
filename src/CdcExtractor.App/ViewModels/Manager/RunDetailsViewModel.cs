using System.Collections.ObjectModel;
using CdcExtractor.Contracts.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels;

/// <summary>
/// ViewModel for the Run Details page.
/// Displays batch header info, dataset details, and errors for a specific batch.
/// </summary>
public partial class RunDetailsViewModel : ObservableObject
{
    private readonly IExtractorService _service;

    [ObservableProperty]
    private string _batchId = string.Empty;

    [ObservableProperty]
    private BatchDetailsDto? _batchDetails;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _duration = string.Empty;

    public ObservableCollection<DatasetDetailDto> Datasets { get; } = [];

    public ObservableCollection<BatchErrorDto> Errors { get; } = [];

    public RunDetailsViewModel(IExtractorService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    [RelayCommand]
    private async Task LoadDetailsAsync(CancellationToken ct = default)
    {
        try
        {
            ErrorMessage = null;

            if (string.IsNullOrWhiteSpace(BatchId))
            {
                ErrorMessage = "No batch ID specified.";
                return;
            }

            var details = await _service.GetBatchDetailsAsync(BatchId, ct).ConfigureAwait(false);
            BatchDetails = details;

            if (details.FinishedAt.HasValue)
            {
                var elapsed = details.FinishedAt.Value - details.StartedAt;
                Duration = elapsed.TotalSeconds < 60
                    ? $"{elapsed.TotalSeconds:F1}s"
                    : $"{elapsed.TotalMinutes:F1}m";
            }
            else
            {
                Duration = "In progress";
            }

            Datasets.Clear();
            foreach (var dataset in details.Datasets)
            {
                Datasets.Add(dataset);
            }

            Errors.Clear();
            foreach (var error in details.Errors)
            {
                Errors.Add(error);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
