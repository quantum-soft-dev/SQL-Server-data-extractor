using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CdcExtractor.App.Controls;

/// <summary>
/// A horizontal step indicator for a multi-step wizard.
/// Shows current, completed, and pending steps with visual differentiation.
/// </summary>
public partial class WizardStepper : UserControl
{
    public static readonly DependencyProperty StepsProperty =
        DependencyProperty.Register(
            nameof(Steps),
            typeof(IReadOnlyList<string>),
            typeof(WizardStepper),
            new PropertyMetadata(null, OnStepsOrCurrentChanged));

    public static readonly DependencyProperty CurrentStepProperty =
        DependencyProperty.Register(
            nameof(CurrentStep),
            typeof(int),
            typeof(WizardStepper),
            new PropertyMetadata(0, OnStepsOrCurrentChanged));

    public WizardStepper()
    {
        InitializeComponent();
    }

    public IReadOnlyList<string> Steps
    {
        get => (IReadOnlyList<string>)GetValue(StepsProperty);
        set => SetValue(StepsProperty, value);
    }

    public int CurrentStep
    {
        get => (int)GetValue(CurrentStepProperty);
        set => SetValue(CurrentStepProperty, value);
    }

    private static void OnStepsOrCurrentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WizardStepper stepper)
        {
            stepper.Rebuild();
        }
    }

    private void Rebuild()
    {
        var steps = Steps;
        if (steps is null || steps.Count == 0)
        {
            StepsItemsControl.ItemsSource = null;
            return;
        }

        var primaryBrush = (Brush)FindResource("PrimaryBrush");
        var surfaceBrush = (Brush)FindResource("SurfaceBrush");
        var borderBrush = (Brush)FindResource("BorderBrush");
        var textPrimaryBrush = (Brush)FindResource("TextPrimaryBrush");
        var textMutedBrush = (Brush)FindResource("TextMutedBrush");
        var successBrush = (Brush)FindResource("SuccessBrush");

        var items = new List<StepViewModel>(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var isCompleted = i < CurrentStep;
            var isCurrent = i == CurrentStep;

            items.Add(new StepViewModel
            {
                Label = steps[i],
                Display = isCompleted ? "\u2713" : (i + 1).ToString(),
                CircleBackground = isCurrent ? primaryBrush : isCompleted ? successBrush : surfaceBrush,
                CircleBorder = isCurrent ? primaryBrush : isCompleted ? successBrush : borderBrush,
                CircleForeground = isCurrent || isCompleted ? Brushes.White : textMutedBrush,
                LabelForeground = isCurrent ? textPrimaryBrush : textMutedBrush,
                LeftLineVisibility = i == 0 ? Visibility.Hidden : Visibility.Visible,
                RightLineVisibility = i == steps.Count - 1 ? Visibility.Hidden : Visibility.Visible,
            });
        }

        StepsItemsControl.ItemsSource = items;
    }

    private sealed class StepViewModel
    {
        public required string Label { get; init; }
        public required string Display { get; init; }
        public required Brush CircleBackground { get; init; }
        public required Brush CircleBorder { get; init; }
        public required Brush CircleForeground { get; init; }
        public required Brush LabelForeground { get; init; }
        public required Visibility LeftLineVisibility { get; init; }
        public required Visibility RightLineVisibility { get; init; }
    }
}
