namespace CodeRag.Core.Parsing;

public enum SymbolKind { Class, Record, Interface, Enum, Method, Constructor, Property, Field, File, Reference }

public sealed class CodeChunk
{
    public string Id { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string? Namespace { get; init; }
    public string? ParentClass { get; init; }
    public string SymbolName { get; init; } = string.Empty;
    public SymbolKind Kind { get; init; }
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
