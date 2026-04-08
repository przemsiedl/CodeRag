using System.CommandLine;
using System.CommandLine.Invocation;

var stateFile = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".fe_current");

string? ReadCurrentFile() =>
    File.Exists(stateFile) ? File.ReadAllText(stateFile).Trim() : null;

void RequireFile(out string path)
{
    path = ReadCurrentFile()
           ?? throw new InvalidOperationException("No file set. Use --set-file first.");
}

// ── root command ──────────────────────────────────────────────────────────────
var root = new RootCommand("fe — file edit helper");

// --set-file / -sf
var setFileOpt = new Option<string?>(["--set-file", "-sf"], "Set the active file for subsequent commands");
root.AddOption(setFileOpt);

// --del-file / -df
var delFileOpt = new Option<bool>(["--del-file", "-df"], "Delete the active file");
root.AddOption(delFileOpt);

// --move-file / -mf
var moveFileOpt = new Option<string?>(["--move-file", "-mf"], "Move the active file to a new path");
root.AddOption(moveFileOpt);

// --del-line / -dl
var delLineOpt = new Option<int?>(["--del-line", "-dl"], "Delete line at 1-based index n");
root.AddOption(delLineOpt);

// --add-line / -al  (n content)
var addLineIndexOpt = new Option<int?>(["--add-line-at", "-ali"], "Line index to insert before (0 = append)");
var addLineContentOpt = new Option<string?>(["--add-line", "-al"], "Append a line with the given content");
root.AddOption(addLineIndexOpt);
root.AddOption(addLineContentOpt);

// --replace-line / -rl  (n content — two values; handled as two options)
var replaceLineIndexOpt = new Option<int?>(["--replace-line", "-rl"], "Replace line at 1-based index n");
var replaceLineContentOpt = new Option<string?>(["--replace-content", "-rc"], "Content for the replaced line");
root.AddOption(replaceLineIndexOpt);
root.AddOption(replaceLineContentOpt);

root.SetHandler((InvocationContext ctx) =>
{
    var setFile       = ctx.ParseResult.GetValueForOption(setFileOpt);
    var delFile       = ctx.ParseResult.GetValueForOption(delFileOpt);
    var moveFile      = ctx.ParseResult.GetValueForOption(moveFileOpt);
    var delLine       = ctx.ParseResult.GetValueForOption(delLineOpt);
    var addLineAt     = ctx.ParseResult.GetValueForOption(addLineIndexOpt);
    var addLine       = ctx.ParseResult.GetValueForOption(addLineContentOpt);
    var replaceLine   = ctx.ParseResult.GetValueForOption(replaceLineIndexOpt);
    var replaceContent = ctx.ParseResult.GetValueForOption(replaceLineContentOpt);

    // --set-file
    if (setFile != null)
    {
        var abs = Path.GetFullPath(setFile);
        File.WriteAllText(stateFile, abs);
        Console.WriteLine($"Active file: {abs}");
        return;
    }

    // --del-file
    if (delFile)
    {
        RequireFile(out var path);
        File.Delete(path);
        File.Delete(stateFile);
        Console.WriteLine($"Deleted: {path}");
        return;
    }

    // --move-file
    if (moveFile != null)
    {
        RequireFile(out var path);
        var dest = Path.GetFullPath(moveFile);
        var destDir = Path.GetDirectoryName(dest);
        if (destDir != null) Directory.CreateDirectory(destDir);
        File.Move(path, dest);
        File.WriteAllText(stateFile, dest);
        Console.WriteLine($"Moved: {path} → {dest}");
        return;
    }

    // --del-line
    if (delLine.HasValue)
    {
        RequireFile(out var path);
        var lines = ReadLines(path);
        int idx = delLine.Value - 1;
        if (idx < 0 || idx >= lines.Count)
        {
            Console.Error.WriteLine($"Error: line index {delLine.Value} out of range (1–{lines.Count})");
            ctx.ExitCode = 1;
            return;
        }
        lines.RemoveAt(idx);
        WriteLines(path, lines);
        Console.WriteLine($"Deleted line {delLine.Value}");
        return;
    }

    // --add-line
    if (addLine != null)
    {
        RequireFile(out var path);
        var lines = File.Exists(path) ? ReadLines(path) : new List<string>();
        if (addLineAt.HasValue && addLineAt.Value > 0 && addLineAt.Value <= lines.Count)
            lines.Insert(addLineAt.Value - 1, addLine);
        else
            lines.Add(addLine);
        WriteLines(path, lines);
        Console.WriteLine($"Added line ({lines.Count})");
        return;
    }

    // --replace-line
    if (replaceLine.HasValue)
    {
        if (replaceContent == null)
        {
            Console.Error.WriteLine("Error: --replace-content/-rc is required with --replace-line");
            ctx.ExitCode = 1;
            return;
        }
        RequireFile(out var path);
        var lines = ReadLines(path);
        int idx = replaceLine.Value - 1;
        if (idx < 0 || idx >= lines.Count)
        {
            Console.Error.WriteLine($"Error: line index {replaceLine.Value} out of range (1–{lines.Count})");
            ctx.ExitCode = 1;
            return;
        }
        lines[idx] = replaceContent;
        WriteLines(path, lines);
        Console.WriteLine($"Replaced line {replaceLine.Value}");
        return;
    }

    // No option given — show current file
    var current = ReadCurrentFile();
    Console.WriteLine(current != null ? $"Active file: {current}" : "No active file set.");
});

return await root.InvokeAsync(args);

static List<string> ReadLines(string path) =>
    File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();

static void WriteLines(string path, List<string> lines)
{
    var dir = Path.GetDirectoryName(path);
    if (dir != null) Directory.CreateDirectory(dir);
    File.WriteAllLines(path, lines);
}
