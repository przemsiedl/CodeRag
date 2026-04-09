using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeRag.Core.Parsing;

public sealed class CSharpSyntaxExtractor : IFileExtractor
{
    public bool CanHandle(string extension) =>
        extension.Equals(".cs", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<CodeChunk> Extract(string sourceText, string relativePath)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();

        var walker = new ChunkWalker(relativePath, tree);
        walker.Visit(root);

        var chunks = new List<CodeChunk>(walker.Chunks.Count + 1);

        // File-level chunk: full source, line 1 to last
        var fileLines = sourceText.Split('\n');
        var fileName = Path.GetFileName(relativePath);
        chunks.Add(new CodeChunk
        {
            Id = ChunkHasher.ComputeId(relativePath, SymbolKind.File, relativePath),
            RelativePath = relativePath,
            SymbolName = fileName,
            Kind = SymbolKind.File,
            Modifiers = string.Empty,
            Signature = relativePath,
            SourceText = sourceText.TrimEnd(),
            ContentHash = ChunkHasher.ComputeContentHash(sourceText),
            StartLine = 1,
            EndLine = fileLines.Length
        });

        chunks.AddRange(walker.Chunks);
        return chunks;
    }

    private sealed class ChunkWalker : CSharpSyntaxWalker
    {
        private readonly string _relativePath;
        private readonly SyntaxTree _tree;
        private readonly Stack<string> _classStack = new();
        public readonly List<CodeChunk> Chunks = new();

        public ChunkWalker(string relativePath, SyntaxTree tree)
        {
            _relativePath = relativePath;
            _tree = tree;
        }

        private string? CurrentNamespace(SyntaxNode node)
        {
            var ns = node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .LastOrDefault();
            return ns?.Name.ToString();
        }

        private (int start, int end) GetLines(SyntaxNode node)
        {
            var span = _tree.GetLineSpan(node.Span);
            return (span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1);
        }

        // ── Class ────────────────────────────────────────────────────────────

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var name = node.Identifier.Text;
            var ns = CurrentNamespace(node);
            var parent = _classStack.Count > 0 ? _classStack.Peek() : null;
            var fqn = ns != null ? $"{ns}.{name}" : name;
            var modifiers = node.Modifiers.ToString();
            var (start, end) = GetLines(node);

            var fullText = node.ToFullString().TrimEnd();
            AddChunks(fullText, SymbolKind.Class, fqn, ns, parent, name, modifiers,
                signature: $"{modifiers} class {name}{node.TypeParameterList}{node.BaseList}".Trim(),
                start, end);

            _classStack.Push(name);
            base.VisitClassDeclaration(node);
            _classStack.Pop();
        }

        // ── Record ───────────────────────────────────────────────────────────

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
        {
            var name = node.Identifier.Text;
            var ns = CurrentNamespace(node);
            var parent = _classStack.Count > 0 ? _classStack.Peek() : null;
            var fqn = ns != null ? $"{ns}.{name}" : name;
            var modifiers = node.Modifiers.ToString();
            var (start, end) = GetLines(node);

            var fullText = node.ToFullString().TrimEnd();
            AddChunks(fullText, SymbolKind.Record, fqn, ns, parent, name, modifiers,
                signature: $"{modifiers} record {node.ClassOrStructKeyword} {name}{node.TypeParameterList}{node.ParameterList}".Trim(),
                start, end);

            _classStack.Push(name);
            base.VisitRecordDeclaration(node);
            _classStack.Pop();
        }

        // ── Interface ────────────────────────────────────────────────────────

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            var name = node.Identifier.Text;
            var ns = CurrentNamespace(node);
            var parent = _classStack.Count > 0 ? _classStack.Peek() : null;
            var fqn = ns != null ? $"{ns}.{name}" : name;
            var modifiers = node.Modifiers.ToString();
            var (start, end) = GetLines(node);

            var fullText = node.ToFullString().TrimEnd();
            AddChunks(fullText, SymbolKind.Interface, fqn, ns, parent, name, modifiers,
                signature: $"{modifiers} interface {name}{node.TypeParameterList}{node.BaseList}".Trim(),
                start, end);

