using System.Windows.Controls;
using CdcExtractor.App.ViewModels.Wizard;

namespace CdcExtractor.App.Views.Wizard;

public partial class ReviewApplyPage : UserControl
{
    public ReviewApplyPage(ReviewApplyViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
