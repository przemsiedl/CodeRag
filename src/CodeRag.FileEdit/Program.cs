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

// --del-line / -dl  (supports single line "5" or range "5-10")
var delLineOpt = new Option<string?>(["--del-line", "-dl"], "Delete line n or range n-m (e.g. 5 or 5-10)")
{
    ArgumentHelpName = "n|n-m"
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

// --replace-line / -rl  n content  or  n-m content
var replaceLineOpt = new Option<string[]>(["--replace-line", "-rl"], "Replace line n (or range n-m) with content")
{
    Arity = new ArgumentArity(2, 2),
    ArgumentHelpName = "n|n-m content",
    AllowMultipleArgumentsPerToken = true
};
root.AddOption(replaceLineOpt);

// --show / -sh  (optional range)
var showOpt = new Option<string?>(["--show", "-sh"], "Show active file contents, optionally with line range (e.g. 5-15)")
{
    ArgumentHelpName = "n-m",
    Arity = ArgumentArity.ZeroOrOne
};
root.AddOption(showOpt);

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
    var show          = ctx.ParseResult.GetValueForOption(showOpt);

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

    // --del-line (single or range)
    if (delLine != null)
    {
        RequireFile(out var path);
        var lines = ReadLines(path);
        if (!TryParseRange(delLine, out int from, out int to))
        {
            Console.Error.WriteLine($"Error: '{delLine}' is not a valid line number or range (e.g. 5 or 5-10)");
            ctx.ExitCode = 1;
            return;
        }
        if (from < 1 || to > lines.Count || from > to)
        {
            Console.Error.WriteLine($"Error: range {from}-{to} out of range (1–{lines.Count})");
            ctx.ExitCode = 1;
            return;
        }
        lines.RemoveRange(from - 1, to - from + 1);
        WriteLines(path, lines);
        Console.WriteLine(from == to ? $"Deleted line {from}" : $"Deleted lines {from}-{to}");
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

    // --replace-line (single or range)
    if (replaceArgs is { Length: 2 })
    {
        if (!TryParseRange(replaceArgs[0], out int from, out int to))
        {
            Console.Error.WriteLine($"Error: '{replaceArgs[0]}' is not a valid line number or range");
            ctx.ExitCode = 1;
            return;
        }
        RequireFile(out var path);
        var lines = ReadLines(path);
        if (from < 1 || to > lines.Count || from > to)
        {
            Console.Error.WriteLine($"Error: range {from}-{to} out of range (1–{lines.Count})");
            ctx.ExitCode = 1;
            return;
        }
        // Replace range with single line of content
        lines.RemoveRange(from - 1, to - from + 1);
        lines.Insert(from - 1, replaceArgs[1]);
        WriteLines(path, lines);
        Console.WriteLine(from == to ? $"Replaced line {from}" : $"Replaced lines {from}-{to}");
        return;
    }

    // --show
    if (ctx.ParseResult.FindResultFor(showOpt) is not null)
    {
        RequireFile(out var path);
        var lines = ReadLines(path);
        int from = 1, to = lines.Count;
        if (show != null)
        {
            if (!TryParseRange(show, out from, out to))
            {
                Console.Error.WriteLine($"Error: '{show}' is not a valid line range (e.g. 5 or 5-15)");
                ctx.ExitCode = 1;
                return;
            }
            from = Math.Max(1, from);
            to = Math.Min(lines.Count, to);
        }
        var width = to.ToString().Length;
        for (int li = from; li <= to; li++)
            Console.WriteLine($"{li.ToString().PadLeft(width)} | {lines[li - 1]}");
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

          -dl, --del-line <n|n-m>     Delete line n or range n-m
          -al, --add-line <content>   Append a line at the end
          -il, --insert-line <n> <content>   Insert a new line before line n
          -rl, --replace-line <n|n-m> <content>  Replace line(s) with content
          -sh, --show [n-m]           Show file contents (optionally a line range)

          -h,  --help                 Show this help

        TYPICAL WORKFLOW
          fe -sf src/Foo.cs               set active file
          fe -sh                          show file contents
          fe -sh 10-20                    show lines 10-20

          fe -al "using System;"          append line at end
          fe -il 3 "// comment"           insert before line 3
          fe -rl 5 "public void Foo()"    replace line 5
          fe -rl 5-10 "// collapsed"      replace lines 5-10 with one line
          fe -dl 7                        delete line 7
          fe -dl 7-12                     delete lines 7-12

          fe -mf src/Bar.cs               rename/move active file
          fe -df                          delete active file

        NOTES
          n — line number (1-based), n-m — inclusive range
          Active file stored in <project>/.rag/fe_current
        """);
}

static bool TryParseRange(string input, out int from, out int to)
{
    var dash = input.IndexOf('-');
    if (dash < 0)
    {
        to = 0;
        if (int.TryParse(input, out from)) { to = from; return true; }
        return false;
    }
    from = 0; to = 0;
    return int.TryParse(input.AsSpan(0, dash), out from)
        && int.TryParse(input.AsSpan(dash + 1), out to);
}

static List<string> ReadLines(string path) =>
    File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();

static void WriteLines(string path, List<string> lines)
{
    var dir = Path.GetDirectoryName(path);
    if (dir != null) Directory.CreateDirectory(dir);
    File.WriteAllLines(path, lines);
}
