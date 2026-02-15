namespace CdcExtractor.Application.Models;

/// <summary>
/// Result of writing a single gzip CSV chunk during extraction.
/// </summary>
public sealed record ChunkResult(
    int ChunkNumber,
    long RowCount,
    long CompressedSize,
    MemoryStream Data);
