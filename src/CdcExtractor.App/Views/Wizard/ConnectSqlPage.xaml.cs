using System.Windows.Controls;
using CdcExtractor.App.ViewModels.Wizard;

namespace CdcExtractor.App.Views.Wizard;

public partial class ConnectSqlPage : UserControl
{
    public ConnectSqlPage(ConnectSqlViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
