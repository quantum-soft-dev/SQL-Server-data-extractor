using CdcExtractor.Application.Services;
using CdcExtractor.Contracts.Config;
using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.Exceptions;
using CdcExtractor.Domain.Interfaces;
using CdcExtractor.Domain.Models;
using CdcExtractor.Domain.ValueObjects;
using CdcExtractor.Application.Models;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CdcExtractor.Application.Tests;

public class DeltaServiceTests
{
    private readonly ICdcReader _cdcReader;
    private readonly IDownstreamClient _downstreamClient;
    private readonly ChunkingService _chunkingService;
    private readonly IStateStore _stateStore;
    private readonly DeltaService _sut;

    private static readonly TableIdentifier TestTable = new("dbo", "Orders");
    private static readonly Lsn LastProcessedLsn = Lsn.Parse("0x00000027000001E80003");
    private static readonly Lsn IncrementedLsn = Lsn.Parse("0x00000027000001E80004");
    private static readonly Lsn MaxLsn = Lsn.Parse("0x00000027000001F00010");

    private static readonly SchemaHash TestSchemaHash =
        new("abc123def456abc123def456abc123def456abc123def456abc123def456abc1");

    private static readonly SchemaManifest TestManifest = new(
        TestTable,
        DateTimeOffset.UtcNow,
        TestSchemaHash,
        new List<ColumnInfo>
        {
            new("Id", "int", false, null, 10, 0, 1),
            new("Name", "nvarchar", true, 100, null, null, 2)
        },
        new List<string> { "Id" },
        new List<IReadOnlyList<string>>(),
        null);

    public DeltaServiceTests()
    {
        _cdcReader = Substitute.For<ICdcReader>();
        _downstreamClient = Substitute.For<IDownstreamClient>();
        _stateStore = Substitute.For<IStateStore>();

        var config = new ExtractionConfig { MaxBytesPerChunk = 10_485_760 };
        _chunkingService = new ChunkingService(config);

        _sut = new DeltaService(_cdcReader, _downstreamClient, _chunkingService, _stateStore);
    }

    /// <summary>
    /// Creates a <see cref="TableState"/> in Complete status so that
    /// <see cref="TableState.AdvanceLsn"/> can be called.
    /// </summary>
    private static TableState CreateCompletedTableState(
        Lsn lastLsn,
        string captureInstance = "dbo_Orders")
    {
        var state = new TableState(TestTable, ExtractionMode.Cdc);
        state.MarkComplete(lastLsn, TestSchemaHash, DateTimeOffset.UtcNow);
        state.SetCaptureInstance(captureInstance);
        return state;
    }

    /// <summary>
    /// Sets up standard CDC reader mocks for a successful delta extraction:
    /// IncrementLsn, GetMaxLsn, GetMinLsn (no gap), and ReadAllChanges returning the supplied rows.
    /// </summary>
    private void SetupStandardCdcMocks(IEnumerable<CdcChangeRow>? rows = null)
    {
        _cdcReader.IncrementLsnAsync(LastProcessedLsn, Arg.Any<CancellationToken>())
            .Returns(IncrementedLsn);
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(MaxLsn);

        // Default: no gap — min available LSN is below from LSN
        _cdcReader.GetMinLsnAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Lsn.Parse("0x00000020000000000001"));

