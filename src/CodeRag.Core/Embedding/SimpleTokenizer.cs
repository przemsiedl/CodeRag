namespace CodeRag.Core.Embedding;

/// <summary>
/// Minimal WordPiece tokenizer for BERT-style models (all-MiniLM-L6-v2).
/// Handles basic tokenization without external dependencies.
/// </summary>
public sealed class SimpleTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private const int ClsTokenId = 101;
    private const int SepTokenId = 102;
    private const int UnkTokenId = 100;
    private const int PadTokenId = 0;

    public SimpleTokenizer(string vocabPath)
    {
        _vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        var lines = File.ReadAllLines(vocabPath);
        for (int i = 0; i < lines.Length; i++)
            _vocab[lines[i]] = i;
    }

    public (long[] inputIds, long[] attentionMask, long[] tokenTypeIds) Tokenize(string text, int maxLength)
    {
        var wordPieceIds = WordPieceEncode(text.ToLowerInvariant());

        // Truncate to maxLength - 2 (for [CLS] and [SEP])
        int maxContent = maxLength - 2;
        if (wordPieceIds.Count > maxContent)
            wordPieceIds = wordPieceIds.GetRange(0, maxContent);

        var ids = new long[wordPieceIds.Count + 2];
        ids[0] = ClsTokenId;
        for (int i = 0; i < wordPieceIds.Count; i++)
            ids[i + 1] = wordPieceIds[i];
        ids[^1] = SepTokenId;

        var mask = Enumerable.Repeat(1L, ids.Length).ToArray();
        var typeIds = new long[ids.Length]; // all zeros

        return (ids, mask, typeIds);
    }

    private List<int> WordPieceEncode(string text)
    {
        var result = new List<int>();
        // Split on whitespace and punctuation
        var tokens = SplitIntoTokens(text);
        foreach (var token in tokens)
        {
            if (_vocab.TryGetValue(token, out var id))
            {
                result.Add(id);
                continue;
            }

            // WordPiece: greedily find longest matching subwords
            bool found = false;
            int start = 0;
            var subIds = new List<int>();
            while (start < token.Length)
            {
                int end = token.Length;
                bool matched = false;
                while (start < end)
                {
                    var sub = start == 0 ? token[start..end] : "##" + token[start..end];
                    if (_vocab.TryGetValue(sub, out var subId))
                    {
                        subIds.Add(subId);
                        start = end;
                        matched = true;
                        break;
                    }
                    end--;
                }
                if (!matched)
                {
                    subIds.Clear();
                    subIds.Add(UnkTokenId);
                    break;
                }
            }
            result.AddRange(subIds);
        }
        return result;
    }

    private static IEnumerable<string> SplitIntoTokens(string text)
    {
        var current = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
            }
            else if (char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                yield return ch.ToString();
            }
            else
            {
                current.Append(ch);
            }
        }
        if (current.Length > 0) yield return current.ToString();
    }
}
