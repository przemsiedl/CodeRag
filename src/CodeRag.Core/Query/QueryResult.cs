using CodeRag.Core.Parsing;

namespace CodeRag.Core.Query;

public sealed record QueryResult(
    string RelativePath,
    string? Namespace,
    string? ParentClass,
    string SymbolName,
    ChunkKind Kind,
    SymbolKind? SymbolKind,
    string Signature,
    string SourceText,
    int ContextHeaderLines,
    int StartLine,
    int EndLine,
    double Distance
);
