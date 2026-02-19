using CdcExtractor.Application.Services;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.Models;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CdcExtractor.Application.Tests;

public class DiagnosticsServiceTests
{
    private readonly ICdcManager _cdcManager;
    private readonly IDownstreamClient _downstreamClient;
    private readonly DiagnosticsService _sut;

    public DiagnosticsServiceTests()
    {
        _cdcManager = Substitute.For<ICdcManager>();
        _downstreamClient = Substitute.For<IDownstreamClient>();
        _sut = new DiagnosticsService(_cdcManager, _downstreamClient);
    }

    [Fact]
    public async Task RunAllChecks_SqlAgentRunning_ReturnsOkCheck()
    {
        // Arrange
        _cdcManager.IsSqlAgentRunningAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        _cdcManager.IsCdcEnabledOnDatabaseAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var results = await _sut.RunAllChecksAsync();

        // Assert
        results.Should().Contain(c =>
            c.Name == "SQL Server Agent" &&
            c.Status == DiagnosticStatus.Ok);
    }

    [Fact]
    public async Task RunAllChecks_SqlAgentNotRunning_ReturnsFailCheck()
    {
        // Arrange
        _cdcManager.IsSqlAgentRunningAsync(Arg.Any<CancellationToken>())
            .Returns(false);
        _cdcManager.IsCdcEnabledOnDatabaseAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var results = await _sut.RunAllChecksAsync();

        // Assert
        results.Should().Contain(c =>
            c.Name == "SQL Server Agent" &&
            c.Status == DiagnosticStatus.Fail);
    }

    [Fact]
    public async Task RunAllChecks_CdcEnabledOnDatabase_ReturnsOkCheck()
    {
        // Arrange
        _cdcManager.IsCdcEnabledOnDatabaseAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        _cdcManager.IsSqlAgentRunningAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var results = await _sut.RunAllChecksAsync();

        // Assert
        results.Should().Contain(c =>
            c.Name == "CDC database status" &&
            c.Status == DiagnosticStatus.Ok);
    }

    [Fact]
    public async Task RunAllChecks_CdcNotEnabledOnDatabase_ReturnsWarnCheck()
    {
        // Arrange
        _cdcManager.IsCdcEnabledOnDatabaseAsync(Arg.Any<CancellationToken>())
            .Returns(false);
        _cdcManager.IsSqlAgentRunningAsync(Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var results = await _sut.RunAllChecksAsync();

        // Assert
        results.Should().Contain(c =>
            c.Name == "CDC database status" &&
            c.Status == DiagnosticStatus.Warn);
    }

    [Fact]
    public async Task RunAllChecks_ConnectionThrows_ReturnsFailConnectivityCheck()
    {
        // Arrange
        _cdcManager.IsCdcEnabledOnDatabaseAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Connection refused"));
        _cdcManager.IsSqlAgentRunningAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Connection refused"));

        // Act
        var results = await _sut.RunAllChecksAsync();

        // Assert
        results.Should().Contain(c =>
            c.Category == DiagnosticCategory.SqlServer &&
            c.Status == DiagnosticStatus.Fail);
    }

    [Fact]
    public async Task RunAllChecks_ReturnsCompleteListOfAllCheckCategories()
    {
        // Arrange
        _cdcManager.IsSqlAgentRunningAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        _cdcManager.IsCdcEnabledOnDatabaseAsync(Arg.Any<CancellationToken>())
            .Returns(true);
        _downstreamClient.HeartbeatAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var results = await _sut.RunAllChecksAsync();

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(c => c.Category == DiagnosticCategory.SqlServer);
    }
}
