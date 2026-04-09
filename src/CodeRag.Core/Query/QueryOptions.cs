using CodeRag.Core.Parsing;

namespace CodeRag.Core.Query;

public sealed class QueryOptions
{
    public int TopK { get; init; } = 5;

    /// <summary>Filter by chunk kind (Symbol / FileDocument / SymbolUsage). Null = all.</summary>
    public IReadOnlySet<ChunkKind>? ChunkKinds { get; init; }

    /// <summary>Filter by code symbol kind (Class, Method, …). Null = all. Only meaningful when ChunkKinds contains Symbol.</summary>
    public IReadOnlySet<SymbolKind>? SymbolKinds { get; init; }

    /// <summary>Filter by parent class name (case-insensitive, partial match).</summary>
    public string? ParentClass { get; init; }

    /// <summary>Filter by file path (case-insensitive, partial match on relative path).</summary>
    public string? InFile { get; init; }

    /// <summary>Filter by namespace (case-insensitive, partial match).</summary>
    public string? InNamespace { get; init; }

    /// <summary>
    /// Filter by file name (case-insensitive, partial match on symbol_name for File-kind chunks).
    /// Automatically restricts results to kind = File.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>When true, SourceText in results is replaced with just the Signature.</summary>
    public bool OnlySignatures { get; init; }

    /// <summary>
    /// Number of context lines to show around the symbol (reads from source file).
    /// 0 = disabled. When set, overrides OnlySignatures for display purposes.
    /// </summary>
    public int ContextLines { get; init; }

    /// <summary>Skip the first N results (pagination).</summary>
    public int Offset { get; init; }
}
