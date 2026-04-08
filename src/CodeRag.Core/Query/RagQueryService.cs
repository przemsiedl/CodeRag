using CodeRag.Core.Embedding;
using CodeRag.Core.Storage;

namespace CodeRag.Core.Query;

public sealed class RagQueryService
{
    private readonly IOnnxEmbeddingModel _embeddingModel;
    private readonly IChunkRepository _repository;

    public RagQueryService(IOnnxEmbeddingModel embeddingModel, IChunkRepository repository)
    {
        _embeddingModel = embeddingModel;
        _repository = repository;
    }

    public async Task<IReadOnlyList<QueryResult>> QueryAsync(
        string? naturalLanguageQuery,
        QueryOptions options,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(naturalLanguageQuery))
        {
            var embedding = _embeddingModel.Embed(naturalLanguageQuery);
            return await _repository.SearchAsync(embedding, options, ct);
        }

        return await _repository.GetByFiltersAsync(options, ct);
    }
}
