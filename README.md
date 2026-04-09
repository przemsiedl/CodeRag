# CodeRag

Local, offline RAG system for C# codebases. Indexes `.cs` files into a local SQLite database with ONNX embeddings, then lets you search by meaning — not just by name.

Includes **fe**, a companion CLI for line-by-line file editing.

## Requirements

- .NET 8+ runtime
- Windows (tested on Windows 11)

## Installation

Copy the published output to a directory on your machine (e.g. `C:\Programy\Rag\`) and optionally add it to your PATH.

The `models/` folder contains ONNX embedding models and is required at runtime.

## Quick start

```bash
# Index a project
rag index ./MyProject

# Search for code by meaning
rag query ./MyProject -q "user authentication"

# Watch for changes and keep index up to date
rag watch ./MyProject
```

---

## rag

### `rag index <path>`

Indexes all `.cs` files under `<path>`. Creates a `.rag/` directory containing `index.db` (SQLite with embeddings).

```bash
rag index D:/repo/my-project
```

Re-run to update the index after code changes, or use `rag watch` for automatic updates.

### `rag watch <path>`

Watches for `.cs` file changes and updates the index incrementally.

```bash
rag watch D:/repo/my-project
```

### `rag query <path>`

Searches the indexed codebase using semantic (natural language) queries.

```bash
rag query D:/repo/my-project -q "order validation" -r 5
```

#### Options

| Flag | Short | Description | Default |
|------|-------|-------------|---------|
| `--query <text>` | `-q` | Natural-language query | _(required)_ |
| `--results <n>` | `-r` | Number of results to return | `5` |
| `--symbol-type <types>` | `-s` | Comma-separated code symbol filter: `Class`, `Record`, `Interface`, `Enum`, `Method`, `Constructor`, `Property`, `Field` | all |
| `--chunk-type <types>` | `-ct` | Comma-separated chunk kind filter: `Symbol`, `FileDocument`, `SymbolUsage` | all |
| `--in-class <name>` | `-ic` | Only symbols belonging to a class (partial match) | _(none)_ |
| `--in-file <name>` | `-if` | Only symbols from files matching name/path (partial match) | _(none)_ |
| `--in-namespace <name>` | `-in` | Only symbols from namespaces matching the given name (partial match) | _(none)_ |
| `--file-name <name>` | `-fn` | Find files by name (partial match). Returns File-level chunks only | _(none)_ |
| `--full` | `-f` | Include full source text (default: signatures only) | off |
| `--context <n>` | `-c` | Show N lines of context around the symbol (reads from source file). Overrides `--full` | `0` |
| `--grep <pattern>` | `-g` | Filter displayed source lines to those matching the given regex. Only affects `-f`/`-c` output | _(none)_ |
| `--lines <from-to>` | `-lr` | Show only lines in the given range, e.g. `5-10` (absolute line numbers). Only affects `-f`/`-c` output | _(none)_ |

#### Examples

```bash
# Signatures only (fast overview)
rag query . -q "send email notification" -r 5

# Full source code
rag query . -q "authentication" -r 3 -f

# Only methods inside a specific class
rag query . -q "calculate total" -ic OrderService

# Only symbols from a specific file
rag query . -q "connection string" -if appsettings

# Find files by name
rag query . -fn ".csproj"

# Show 5 lines of context around each result
rag query . -q "send email" -c 5

# Show full source, filter lines matching a regex
rag query . -q "order total" -f -g "return"

# Show full source, but only lines 20-40
rag query . -q "authentication" -f -lr 20-40

# Find all usages of a symbol (SymbolUsage chunks)
rag query . -q "OrderService" -ct SymbolUsage -r 10

# Show source lines where a symbol is referenced
rag query . -q "ValidateOrder" -ct SymbolUsage -f

# Only symbols from a specific namespace
rag query . -q "authentication" -in MyApp.Services
```

#### Output format

Without `-f` (signatures):

```
[1] Class        AuthController
     namespace : DTM.SMT.Api.Controllers
     class     : -
     file      : DTM.SMT.Api/Controllers/AuthController.cs:6-29
     signature : public class AuthController: ControllerBase
```

With `-f` (full source):

```
[1] Class        AuthController
     namespace : DTM.SMT.Api.Controllers
     class     : -
     file      : DTM.SMT.Api/Controllers/AuthController.cs:6-29
     signature : public class AuthController: ControllerBase
     source    :
      6 | [ApiController]
      7 | [Route(AuthRoutes.Base)]
      8 | public class AuthController : ControllerBase
      9 | {
     ...
```

SymbolUsage chunks (`-ct SymbolUsage`) group all usages of a symbol by file. Each result shows one file that references the symbol, with the actual lines of code:

```
[1] SymbolUsage  ValidateOrder (Calls)
     signature : ValidateOrder (Calls)
     source    :
     Services/OrderService.cs
       42: var result = ValidateOrder(order);
       87: if (!ValidateOrder(updatedOrder)) throw ...
```

#### Concurrency

`rag query` automatically waits if `rag index` or `rag watch` is currently writing to the index. No manual coordination is needed.

### `rag status <path>`

Shows index statistics.

```bash
rag status D:/repo/my-project
```

```
Chunks:       2146
DB size:      9760,0 KB
Last indexed: 2026-04-09 08:18:06 UTC
DB path:      D:\repo\my-project\.rag\index.db
Models:       C:\Programy\Rag\models
```

---

## fe

Companion CLI for line-by-line file editing. Operates on an "active file" that persists between calls.

```bash
# Set active file
fe -sf src/Foo.cs

# Show current active file
fe

# Insert before line 3
fe -il 3 "using System.Linq;"

# Replace line 10
fe -rl 10 "    return result;"

# Delete line 15
fe -dl 15

# Append at end
fe -al "// EOF"

# Move/rename the active file
fe -mf src/Bar.cs

# Delete the active file from disk
fe -df
```

### Options

| Flag | Short | Description |
|------|-------|-------------|
| `--set-file <path>` | `-sf` | Set the active file |
| `--del-file` | `-df` | Delete the active file from disk |
| `--move-file <path>` | `-mf` | Move/rename the active file |
| `--del-line <n>` | `-dl` | Delete line _n_ |
| `--add-line <content>` | `-al` | Append a line at the end |
| `--insert-line <n> <content>` | `-il` | Insert a new line before line _n_ |
| `--replace-line <n> <content>` | `-rl` | Replace line _n_ with content |

Line numbers are **1-based**. After inserting or deleting, subsequent line numbers shift — when making multiple edits, work from bottom to top.

The active file path is stored in `<project>/.rag/fe_current`.

---

## Claude Code integration

Both tools integrate with [Claude Code](https://claude.ai/code) as custom skills. Skill definitions go in `~/.claude/skills/`:

```
~/.claude/skills/
  rag-query/
    SKILL.md      # Skill definition for rag query
  fe/
    SKILL.md      # Skill definition for fe
```

This lets Claude Code invoke `rag` and `fe` automatically during coding sessions — using semantic search to find relevant code and line-based editing when needed.

## Data storage

All index data is stored locally in `<project>/.rag/index.db`. No data leaves your machine. Add `.rag/` to your `.gitignore`.

## License

MIT
