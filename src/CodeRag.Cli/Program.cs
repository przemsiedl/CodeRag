using System.CommandLine;
using CodeRag.Cli.Commands;

var root = new RootCommand("rag — local code-aware RAG system");
root.AddCommand(IndexCommand.Build());
root.AddCommand(WatchCommand.Build());
root.AddCommand(QueryCommand.Build());
root.AddCommand(StatusCommand.Build());

return await root.InvokeAsync(args);
