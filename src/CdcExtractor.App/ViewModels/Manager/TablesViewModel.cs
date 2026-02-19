using System.Collections.ObjectModel;
using CdcExtractor.Contracts.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels;

/// <summary>
/// ViewModel for the Tables page.
/// Displays the state of all tracked tables including CDC status, sync lag, and errors.
/// </summary>
public partial class TablesViewModel : ObservableObject
{
    private readonly IExtractorService _service;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<TableStateDto> Tables { get; } = [];

    public TablesViewModel(IExtractorService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    [RelayCommand]
    private async Task LoadTablesAsync(CancellationToken ct = default)
    {
        try
        {
            ErrorMessage = null;
            var result = await _service.GetTableStatesAsync(ct).ConfigureAwait(false);

            Tables.Clear();
            foreach (var table in result.Tables)
            {
                Tables.Add(table);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
