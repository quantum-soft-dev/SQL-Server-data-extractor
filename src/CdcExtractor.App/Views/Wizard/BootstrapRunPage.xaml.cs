using System.Windows.Controls;
using CdcExtractor.App.ViewModels.Wizard;

namespace CdcExtractor.App.Views.Wizard;

public partial class BootstrapRunPage : UserControl
{
    public BootstrapRunPage(BootstrapRunViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
