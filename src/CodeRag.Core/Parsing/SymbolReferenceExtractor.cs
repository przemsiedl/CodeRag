using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeRag.Core.Parsing;

// <summary>
// Extracts symbol references from a C# source file as CodeChunk objects (Kind = Reference).
// Each chunk represents one unique referenced symbol — SourceText lists every line
// in this file that contains a reference to it.
// Uses syntax-only analysis (no SemanticModel).
// </summary>
public static class SymbolReferenceExtractor
{
    public static IReadOnlyList<CodeChunk> Extract(string sourceText, string relativePath)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();
        var sourceLines = sourceText.Split('\n');
        var walker = new ReferenceWalker(relativePath, sourceLines);
        walker.Visit(root);
        return walker.BuildChunks();
    }

    private sealed class ReferenceWalker : CSharpSyntaxWalker
    {
        private readonly string _relativePath;
        private readonly string[] _sourceLines;
        private readonly Stack<(string chunkId, string symbolName)> _scope = new();

        // toSymbol → list of (refKind, lineNumber 1-based)
        private readonly Dictionary<string, List<(string kind, int line)>> _refs = new();

        public ReferenceWalker(string relativePath, string[] sourceLines)
        {
            _relativePath = relativePath;
            _sourceLines = sourceLines;
        }

        private string? CurrentNamespace(SyntaxNode node) =>
            node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .LastOrDefault()?.Name.ToString();

        private string BuildFqn(string name, string? ns, string? parent) =>
            ns != null ? $"{ns}.{(parent != null ? parent + "." : "")}{name}" : name;

        private string MakeChunkId(SymbolKind symbolKind, string fqn) =>
            ChunkHasher.ComputeId(_relativePath, ChunkKind.Symbol, fqn, symbolKind);

        private void AddRef(string toSymbol, string kind, SyntaxNode node)
        {
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (!_refs.TryGetValue(toSymbol, out var list))
                _refs[toSymbol] = list = [];
            list.Add((kind, line));
        }

        private void VisitTypeDeclaration(SyntaxNode node, string name, SymbolKind kind,
            BaseListSyntax? baseList, Action visitChildren)
        {
            var ns = CurrentNamespace(node);
            var parent = _scope.Count > 0 ? _scope.Peek().symbolName : null;
            var fqn = BuildFqn(name, ns, parent);
            var chunkId = MakeChunkId(kind, fqn);

            if (baseList != null)
            {
                foreach (var baseType in baseList.Types)
                {
                    var typeName = baseType.Type.ToString().Split('<')[0].Trim();
                    bool isInterface = typeName.StartsWith("I") && typeName.Length > 1 && char.IsUpper(typeName[1]);
                    AddRef(typeName, isInterface ? "Implements" : "Inherits", baseType);
                }
            }

            _scope.Push((chunkId, name));
            visitChildren();
            _scope.Pop();
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
            => VisitTypeDeclaration(node, node.Identifier.Text, SymbolKind.Class,
                node.BaseList, () => base.VisitClassDeclaration(node));

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
            => VisitTypeDeclaration(node, node.Identifier.Text, SymbolKind.Record,
                node.BaseList, () => base.VisitRecordDeclaration(node));

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
            => VisitTypeDeclaration(node, node.Identifier.Text, SymbolKind.Interface,
                node.BaseList, () => base.VisitInterfaceDeclaration(node));

        public override void VisitStructDeclaration(StructDeclarationSyntax node)
            => VisitTypeDeclaration(node, node.Identifier.Text, SymbolKind.Class,
                node.BaseList, () => base.VisitStructDeclaration(node));

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (_scope.Count == 0) { base.VisitMethodDeclaration(node); return; }

            var returnType = node.ReturnType.ToString().Split('<')[0].Trim();
            if (!IsBuiltin(returnType))
                AddRef(returnType, "UsesType", node.ReturnType);

            foreach (var param in node.ParameterList.Parameters)
            {
                var typeName = param.Type?.ToString().Split('<')[0].Trim();
                if (typeName != null && !IsBuiltin(typeName))
                    AddRef(typeName, "UsesType", param);
            }

            if (node.Body != null)
            {
                foreach (var inv in node.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var calledName = ExtractCalledName(inv.Expression);
                    if (calledName != null)
                        AddRef(calledName, "Calls", inv);
                }
            }

            base.VisitMethodDeclaration(node);
        }

        public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
        {
            if (_scope.Count == 0) { base.VisitConstructorDeclaration(node); return; }

            if (node.Body != null)
            {
                foreach (var inv in node.Body.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var calledName = ExtractCalledName(inv.Expression);
                    if (calledName != null)
                        AddRef(calledName, "Calls", inv);
                }
            }

            base.VisitConstructorDeclaration(node);
        }

        private static string? ExtractCalledName(ExpressionSyntax expr) => expr switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };

        private static readonly HashSet<string> _builtins = new(StringComparer.OrdinalIgnoreCase)
        {
            "string", "int", "long", "bool", "double", "float", "decimal", "byte",
            "short", "uint", "ulong", "ushort", "sbyte", "char", "object", "dynamic",
            "var", "void", "Task", "ValueTask", "CancellationToken", "DateTime",
            "DateTimeOffset", "Guid", "TimeSpan", "IEnumerable", "IList",
            "IReadOnlyList", "ICollection", "IReadOnlyCollection", "IDictionary",
            "IReadOnlyDictionary", "List", "Dictionary", "HashSet", "Array"
        };

        private static bool IsBuiltin(string typeName) => _builtins.Contains(typeName);

        public IReadOnlyList<CodeChunk> BuildChunks()
        {
            var chunks = new List<CodeChunk>();
            foreach (var (toSymbol, refs) in _refs)
            {
                var kinds = string.Join(", ", refs.Select(r => r.kind).Distinct());
                var signature = $"{toSymbol} ({kinds})";

                // Build source text: file header, then each reference line with its content
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(_relativePath);
                foreach (var (_, line) in refs.OrderBy(r => r.line))
                {
                    var lineContent = line >= 1 && line <= _sourceLines.Length
                        ? _sourceLines[line - 1].TrimEnd()
                        : string.Empty;
                    sb.AppendLine($"  {line}: {lineContent}");
                }

                var sourceText = sb.ToString().TrimEnd();
                var id = ChunkHasher.ComputeContentHash($"{_relativePath}|ref|{toSymbol}");

                chunks.Add(new CodeChunk
                {
                    Id = id,
                    RelativePath = _relativePath,
                    SymbolName = toSymbol,
                    Kind = ChunkKind.SymbolUsage,
                    Modifiers = kinds,
                    Signature = signature,
                    SourceText = sourceText,
                    ContentHash = ChunkHasher.ComputeContentHash(sourceText),
                });
            }
            return chunks;
        }
    }
}
