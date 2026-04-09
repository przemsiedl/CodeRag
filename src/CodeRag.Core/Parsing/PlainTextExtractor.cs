namespace CodeRag.Core.Parsing;

/// <summary>
/// Treats the entire file as a single File-level chunk.
/// Used for non-C# files such as .sln, .csproj, .json, .md, etc.
/// </summary>
public sealed class PlainTextExtractor : IFileExtractor
{
    private readonly IReadOnlySet<string> _extensions;

    public PlainTextExtractor(IEnumerable<string> extensions)
    {
        _extensions = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
    }

    public bool CanHandle(string extension) => _extensions.Contains(extension);

    public IReadOnlyList<CodeChunk> Extract(string sourceText, string relativePath)
    {
        var trimmed = sourceText.TrimEnd();
        var lineCount = trimmed.Split('\n').Length;

        return
        [
            new CodeChunk
            {
                Id          = ChunkHasher.ComputeId(relativePath, ChunkKind.FileDocument, relativePath),
                RelativePath = relativePath,
                SymbolName  = Path.GetFileName(relativePath),
                Kind        = ChunkKind.FileDocument,
                Modifiers   = string.Empty,
                Signature   = relativePath,
                SourceText  = trimmed,
                ContentHash = ChunkHasher.ComputeContentHash(sourceText),
                StartLine   = 1,
                EndLine     = lineCount
            }
        ];
    }
}
