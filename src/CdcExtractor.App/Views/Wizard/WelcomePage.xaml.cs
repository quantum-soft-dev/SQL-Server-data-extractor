using System.Windows.Controls;
using CdcExtractor.App.ViewModels.Wizard;

namespace CdcExtractor.App.Views.Wizard;

public partial class WelcomePage : UserControl
{
    public WelcomePage(WelcomeViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
