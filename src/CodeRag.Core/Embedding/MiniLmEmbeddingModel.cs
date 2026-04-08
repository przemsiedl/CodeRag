using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CodeRag.Core.Embedding;

/// <summary>
/// Runs all-MiniLM-L6-v2 locally via ONNX Runtime.
/// Produces 384-dimensional L2-normalized sentence embeddings.
/// </summary>
public sealed class MiniLmEmbeddingModel : IOnnxEmbeddingModel
{
    private readonly InferenceSession _session;
    private readonly SimpleTokenizer _tokenizer;
    private const int MaxTokens = 256;
    private const int EmbeddingDim = 384;

    public MiniLmEmbeddingModel(string modelPath, string vocabPath)
    {
        var opts = new SessionOptions();
        opts.EnableCpuMemArena = true;
        _session = new InferenceSession(modelPath, opts);
        _tokenizer = new SimpleTokenizer(vocabPath);
    }

    public float[] Embed(string text)
    {
        var (inputIds, attentionMask, tokenTypeIds) = _tokenizer.Tokenize(text, MaxTokens);
        var seqLen = inputIds.Length;

        var inputIdsTensor = new DenseTensor<long>(inputIds, new[] { 1, seqLen });
        var attentionMaskTensor = new DenseTensor<long>(attentionMask, new[] { 1, seqLen });
        var tokenTypeIdsTensor = new DenseTensor<long>(tokenTypeIds, new[] { 1, seqLen });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
            NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIdsTensor)
        };

        using var results = _session.Run(inputs);
        // last_hidden_state shape: [1, seq_len, 384]
        var hiddenState = results.First(r => r.Name == "last_hidden_state")
            .AsEnumerable<float>().ToArray();

        return MeanPoolAndNormalize(hiddenState, attentionMask, seqLen);
    }

    private static float[] MeanPoolAndNormalize(float[] hiddenState, long[] attentionMask, int seqLen)
    {
        var pooled = new float[EmbeddingDim];
        int validTokens = 0;

        for (int i = 0; i < seqLen; i++)
        {
            if (attentionMask[i] == 0) continue;
            validTokens++;
            for (int d = 0; d < EmbeddingDim; d++)
                pooled[d] += hiddenState[i * EmbeddingDim + d];
        }

        if (validTokens > 0)
            for (int d = 0; d < EmbeddingDim; d++)
                pooled[d] /= validTokens;

        // L2 normalize
        float norm = 0f;
        for (int d = 0; d < EmbeddingDim; d++) norm += pooled[d] * pooled[d];
        norm = MathF.Sqrt(norm);
        if (norm > 1e-9f)
            for (int d = 0; d < EmbeddingDim; d++) pooled[d] /= norm;

        return pooled;
    }

    public void Dispose() => _session.Dispose();
}
