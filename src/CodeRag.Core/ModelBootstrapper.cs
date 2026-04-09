using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CodeRag.Core;

public sealed class ModelBootstrapper
{
    private readonly RagConfiguration _config;
    private readonly ILogger<ModelBootstrapper> _logger;

    // Hugging Face URLs for all-MiniLM-L6-v2 ONNX
    private const string ModelUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx";
    private const string VocabUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt";

    // sqlite-vec releases
    private const string Vec0WindowsUrl = "https://github.com/asg017/sqlite-vec/releases/download/v0.1.9/sqlite-vec-0.1.9-loadable-windows-x86_64.tar.gz";
    private const string Vec0LinuxUrl   = "https://github.com/asg017/sqlite-vec/releases/download/v0.1.9/sqlite-vec-0.1.9-loadable-linux-x86_64.zip";
    private const string Vec0MacUrl     = "https://github.com/asg017/sqlite-vec/releases/download/v0.1.9/sqlite-vec-0.1.9-loadable-macos-aarch64.zip";

    public ModelBootstrapper(RagConfiguration config, ILogger<ModelBootstrapper> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        // Models are stored next to the executable, shared across all projects.
        Directory.CreateDirectory(RagConfiguration.AppDirectory);
        Directory.CreateDirectory(RagConfiguration.ModelsDirectory);

        // Project-local .rag dir is only needed for the database.
        Directory.CreateDirectory(_config.RagDirectory);

        await EnsureFileAsync(RagConfiguration.ModelPath, ModelUrl, "ONNX model", ct);
        await EnsureFileAsync(RagConfiguration.VocabPath, VocabUrl, "vocabulary", ct);
        await EnsureVec0Async(ct);
        EnsureConfig();
        EnsureGitignore();
    }

    private async Task EnsureFileAsync(string localPath, string url, string label, CancellationToken ct)
    {
        if (File.Exists(localPath))
        {
            _logger.LogDebug("{Label} already exists at {Path}", label, localPath);
            return;
        }

        _logger.LogInformation("Downloading {Label}...", label);
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(10);
        var bytes = await http.GetByteArrayAsync(url, ct);
        await File.WriteAllBytesAsync(localPath, bytes, ct);
        _logger.LogInformation("Saved {Label} ({Bytes:N0} bytes)", label, bytes.Length);
    }

    private async Task EnsureVec0Async(CancellationToken ct)
    {
        if (File.Exists(RagConfiguration.Vec0ExtensionPath))
        {
            _logger.LogDebug("sqlite-vec extension already exists");
            return;
        }

        string zipUrl;
        string entryName;

        if (OperatingSystem.IsWindows())
        {
            zipUrl    = Vec0WindowsUrl;
            entryName = "vec0.dll";
        }
        else if (OperatingSystem.IsMacOS())
        {
            zipUrl    = Vec0MacUrl;
            entryName = "vec0.dylib";
        }
        else
        {
            zipUrl    = Vec0LinuxUrl;
            entryName = "vec0.so";
        }

        _logger.LogInformation("Downloading sqlite-vec extension...");
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromMinutes(5);
        var zipBytes = await http.GetByteArrayAsync(zipUrl, ct);

        using var zipStream = new MemoryStream(zipBytes);
        using var archive = new System.IO.Compression.ZipArchive(zipStream);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            throw new FileNotFoundException($"Could not find '{entryName}' in the sqlite-vec zip archive.");

        await using var outStream = File.OpenWrite(RagConfiguration.Vec0ExtensionPath);
        await using var entryStream = entry.Open();
        await entryStream.CopyToAsync(outStream, ct);

        _logger.LogInformation("sqlite-vec extension saved to {Path}", RagConfiguration.Vec0ExtensionPath);
    }

    private void EnsureConfig()
    {
        var configPath = Path.Combine(_config.RagDirectory, "config.json");
        if (File.Exists(configPath))
            return;

        var settings = new
        {
            topK                = _config.TopK,
            watchDebounceMs     = _config.WatchDebounceMs,
            indexingParallelism = _config.IndexingParallelism,
            useGpu              = _config.UseGpu,
            indexedExtensions   = _config.IndexedExtensions,
            ignoredDirectories  = _config.IgnoredDirectories,
            ignorePatterns      = _config.IgnorePatterns,
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, json);
        _logger.LogInformation("Created default config at {Path}", configPath);
    }

    private void EnsureGitignore()
    {
        var gitignorePath = Path.Combine(_config.ProjectRoot, ".gitignore");
        const string entry = ".rag/";

        if (File.Exists(gitignorePath))
        {
            var content = File.ReadAllText(gitignorePath);
            if (!content.Contains(entry))
            {
                File.AppendAllText(gitignorePath, $"\n{entry}\n");
                _logger.LogInformation("Added '{Entry}' to .gitignore", entry);
            }
        }
        else
        {
            File.WriteAllText(gitignorePath, $"{entry}\n");
            _logger.LogInformation("Created .gitignore with '{Entry}'", entry);
        }
    }
}
