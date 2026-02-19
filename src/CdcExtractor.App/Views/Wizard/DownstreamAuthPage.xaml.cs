using System.Windows.Controls;
using CdcExtractor.App.ViewModels.Wizard;

namespace CdcExtractor.App.Views.Wizard;

public partial class DownstreamAuthPage : UserControl
{
    public DownstreamAuthPage(DownstreamAuthViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