        var cdcRows = rows?.ToList() ?? new List<CdcChangeRow>();
        _cdcReader.ReadAllChangesAsync(
                Arg.Any<string>(), Arg.Any<Lsn>(), Arg.Any<Lsn>(), Arg.Any<CancellationToken>())
            .Returns(cdcRows.ToAsyncEnumerable());
    }

    /// <summary>Sets up the downstream client to return a dataset id on CreateDatasetAsync.</summary>
    private void SetupDownstreamCreate(string datasetId = "dataset-1")
    {
        _downstreamClient.CreateDatasetAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(datasetId);
    }

    private static List<CdcChangeRow> CreateTestCdcRows() =>
    [
        new CdcChangeRow("I", "0x00000027000001E80005", "0x00000001", DateTimeOffset.UtcNow,
            new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "Order1" }),
        new CdcChangeRow("I", "0x00000027000001E80006", "0x00000002", DateTimeOffset.UtcNow,
            new Dictionary<string, object?> { ["Id"] = 2, ["Name"] = "Order2" })
    ];

    // -----------------------------------------------------------------------
    // Test 1: LSN range — from_lsn calculated by incrementing last processed
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_CalculatesFromLsnByIncrementingLastProcessed()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);
        SetupStandardCdcMocks(CreateTestCdcRows());
        SetupDownstreamCreate();

        // Act
        await _sut.ExtractDeltaAsync(tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — IncrementLsnAsync must be called with the tableState's LastProcessedLsn
        await _cdcReader.Received(1).IncrementLsnAsync(LastProcessedLsn, Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test 2: LSN range — to_lsn fetched via GetMaxLsnAsync
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_CalculatesToLsnViaGetMaxLsn()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);
        SetupStandardCdcMocks(CreateTestCdcRows());
        SetupDownstreamCreate();

        // Act
        await _sut.ExtractDeltaAsync(tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert
        await _cdcReader.Received(1).GetMaxLsnAsync(Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test 3: ReadAllChangesAsync called with correct (incremented from, max to)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_ReadsChangesWithCorrectLsnRange()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);
        SetupStandardCdcMocks(CreateTestCdcRows());
        SetupDownstreamCreate();

        // Act
        await _sut.ExtractDeltaAsync(tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — ReadAllChangesAsync called with incremented from and max to
        _cdcReader.Received(1).ReadAllChangesAsync(
            tableState.CaptureInstance!,
            IncrementedLsn,
            MaxLsn,
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test 4: Chunks uploaded to downstream after reading changes
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_UploadsChunksToDownstream()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);
        SetupStandardCdcMocks(CreateTestCdcRows());
        SetupDownstreamCreate();

        // Act
        await _sut.ExtractDeltaAsync(tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — at least one chunk uploaded
        await _downstreamClient.Received().UploadChunkAsync(
            "dataset-1",
            "lease-1",
            Arg.Any<int>(),
            Arg.Any<Stream>(),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test 5: CommitDatasetAsync called after upload
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_CommitsDataset()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);
        SetupStandardCdcMocks(CreateTestCdcRows());
        SetupDownstreamCreate();

        // Act
        await _sut.ExtractDeltaAsync(tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert
        await _downstreamClient.Received(1).CommitDatasetAsync(
            "dataset-1", "lease-1", Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test 6: LSN advanced ONLY after commit, then state persisted
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_AdvancesLsnOnlyAfterCommit()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);
        SetupStandardCdcMocks(CreateTestCdcRows());
        SetupDownstreamCreate();

        // Act
        await _sut.ExtractDeltaAsync(tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — AdvanceLsn should have moved the LSN to MaxLsn (the to_lsn)
        tableState.LastProcessedLsn.Should().Be(MaxLsn);

        // Assert — UpsertTableStateAsync must be called to persist the advanced state
        await _stateStore.Received(1).UpsertTableStateAsync(tableState, Arg.Any<CancellationToken>());

        // Assert — ordering: commit must happen before state upsert
        Received.InOrder(() =>
        {
            _downstreamClient.CommitDatasetAsync("dataset-1", "lease-1", Arg.Any<CancellationToken>());
            _stateStore.UpsertTableStateAsync(tableState, Arg.Any<CancellationToken>());
        });
    }

    // -----------------------------------------------------------------------
    // Test 7: On failure — LSN NOT advanced, dataset aborted
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_Failure_DoesNotAdvanceLsn()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);
        SetupStandardCdcMocks(CreateTestCdcRows());
        SetupDownstreamCreate();

        _downstreamClient.UploadChunkAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
                Arg.Any<Stream>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Upload failed"));

        // Act
        var act = () => _sut.ExtractDeltaAsync(
            tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — should throw the upload error
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert — LSN must NOT have advanced (still the original)
        tableState.LastProcessedLsn.Should().Be(LastProcessedLsn);

        // Assert — dataset was aborted
        await _downstreamClient.Received(1).AbortDatasetAsync(
            "dataset-1", "lease-1", Arg.Any<CancellationToken>());

        // Assert — state store NOT called to persist (no LSN advancement)
        await _stateStore.DidNotReceive().UpsertTableStateAsync(
            Arg.Any<TableState>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test 8: No changes (from > to) — dataset skipped, no download/upload
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_NoChanges_SkipsDataset()
    {
        // Arrange — make incrementedLsn GREATER than maxLsn so there are no new changes
        var tableState = CreateCompletedTableState(LastProcessedLsn);
        var highIncrementedLsn = Lsn.Parse("0xFFFFFFFFFFFFFFFFFFFF");
        var lowMaxLsn = Lsn.Parse("0x00000027000001E80002");

        _cdcReader.IncrementLsnAsync(LastProcessedLsn, Arg.Any<CancellationToken>())
            .Returns(highIncrementedLsn);
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(lowMaxLsn);

        // Act
        var result = await _sut.ExtractDeltaAsync(
            tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — dataset should be skipped
        result.Status.Should().Be(DatasetStatus.Skipped);

        // Assert — no CDC reads, no uploads, no commits, no state changes
        _cdcReader.DidNotReceive().ReadAllChangesAsync(
            Arg.Any<string>(), Arg.Any<Lsn>(), Arg.Any<Lsn>(), Arg.Any<CancellationToken>());
        await _downstreamClient.DidNotReceive().CreateDatasetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _downstreamClient.DidNotReceive().UploadChunkAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await _downstreamClient.DidNotReceive().CommitDatasetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // Assert — LSN unchanged
        tableState.LastProcessedLsn.Should().Be(LastProcessedLsn);
    }

    // -----------------------------------------------------------------------
    // Test 9: CreateDatasetAsync called with from/to LSN strings
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_CreatesDatasetWithLsnRange()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);
        SetupStandardCdcMocks(CreateTestCdcRows());
        SetupDownstreamCreate();

        // Act
        await _sut.ExtractDeltaAsync(tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — CreateDatasetAsync must include the from/to LSN as strings
        await _downstreamClient.Received(1).CreateDatasetAsync(
            "batch-1",
            "lease-1",
            TestTable.FullName,
            IncrementedLsn.ToString(),
            MaxLsn.ToString(),
            TestManifest.Hash.Value,
            Arg.Any<CancellationToken>());
    }

    // =======================================================================
    // CDC Gap Detection Tests (T096 — US4)
    // =======================================================================

    /// <summary>
    /// Helper: LSN that is GREATER than LastProcessedLsn — simulates CDC history purge.
    /// When GetMinLsnAsync returns this, it means changes before this LSN were cleaned up.
    /// </summary>
    private static readonly Lsn GapMinLsn = Lsn.Parse("0x00000028000000000001");

    // -----------------------------------------------------------------------
    // Test 10: Gap detected — table marked RE_BOOTSTRAP
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_GapDetected_SetsTableStatusToReBootstrap()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);

        _cdcReader.IncrementLsnAsync(LastProcessedLsn, Arg.Any<CancellationToken>())
            .Returns(IncrementedLsn);
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(MaxLsn);

        // Min LSN from CDC > incremented last processed LSN → gap
        _cdcReader.GetMinLsnAsync(tableState.CaptureInstance!, Arg.Any<CancellationToken>())
            .Returns(GapMinLsn);

        // Act
        var result = await _sut.ExtractDeltaAsync(
            tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert
        tableState.BootstrapStatus.Should().Be(BootstrapStatus.ReBootstrap);
    }

    // -----------------------------------------------------------------------
    // Test 11: Gap detected — table state persisted to store
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_GapDetected_PersistsTableState()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);

        _cdcReader.IncrementLsnAsync(LastProcessedLsn, Arg.Any<CancellationToken>())
            .Returns(IncrementedLsn);
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(MaxLsn);
        _cdcReader.GetMinLsnAsync(tableState.CaptureInstance!, Arg.Any<CancellationToken>())
            .Returns(GapMinLsn);

        // Act
        await _sut.ExtractDeltaAsync(
            tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — state store must be called to persist the RE_BOOTSTRAP status
        await _stateStore.Received(1).UpsertTableStateAsync(tableState, Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test 12: Gap detected — no partial data extracted
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_GapDetected_DoesNotReadChanges()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);

        _cdcReader.IncrementLsnAsync(LastProcessedLsn, Arg.Any<CancellationToken>())
            .Returns(IncrementedLsn);
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(MaxLsn);
        _cdcReader.GetMinLsnAsync(tableState.CaptureInstance!, Arg.Any<CancellationToken>())
            .Returns(GapMinLsn);

        // Act
        await _sut.ExtractDeltaAsync(
            tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — NO CDC reads, NO downloads, NO uploads, NO commits
        _cdcReader.DidNotReceive().ReadAllChangesAsync(
            Arg.Any<string>(), Arg.Any<Lsn>(), Arg.Any<Lsn>(), Arg.Any<CancellationToken>());
        await _downstreamClient.DidNotReceive().CreateDatasetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _downstreamClient.DidNotReceive().UploadChunkAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<Stream>(), Arg.Any<CancellationToken>());
        await _downstreamClient.DidNotReceive().CommitDatasetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test 13: Gap detected — error reported to downstream with CDC_GAP_DETECTED
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_GapDetected_ReportsErrorToDownstream()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);

        _cdcReader.IncrementLsnAsync(LastProcessedLsn, Arg.Any<CancellationToken>())
            .Returns(IncrementedLsn);
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(MaxLsn);
        _cdcReader.GetMinLsnAsync(tableState.CaptureInstance!, Arg.Any<CancellationToken>())
            .Returns(GapMinLsn);

        // Act
        await _sut.ExtractDeltaAsync(
            tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — error reported with code CDC_GAP_DETECTED and is_terminal true
        await _downstreamClient.Received(1).ReportErrorAsync(
            "batch-1",
            "lease-1",
            "TABLE",
            TestTable.FullName,
            Arg.Any<string?>(),
            "ERROR",
            "CDC_GAP_DETECTED",
            Arg.Is<string>(m => m.Contains(TestTable.FullName)),
            Arg.Is(false),
            Arg.Is(true),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Test 14: Gap detected — returns aborted dataset with CDC_GAP_DETECTED
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_GapDetected_ReturnsAbortedDataset()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);

        _cdcReader.IncrementLsnAsync(LastProcessedLsn, Arg.Any<CancellationToken>())
            .Returns(IncrementedLsn);
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(MaxLsn);
        _cdcReader.GetMinLsnAsync(tableState.CaptureInstance!, Arg.Any<CancellationToken>())
            .Returns(GapMinLsn);

        // Act
        var result = await _sut.ExtractDeltaAsync(
            tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert
        result.Status.Should().Be(DatasetStatus.Aborted);
        result.ErrorCode.Should().Be("CDC_GAP_DETECTED");
        result.ErrorMessage.Should().Contain(TestTable.FullName);
        result.RowCount.Should().Be(0);
        result.ChunkCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Test 15: Gap detected — LSN NOT advanced
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_GapDetected_DoesNotAdvanceLsn()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);
        var originalLsn = tableState.LastProcessedLsn;

        _cdcReader.IncrementLsnAsync(LastProcessedLsn, Arg.Any<CancellationToken>())
            .Returns(IncrementedLsn);
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(MaxLsn);
        _cdcReader.GetMinLsnAsync(tableState.CaptureInstance!, Arg.Any<CancellationToken>())
            .Returns(GapMinLsn);

        // Act
        await _sut.ExtractDeltaAsync(
            tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — LSN must NOT have advanced; status changed to ReBootstrap so
        // AdvanceLsn would throw anyway, but verify the stored LSN is unchanged
        tableState.LastProcessedLsn.Should().Be(originalLsn);
    }

    // -----------------------------------------------------------------------
    // Test 16: No gap — normal delta extraction proceeds
    // -----------------------------------------------------------------------
    [Fact]
    public async Task ExtractDelta_NoGap_ProceedsNormally()
    {
        // Arrange
        var tableState = CreateCompletedTableState(LastProcessedLsn);

        // Min LSN from CDC is LESS than or equal to incremented LSN → no gap
        var safeMinLsn = Lsn.Parse("0x00000020000000000001");

        _cdcReader.IncrementLsnAsync(LastProcessedLsn, Arg.Any<CancellationToken>())
            .Returns(IncrementedLsn);
        _cdcReader.GetMaxLsnAsync(Arg.Any<CancellationToken>())
            .Returns(MaxLsn);
        _cdcReader.GetMinLsnAsync(tableState.CaptureInstance!, Arg.Any<CancellationToken>())
            .Returns(safeMinLsn);
        _cdcReader.ReadAllChangesAsync(
                Arg.Any<string>(), Arg.Any<Lsn>(), Arg.Any<Lsn>(), Arg.Any<CancellationToken>())
            .Returns(CreateTestCdcRows().ToAsyncEnumerable());
        SetupDownstreamCreate();

        // Act
        var result = await _sut.ExtractDeltaAsync(
            tableState, "batch-1", "lease-1", TestManifest, CancellationToken.None);

        // Assert — normal extraction proceeds
        result.Status.Should().Be(DatasetStatus.Committed);
        tableState.BootstrapStatus.Should().Be(BootstrapStatus.Complete);

        // Assert — no error reported for gap
        await _downstreamClient.DidNotReceive().ReportErrorAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<string>(), "CDC_GAP_DETECTED", Arg.Any<string>(),
            Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }
}
