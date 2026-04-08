using CodeRag.Core.Parsing;

namespace CodeRag.Core.Query;

public sealed record QueryResult(
    string RelativePath,
    string? Namespace,
    string? ParentClass,
    string SymbolName,
    SymbolKind Kind,
    string Signature,
    string SourceText,
    int ContextHeaderLines,
    int StartLine,
    int EndLine,
    double Distance
);
