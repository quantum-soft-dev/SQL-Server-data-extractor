using CdcExtractor.Domain.Enums;
using CdcExtractor.Domain.ValueObjects;

namespace CdcExtractor.Domain.Models;

/// <summary>
/// Discovery information for a table, including CDC status and suggested extraction mode.
/// </summary>
public sealed record TableDiscoveryInfo(
    TableIdentifier Table,
    bool CdcEnabled,
    bool HasUniqueKey,
    long EstimatedRowCount,
    ExtractionMode SuggestedMode);
