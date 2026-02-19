using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CdcExtractor.Contracts.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels;

/// <summary>
/// ViewModel for the Manager Diagnostics page.
/// Runs diagnostic checks against the extractor service and displays results grouped by category.
/// </summary>
public partial class DiagnosticsViewModel : ObservableObject
{
    private readonly IExtractorService _service;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<DiagnosticCheckDto> Checks { get; } = [];

    public ICollectionView GroupedChecks { get; }

    public DiagnosticsViewModel(IExtractorService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;

        GroupedChecks = CollectionViewSource.GetDefaultView(Checks);
        GroupedChecks.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DiagnosticCheckDto.Category)));
    }

    [RelayCommand]
    private async Task RunDiagnosticsAsync(CancellationToken ct = default)
    {
        try
        {
            IsRunning = true;
            ErrorMessage = null;

            var result = await _service.RunDiagnosticsAsync(ct).ConfigureAwait(false);

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                Checks.Clear();
                foreach (var check in result.Checks)
                {
                    Checks.Add(check);
                }
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }
}
