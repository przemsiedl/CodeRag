namespace CodeRag.Core.Embedding;

public interface IOnnxEmbeddingModel : IDisposable
{
    float[] Embed(string text);
}