            _classStack.Push(name);
            base.VisitInterfaceDeclaration(node);
            _classStack.Pop();
        }

        // ── Enum ─────────────────────────────────────────────────────────────

        public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
        {
            var name = node.Identifier.Text;
            var ns = CurrentNamespace(node);
            var parent = _classStack.Count > 0 ? _classStack.Peek() : null;
            var fqn = ns != null ? $"{ns}.{name}" : name;
            var modifiers = node.Modifiers.ToString();
            var (start, end) = GetLines(node);

            var fullText = node.ToFullString().TrimEnd();
            AddChunks(fullText, SymbolKind.Enum, fqn, ns, parent, name, modifiers,
                signature: $"{modifiers} enum {name}".Trim(),
                start, end);

            base.VisitEnumDeclaration(node);
        }

        // ── Method ───────────────────────────────────────────────────────────

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var name = node.Identifier.Text;
            var ns = CurrentNamespace(node);
            var parent = _classStack.Count > 0 ? _classStack.Peek() : null;
            var fqn = ns != null ? $"{ns}.{(parent != null ? parent + "." : "")}{name}" : name;
            var modifiers = node.Modifiers.ToString();
            var signature = $"{modifiers} {node.ReturnType} {name}{node.TypeParameterList}{node.ParameterList}".Trim();
            var (start, end) = GetLines(node);

            AddChunks(node.ToFullString().TrimEnd(), SymbolKind.Method, fqn, ns, parent, name, modifiers, signature, start, end);

            base.VisitMethodDeclaration(node);
        }

        // ── Constructor ──────────────────────────────────────────────────────

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            var name = node.Identifier.Text;
            var ns = CurrentNamespace(node);
            var parent = _classStack.Count > 0 ? _classStack.Peek() : null;
            var fqn = ns != null ? $"{ns}.{(parent != null ? parent + "." : "")}.ctor" : ".ctor";
            var modifiers = node.Modifiers.ToString();
            var signature = $"{modifiers} {name}{node.ParameterList}".Trim();
            var (start, end) = GetLines(node);

            AddChunks(node.ToFullString().TrimEnd(), SymbolKind.Constructor, fqn, ns, parent, name, modifiers, signature, start, end);

            base.VisitConstructorDeclaration(node);
        }

        // ── Property ─────────────────────────────────────────────────────────

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            var name = node.Identifier.Text;
            var ns = CurrentNamespace(node);
            var parent = _classStack.Count > 0 ? _classStack.Peek() : null;
            var fqn = ns != null ? $"{ns}.{(parent != null ? parent + "." : "")}{name}" : name;
            var modifiers = node.Modifiers.ToString();
            var signature = $"{modifiers} {node.Type} {name}".Trim();
            var (start, end) = GetLines(node);

            AddChunks(node.ToFullString().TrimEnd(), SymbolKind.Property, fqn, ns, parent, name, modifiers, signature, start, end);

            base.VisitPropertyDeclaration(node);
        }

        // ── Field ────────────────────────────────────────────────────────────

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            var ns = CurrentNamespace(node);
            var parent = _classStack.Count > 0 ? _classStack.Peek() : null;
            var modifiers = node.Modifiers.ToString();
            var (start, end) = GetLines(node);

            foreach (var variable in node.Declaration.Variables)
            {
                var name = variable.Identifier.Text;
                var fqn = ns != null ? $"{ns}.{(parent != null ? parent + "." : "")}{name}" : name;
                var signature = $"{modifiers} {node.Declaration.Type} {name}".Trim();

                var chunk = MakeChunk(node.ToFullString().TrimEnd(), SymbolKind.Field,
                    fqn, ns, parent, name, modifiers, signature, start, end);
                Chunks.Add(chunk);
            }

            base.VisitFieldDeclaration(node);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private void AddChunks(string fullText, SymbolKind kind, string fqn,
            string? ns, string? parent, string name, string modifiers, string signature,
            int startLine, int endLine)
        {
            const int maxChars = 4000;
            if (fullText.Length <= maxChars)
            {
                Chunks.Add(MakeChunk(fullText, kind, fqn, ns, parent, name, modifiers, signature, startLine, endLine));
                return;
            }

            // Sliding window for large nodes
            int step = maxChars / 2;
            int i = 0, part = 0;
            while (i < fullText.Length)
            {
                var slice = fullText.Substring(i, Math.Min(maxChars, fullText.Length - i));
                Chunks.Add(MakeChunk(slice, kind, $"{fqn}__part{part}", ns, parent, name, modifiers, signature, startLine, endLine));
                i += step;
                part++;
            }
        }

        private CodeChunk MakeChunk(string text, SymbolKind kind, string fqn,
            string? ns, string? parent, string name, string modifiers, string signature,
            int startLine, int endLine)
        {
            return new CodeChunk
            {
                Id = ChunkHasher.ComputeId(_relativePath, kind, fqn),
                RelativePath = _relativePath,
                Namespace = ns,
                ParentClass = parent,
                SymbolName = name,
                Kind = kind,
                Modifiers = modifiers,
                Signature = signature,
                SourceText = text,
                ContentHash = ChunkHasher.ComputeContentHash(text),
                ContextHeaderLines = 0,
                StartLine = startLine,
                EndLine = endLine
            };
        }
    }
}
