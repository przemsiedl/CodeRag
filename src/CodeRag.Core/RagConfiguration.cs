using System.Text.Json;

namespace CodeRag.Core;

public sealed class RagConfiguration
{
    public string ProjectRoot { get; set; } = Directory.GetCurrentDirectory();
    public int TopK { get; set; } = 5;
    public int WatchDebounceMs { get; set; } = 500;
    public int IndexingParallelism { get; set; } = 4;
    /// <summary>
    /// Use DirectML GPU acceleration for embeddings. Requires a DirectML-capable GPU and driver.
    /// Note: some GPU/driver combinations may not support all ONNX ops used by this model — leave false if you experience crashes.
    /// </summary>
    public bool UseGpu { get; set; } = false;

    /// <summary>File extensions to index. Configurable via .rag/config.json.</summary>
    public IReadOnlyList<string> IndexedExtensions { get; set; } =
        [".cs", ".sln", ".csproj", ".json", ".md"];

    /// <summary>Directory names to exclude (matched against any path segment).</summary>
    public IReadOnlyList<string> IgnoredDirectories { get; set; } =
        ["bin", "obj", "packages", ".git", ".rag"];

    /// <summary>Glob-style filename patterns to exclude (e.g. "*.Design.cs", "*.doc.md"). Ignore wins over include.</summary>
    public IReadOnlyList<string> IgnorePatterns { get; set; } = [];

    /// <summary>Project-local .rag folder — only index.db lives here.</summary>
    public string RagDirectory => Path.Combine(ProjectRoot, ".rag");
    public string DatabasePath => Path.Combine(RagDirectory, "index.db");

    /// <summary>Directory where the executable lives — models and extensions are stored here.</summary>
    public static string AppDirectory =>
        Path.GetDirectoryName(Environment.ProcessPath ?? AppContext.BaseDirectory)
        ?? AppContext.BaseDirectory;

    public static string ModelsDirectory => Path.Combine(AppDirectory, "models");
    public static string ModelPath => Path.Combine(ModelsDirectory, "all-MiniLM-L6-v2.onnx");
    public static string VocabPath => Path.Combine(ModelsDirectory, "vocab.txt");
    public static string Vec0ExtensionPath => Path.Combine(ModelsDirectory, Vec0ExtensionName);

    public static string Vec0ExtensionName =>
        OperatingSystem.IsWindows() ? "vec0.dll" :
        OperatingSystem.IsMacOS()   ? "vec0.dylib" : "vec0.so";

    /// <summary>
    /// Loads configuration from .rag/config.json in the project root (if it exists),
    /// overriding defaults with any values present in the file.
    /// </summary>
    public static RagConfiguration Load(string projectRoot)
    {
        var config = new RagConfiguration { ProjectRoot = Path.GetFullPath(projectRoot) };
        var configFile = Path.Combine(config.RagDirectory, "config.json");

        if (!File.Exists(configFile))
            return config;

        try
        {
            using var stream = File.OpenRead(configFile);
            var overrides = JsonSerializer.Deserialize<RagConfigurationOverrides>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (overrides is null)
                return config;

            if (overrides.TopK.HasValue)              config.TopK              = overrides.TopK.Value;
            if (overrides.WatchDebounceMs.HasValue)   config.WatchDebounceMs   = overrides.WatchDebounceMs.Value;
            if (overrides.IndexingParallelism.HasValue) config.IndexingParallelism = overrides.IndexingParallelism.Value;
            if (overrides.UseGpu.HasValue)              config.UseGpu              = overrides.UseGpu.Value;
            if (overrides.IndexedExtensions is { Length: > 0 })
                config.IndexedExtensions = overrides.IndexedExtensions;
            if (overrides.IgnoredDirectories is not null)
                config.IgnoredDirectories = overrides.IgnoredDirectories;
            if (overrides.IgnorePatterns is not null)
                config.IgnorePatterns = overrides.IgnorePatterns;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: failed to read {configFile}: {ex.Message}");
        }

        return config;
    }
}

/// <summary>Partial view of RagConfiguration for JSON deserialization (all fields optional).</summary>
file sealed class RagConfigurationOverrides
{
    public int? TopK { get; set; }
    public int? WatchDebounceMs { get; set; }
    public int? IndexingParallelism { get; set; }
    public bool? UseGpu { get; set; }
    public string[]? IndexedExtensions { get; set; }
    public string[]? IgnoredDirectories { get; set; }
    public string[]? IgnorePatterns { get; set; }
}
