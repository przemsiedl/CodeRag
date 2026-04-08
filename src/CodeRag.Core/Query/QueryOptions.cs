using CodeRag.Core.Parsing;

namespace CodeRag.Core.Query;

public sealed class QueryOptions
{
    public int TopK { get; init; } = 5;

    /// <summary>Filter by symbol kinds. Null = all kinds.</summary>
    public IReadOnlySet<SymbolKind>? Kinds { get; init; }

    /// <summary>Filter by parent class name (case-insensitive, partial match).</summary>
    public string? ParentClass { get; init; }

    /// <summary>Filter by file path (case-insensitive, partial match on relative path).</summary>
    public string? InFile { get; init; }

    /// <summary>
    /// Filter by file name (case-insensitive, partial match on symbol_name for File-kind chunks).
    /// Automatically restricts results to kind = File.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>When true, SourceText in results is replaced with just the Signature.</summary>
    public bool OnlySignatures { get; init; }
}
