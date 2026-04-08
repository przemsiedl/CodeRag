using System.Security.Cryptography;
using System.Text;

namespace CodeRag.Core.Parsing;

public static class ChunkHasher
{
    public static string ComputeId(string relativePath, SymbolKind kind, string fullyQualifiedName)
        => Hash($"{relativePath}|{kind}|{fullyQualifiedName}");

    public static string ComputeContentHash(string sourceText)
        => Hash(sourceText);

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
