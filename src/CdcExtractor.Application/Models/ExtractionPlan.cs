using CdcExtractor.Domain.Entities;
using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Application.Models;

/// <summary>
/// Describes which tables to extract in a batch and how.
/// </summary>
public sealed record ExtractionPlan(
    BatchType BatchType,
    BatchTrigger Trigger,
    IReadOnlyList<TableExtractionTask> Tables);

/// <summary>
/// Extraction task for a single table within a batch.
/// </summary>
public sealed record TableExtractionTask(
    TableIdentifier Table,
    ExtractionMode Mode,
    TableState State,
    SchemaManifest Schema);
