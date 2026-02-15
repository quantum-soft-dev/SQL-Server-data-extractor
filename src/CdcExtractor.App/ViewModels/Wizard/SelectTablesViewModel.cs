using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdcExtractor.App.ViewModels.Wizard;

/// <summary>
/// Step 4 — Discover and select SQL Server tables for CDC tracking.
/// </summary>
public partial class SelectTablesViewModel : ObservableObject
{
    public WizardViewModel Wizard { get; }

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<TableItem> AllTables { get; } = [];
    public ObservableCollection<TableItem> FilteredTables { get; } = [];

    public int SelectedCount => AllTables.Count(t => t.IsSelected);

    public SelectTablesViewModel(WizardViewModel wizard)
    {
        Wizard = wizard;
    }

    partial void OnSearchFilterChanged(string value)
    {
        ApplyFilter();
    }

    [RelayCommand]
    private async Task LoadTablesAsync()
    {
        IsLoading = true;

        try
        {
            // MVP stub — real implementation queries sys.tables via the SQL connection
            await Task.Delay(500);

            AllTables.Clear();
            var sampleTables = new[]
            {
                "dbo.Customers", "dbo.Orders", "dbo.OrderDetails",
                "dbo.Products", "dbo.Categories", "dbo.Suppliers",
                "dbo.Employees", "dbo.Shippers", "dbo.Region",
            };

            foreach (var table in sampleTables)
            {
                var item = new TableItem(table, Wizard.SelectedTables.Contains(table));
                item.PropertyChanged += (_, _) => OnPropertyChanged(nameof(SelectedCount));
                AllTables.Add(item);
            }

            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var t in FilteredTables) t.IsSelected = true;
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var t in FilteredTables) t.IsSelected = false;
    }

    /// <summary>
    /// Writes the selected tables back to the wizard's shared state.
    /// </summary>
    public void SaveToWizard()
    {
        Wizard.SelectedTables = AllTables.Where(t => t.IsSelected).Select(t => t.FullName).ToList();
    }

    private void ApplyFilter()
    {
        FilteredTables.Clear();
        var filter = SearchFilter.Trim();
        foreach (var t in AllTables)
        {
            if (string.IsNullOrEmpty(filter) || t.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredTables.Add(t);
            }
        }
    }
}

/// <summary>
/// Represents a single table row with checkbox state.
/// </summary>
public partial class TableItem : ObservableObject
{
    public string FullName { get; }

    [ObservableProperty]
    private bool _isSelected;

    public TableItem(string fullName, bool isSelected = false)
    {
        FullName = fullName;
        _isSelected = isSelected;
    }
}
