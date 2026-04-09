using System.CommandLine;
using System.CommandLine.Invocation;

var stateFile = Path.Combine(Directory.GetCurrentDirectory(), ".rag", "fe_current");

string? ReadCurrentFile() =>
    File.Exists(stateFile) ? File.ReadAllText(stateFile).Trim() : null;

void RequireFile(out string path)
{
    path = ReadCurrentFile()
           ?? throw new InvalidOperationException("No file set. Use --set-file first.");
}

// ── root command ──────────────────────────────────────────────────────────────
var root = new RootCommand("fe — file edit helper");

// custom --help
var helpOpt = new Option<bool>(["-h", "--help", "-?"], "Show help and usage information");
root.AddOption(helpOpt);

// --set-file / -sf
var setFileOpt = new Option<string?>(["--set-file", "-sf"], "Set the active file for subsequent commands");
root.AddOption(setFileOpt);

// --del-file / -df
var delFileOpt = new Option<bool>(["--del-file", "-df"], "Delete the active file from disk");
root.AddOption(delFileOpt);

// --move-file / -mf
var moveFileOpt = new Option<string?>(["--move-file", "-mf"], "Move the active file to a new path");
root.AddOption(moveFileOpt);

// --del-line / -dl
var delLineOpt = new Option<int?>(["--del-line", "-dl"], "Delete line at 1-based index n")
{
    ArgumentHelpName = "n"
};
root.AddOption(delLineOpt);

// --add-line / -al
var addLineContentOpt = new Option<string?>(["--add-line", "-al"], "Append a line at the end")
{
    ArgumentHelpName = "content"
};
root.AddOption(addLineContentOpt);

// --insert-line / -il  n content
var insertLineOpt = new Option<string[]>(["--insert-line", "-il"], "Insert a new line before line n")
{
    Arity = new ArgumentArity(2, 2),
    ArgumentHelpName = "n content",
    AllowMultipleArgumentsPerToken = true
};
root.AddOption(insertLineOpt);

// --replace-line / -rl  n content
var replaceLineOpt = new Option<string[]>(["--replace-line", "-rl"], "Replace line n with content")
{
    Arity = new ArgumentArity(2, 2),
    ArgumentHelpName = "n content",
    AllowMultipleArgumentsPerToken = true
};
root.AddOption(replaceLineOpt);

root.SetHandler((InvocationContext ctx) =>
{
    var help          = ctx.ParseResult.GetValueForOption(helpOpt);
    var setFile       = ctx.ParseResult.GetValueForOption(setFileOpt);
    var delFile       = ctx.ParseResult.GetValueForOption(delFileOpt);
    var moveFile      = ctx.ParseResult.GetValueForOption(moveFileOpt);
    var delLine       = ctx.ParseResult.GetValueForOption(delLineOpt);
    var addLine       = ctx.ParseResult.GetValueForOption(addLineContentOpt);
    var insertArgs    = ctx.ParseResult.GetValueForOption(insertLineOpt);
    var replaceArgs   = ctx.ParseResult.GetValueForOption(replaceLineOpt);

    // --help
    if (help)
    {
        PrintHelp();
        return;
    }

    // --set-file
    if (setFile != null)
    {
        var abs = Path.GetFullPath(setFile);
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
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
            Console.Error.WriteLine($"Error: line {delLine.Value} out of range (1–{lines.Count})");
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
        lines.Add(addLine);
        WriteLines(path, lines);
        Console.WriteLine($"Added line {lines.Count}");
        return;
    }

    // --insert-line
    if (insertArgs is { Length: 2 })
    {
        if (!int.TryParse(insertArgs[0], out int n))
        {
            Console.Error.WriteLine($"Error: '{insertArgs[0]}' is not a valid line number");
            ctx.ExitCode = 1;
            return;
        }
        RequireFile(out var path);
        var lines = ReadLines(path);
        int idx = n - 1;
        if (idx < 0 || idx > lines.Count)
        {
            Console.Error.WriteLine($"Error: line {n} out of range (1–{lines.Count + 1})");
            ctx.ExitCode = 1;
            return;
        }
        lines.Insert(idx, insertArgs[1]);
        WriteLines(path, lines);
        Console.WriteLine($"Inserted line at {n}");
        return;
    }

    // --replace-line
    if (replaceArgs is { Length: 2 })
    {
        if (!int.TryParse(replaceArgs[0], out int n))
        {
            Console.Error.WriteLine($"Error: '{replaceArgs[0]}' is not a valid line number");
            ctx.ExitCode = 1;
            return;
        }
        RequireFile(out var path);
        var lines = ReadLines(path);
        int idx = n - 1;
        if (idx < 0 || idx >= lines.Count)
        {
            Console.Error.WriteLine($"Error: line {n} out of range (1–{lines.Count})");
            ctx.ExitCode = 1;
            return;
        }
        lines[idx] = replaceArgs[1];
        WriteLines(path, lines);
        Console.WriteLine($"Replaced line {n}");
        return;
    }

    // No option given — show current file
    var current = ReadCurrentFile();
    Console.WriteLine(current != null ? $"Active file: {current}" : "No active file set.");
});

return await root.InvokeAsync(args);

static void PrintHelp()
{
    Console.WriteLine("""
        fe — file edit helper

        USAGE
          fe [options]

        OPTIONS
          -sf, --set-file <path>      Set the active file for subsequent commands
          -df, --del-file             Delete the active file from disk
          -mf, --move-file <path>     Move the active file to a new path

          -dl, --del-line <n>         Delete line n
          -al, --add-line <content>   Append a line at the end
          -il, --insert-line <n> <content>   Insert a new line before line n
          -rl, --replace-line <n> <content>  Replace line n with content

          -h,  --help                 Show this help

        TYPICAL WORKFLOW
          fe -sf src/Foo.cs               set active file
          fe                              show active file path

          fe -al "using System;"          append line at end
          fe -il 3 "// comment"           insert before line 3
          fe -rl 5 "public void Foo()"    replace line 5
          fe -dl 7                        delete line 7

          fe -mf src/Bar.cs               rename/move active file
          fe -df                          delete active file

        NOTES
          n — line number (1-based)
          Active file stored in <project>/.rag/fe_current
        """);
}

static List<string> ReadLines(string path) =>
    File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();

static void WriteLines(string path, List<string> lines)
{
    var dir = Path.GetDirectoryName(path);
    if (dir != null) Directory.CreateDirectory(dir);
    File.WriteAllLines(path, lines);
}
