using System.Windows.Controls;
using CdcExtractor.App.ViewModels.Wizard;

namespace CdcExtractor.App.Views.Wizard;

public partial class CdcPolicyPage : UserControl
{
    public CdcPolicyPage(CdcPolicyViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
