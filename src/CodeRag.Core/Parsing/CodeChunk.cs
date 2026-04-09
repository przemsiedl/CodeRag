namespace CodeRag.Core.Parsing;

/// <summary>Kind of code symbol (applies when ChunkKind == Symbol).</summary>
public enum SymbolKind { Class, Record, Interface, Enum, Method, Constructor, Property, Field }

/// <summary>Kind of index chunk — what the chunk represents in the index.</summary>
public enum ChunkKind { Symbol, FileDocument, SymbolUsage }

public sealed class CodeChunk
{
    public string Id { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string? Namespace { get; init; }
    public string? ParentClass { get; init; }
    public string SymbolName { get; init; } = string.Empty;
    public ChunkKind Kind { get; init; }
    /// <summary>Non-null only when Kind == ChunkKind.Symbol.</summary>
    public SymbolKind? SymbolKind { get; init; }
    public string Modifiers { get; init; } = string.Empty;
    public string Signature { get; init; } = string.Empty;
    public string SourceText { get; init; } = string.Empty;
    public string ContentHash { get; init; } = string.Empty;
    public int ContextHeaderLines { get; init; }
    public int StartLine { get; init; }
    public int EndLine { get; init; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public DateTimeOffset IndexedAt { get; set; } = DateTimeOffset.UtcNow;
}
