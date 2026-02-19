using CdcExtractor.App.ViewModels.Wizard;
using CdcExtractor.Contracts.Config;
using FluentAssertions;

namespace CdcExtractor.App.Tests.ViewModels;

public class WizardViewModelTests
{
    private readonly WizardViewModel _sut = new();

    [Fact]
    public void Constructor_SetsInitialState()
    {
        _sut.CurrentStep.Should().Be(0);
        _sut.TotalSteps.Should().Be(9);
        _sut.IsCurrentStepValid.Should().BeTrue();
        _sut.IsApplying.Should().BeFalse();
    }

    [Fact]
    public void StepNames_ContainsExactlyNineEntries()
    {
        WizardViewModel.StepNames.Should().HaveCount(9);
        WizardViewModel.StepNames.Should().ContainInOrder(
            "Welcome", "Connect SQL", "Downstream Auth", "Select Tables",
            "CDC Policy", "Schedule", "Review & Apply", "Bootstrap Run", "Done");
    }

    [Fact]
    public void Next_AdvancesStepByOne()
    {
        _sut.NextCommand.Execute(null);
        _sut.CurrentStep.Should().Be(1);

        _sut.NextCommand.Execute(null);
        _sut.CurrentStep.Should().Be(2);
    }

    [Fact]
    public void Next_CannotGoBeyondLastStep()
    {
        // Navigate to the last step (step 8 = "Done")
        for (var i = 0; i < 8; i++)
        {
            _sut.NextCommand.Execute(null);
        }

        _sut.CurrentStep.Should().Be(8);
        _sut.NextCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Back_GoesBackOnStep()
    {
        _sut.NextCommand.Execute(null);
        _sut.NextCommand.Execute(null);
        _sut.CurrentStep.Should().Be(2);

        _sut.BackCommand.Execute(null);
        _sut.CurrentStep.Should().Be(1);
    }

    [Fact]
    public void Back_CannotGoBelowStepZero()
    {
        _sut.CurrentStep.Should().Be(0);
        _sut.BackCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Next_IsDisabled_WhenIsCurrentStepValidIsFalse()
    {
        _sut.IsCurrentStepValid = false;

        _sut.NextCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Next_IsReEnabled_WhenIsCurrentStepValidSetBackToTrue()
    {
        _sut.IsCurrentStepValid = false;
        _sut.NextCommand.CanExecute(null).Should().BeFalse();

        _sut.IsCurrentStepValid = true;
        _sut.NextCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Cancel_ResetsAllState()
    {
        // Advance a few steps and accumulate state
        _sut.NextCommand.Execute(null);
        _sut.NextCommand.Execute(null);
        _sut.SqlServerConfig = new SqlServerConfig { Server = "myserver", Database = "mydb" };
        _sut.DownstreamConfig = new DownstreamConfig { BaseUrl = "https://example.com" };
        _sut.ScheduleConfig = new ScheduleConfig { Timezone = "America/New_York" };
        _sut.CdcConfig = new CdcConfig { RetentionMinDays = 30 };
        _sut.ExtractionConfig = new ExtractionConfig { MaxBytesPerChunk = 999 };
        _sut.SelectedTables = ["dbo.Orders", "dbo.Customers"];
        _sut.IsCurrentStepValid = false;

        _sut.CancelCommand.Execute(null);

        _sut.CurrentStep.Should().Be(0);
        _sut.SqlServerConfig.Server.Should().BeEmpty();
        _sut.DownstreamConfig.BaseUrl.Should().BeEmpty();
        _sut.ScheduleConfig.Timezone.Should().Be("UTC");
        _sut.CdcConfig.RetentionMinDays.Should().Be(7);
        _sut.ExtractionConfig.MaxBytesPerChunk.Should().Be(10_485_760);
        _sut.SelectedTables.Should().BeEmpty();
    }

    [Fact]
    public void BuildConfig_ReturnsAppConfigWithAccumulatedState()
    {
        var sqlConfig = new SqlServerConfig { Server = "srv1", Database = "db1", Encrypt = false };
        var downConfig = new DownstreamConfig { BaseUrl = "https://api.test", ClientId = "client-42" };
        var schedConfig = new ScheduleConfig { Timezone = "Europe/London", PreventParallelRuns = false };
        var cdcCfg = new CdcConfig { AutoEnableDatabase = false, RetentionMinDays = 14 };
        var extractCfg = new ExtractionConfig { MaxBytesPerChunk = 5_000_000, SmallTableSnapThreshold = 500 };

        _sut.SqlServerConfig = sqlConfig;
        _sut.DownstreamConfig = downConfig;
        _sut.ScheduleConfig = schedConfig;
        _sut.CdcConfig = cdcCfg;
        _sut.ExtractionConfig = extractCfg;

        var config = _sut.BuildConfig();

        config.Should().NotBeNull();
        config.SqlServer.Should().Be(sqlConfig);
        config.Downstream.Should().Be(downConfig);
        config.Schedule.Should().Be(schedConfig);
        config.Cdc.Should().Be(cdcCfg);
        config.Extraction.Should().Be(extractCfg);
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, true, true)]
    [InlineData(4, true, true)]
    [InlineData(8, false, true)]
    public void NavigationCommands_CanExecute_ReflectsCurrentStep(
        int targetStep, bool expectedCanNext, bool expectedCanBack)
    {
        // Navigate to the target step
        for (var i = 0; i < targetStep; i++)
        {
            _sut.NextCommand.Execute(null);
        }

        _sut.CurrentStep.Should().Be(targetStep);
        _sut.NextCommand.CanExecute(null).Should().Be(expectedCanNext);
        _sut.BackCommand.CanExecute(null).Should().Be(expectedCanBack);
    }

    [Fact]
    public void Next_ResetsIsCurrentStepValid_OnNewStep()
    {
        _sut.NextCommand.Execute(null);

        // Invalidate step 1
        _sut.IsCurrentStepValid = false;
        _sut.IsCurrentStepValid.Should().BeFalse();

        // Re-enable to allow navigation, then advance
        _sut.IsCurrentStepValid = true;
        _sut.NextCommand.Execute(null);

        // Step 2 should start as valid (Next resets IsCurrentStepValid to true)
        _sut.CurrentStep.Should().Be(2);
        _sut.IsCurrentStepValid.Should().BeTrue();
    }
}
