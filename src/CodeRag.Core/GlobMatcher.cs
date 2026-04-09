using System.Text.RegularExpressions;

namespace CodeRag.Core;

/// <summary>
/// Matches relative file paths against glob patterns.
///
/// Rules:
///   - Pattern containing '/' is matched against the full relative path from project root.
///   - Pattern without '/' is matched against the filename only (any directory).
///   - '*'  matches any character except '/'.
///   - '**' matches any character including '/' (recursive).
///
/// Examples:
///   *.cs              → any .cs file in any directory (filename match)
///   *.Design.cs       → any file ending with .Design.cs (filename match)
///   /src/*.cs         → .cs files directly in src/ (not subdirectories)
///   /src/**/*.cs      → all .cs files in src/ and subdirectories
///   **/bin/**         → anything inside a bin/ directory at any depth
/// </summary>
internal static class GlobMatcher
{
    public static bool Matches(string relativePath, string pattern)
    {
        relativePath = relativePath.Replace('\\', '/').TrimStart('/');

        bool matchFullPath = pattern.Contains('/');
        pattern = pattern.TrimStart('/');

        string subject = matchFullPath ? relativePath : Path.GetFileName(relativePath);
        return MatchGlob(subject, pattern);
    }

    private static bool MatchGlob(string text, string pattern)
    {
        var regexStr = "^" + Regex.Escape(pattern)
            .Replace(@"\*\*", "\x00")   // preserve ** before replacing *
            .Replace(@"\*", "[^/]*")    // * → match anything except /
            .Replace("\x00", ".*")      // ** → match anything including /
            .Replace(@"\?", "[^/]")     // ? → any single char except /
            + "$";

        return Regex.IsMatch(text, regexStr, RegexOptions.IgnoreCase);
    }

    /// <summary>Returns true if the string is a glob pattern (not a plain extension like ".cs").</summary>
    public static bool IsGlob(string pattern) =>
        pattern.Contains('*') || pattern.Contains('?') || pattern.Contains('/');
}
