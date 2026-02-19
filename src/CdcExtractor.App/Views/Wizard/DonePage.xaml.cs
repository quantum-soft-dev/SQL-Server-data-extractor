using System.Windows.Controls;
using CdcExtractor.App.ViewModels.Wizard;

namespace CdcExtractor.App.Views.Wizard;

public partial class DonePage : UserControl
{
    public DonePage(DoneViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
