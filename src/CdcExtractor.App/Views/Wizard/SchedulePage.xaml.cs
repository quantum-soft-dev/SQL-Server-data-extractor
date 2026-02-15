using System.Windows.Controls;
using CdcExtractor.App.ViewModels.Wizard;

namespace CdcExtractor.App.Views.Wizard;

public partial class SchedulePage : UserControl
{
    public SchedulePage(ScheduleViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
