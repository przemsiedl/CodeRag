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

    /// <summary>
    /// Glob patterns for files to index. Supports plain extensions (.cs) and glob paths.
    ///   .cs           → all .cs files (any directory)
    ///   /src/*.cs     → .cs files directly in src/
    ///   /src/**/*.cs  → all .cs files in src/ and subdirectories
    /// </summary>
    public IReadOnlyList<string> IndexedPatterns { get; set; } =
        ["**/*.cs", "**/*.sln", "**/*.csproj", "**/*.json", "**/*.md"];

    /// <summary>
    /// Glob patterns for files/directories to exclude. Ignore wins over include.
    ///   **/bin/**      → ignore bin/ directory at any depth
    ///   *.Design.cs    → ignore by filename pattern
    ///   /src/gen/**    → ignore specific rooted path
    /// </summary>
    public IReadOnlyList<string> IgnorePatterns { get; set; } =
        ["**/bin/**", "**/obj/**", "**/packages/**", "**/.git/**", "**/.rag/**", "**/.vs/**", "**/.claude/**"];

    /// <summary>Project-local .rag folder — only index.db lives here.</summary>
    public string RagDirectory => Path.Combine(ProjectRoot, ".rag");
    public string DatabasePath => Path.Combine(RagDirectory, "index.db");
    public string LockFilePath  => Path.Combine(RagDirectory, "index.lock");

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

            if (overrides.TopK.HasValue)                config.TopK                = overrides.TopK.Value;
            if (overrides.WatchDebounceMs.HasValue)     config.WatchDebounceMs     = overrides.WatchDebounceMs.Value;
            if (overrides.IndexingParallelism.HasValue) config.IndexingParallelism = overrides.IndexingParallelism.Value;
            if (overrides.UseGpu.HasValue)              config.UseGpu              = overrides.UseGpu.Value;
            if (overrides.IndexedPatterns is { Length: > 0 })
                config.IndexedPatterns = overrides.IndexedPatterns;
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
    public string[]? IndexedPatterns { get; set; }
    public string[]? IgnorePatterns { get; set; }
}
