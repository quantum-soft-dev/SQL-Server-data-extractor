using System.Windows.Controls;
using CdcExtractor.App.ViewModels.Wizard;

namespace CdcExtractor.App.Views.Wizard;

public partial class SelectTablesPage : UserControl
{
    public SelectTablesPage(SelectTablesViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
