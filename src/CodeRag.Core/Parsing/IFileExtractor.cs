namespace CodeRag.Core.Parsing;

public interface IFileExtractor
{
    /// <summary>Returns true if this extractor can handle the given file extension (e.g. ".cs").</summary>
    bool CanHandle(string extension);

    IReadOnlyList<CodeChunk> Extract(string sourceText, string relativePath);
}
