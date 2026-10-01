using System.Collections.Immutable;
using System.Composition;
using FlowMate.Formatting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Text;

namespace FlowMate.Formatting.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(BlankLineBeforeReturnCodeFixProvider)), Shared]
public sealed class BlankLineBeforeReturnCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [BlankLineBeforeReturnAnalyzer.DiagnosticId, BlankLineBeforeReturnAnalyzer.BlankLineBeforeControlStatementDiagnosticId];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];
        var action = diagnostic.Properties[BlankLineBeforeReturnAnalyzer.ActionProperty];
        var isControlStatementDiagnostic = diagnostic.Id == BlankLineBeforeReturnAnalyzer.BlankLineBeforeControlStatementDiagnosticId;
        var title = action == BlankLineBeforeReturnAnalyzer.InsertAction
            ? isControlStatementDiagnostic ? "Add blank line before control statement" : "Add blank line before return"
            : "Remove blank line before return";

        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                cancellationToken => ApplyAsync(context.Document, diagnostic, action, cancellationToken),
                equivalenceKey: action),
            diagnostic);

        return Task.CompletedTask;
    }

    private static async Task<Document> ApplyAsync(
        Document document,
        Diagnostic diagnostic,
        string? action,
        CancellationToken cancellationToken)
    {
        var source = await document.GetTextAsync(cancellationToken);
        var line = source.Lines.GetLineFromPosition(diagnostic.Location.SourceSpan.Start);

        if (action == BlankLineBeforeReturnAnalyzer.InsertAction)
        {
            var lineEnding = GetLineEnding(source, line.LineNumber);
            return document.WithText(source.WithChanges(new TextChange(new TextSpan(line.Start, 0), lineEnding)));
        }

        if (line.LineNumber == 0)
        {
            return document;
        }

        var blankLine = source.Lines[line.LineNumber - 1];
        if (!string.IsNullOrWhiteSpace(blankLine.ToString()))
        {
            return document;
        }

        return document.WithText(source.WithChanges(new TextChange(TextSpan.FromBounds(blankLine.Start, line.Start), string.Empty)));
    }

    private static string GetLineEnding(SourceText source, int lineNumber)
    {
        if (lineNumber > 0)
        {
            var previousLine = source.Lines[lineNumber - 1];
            var previousEnding = source.ToString(TextSpan.FromBounds(previousLine.End, previousLine.EndIncludingLineBreak));
            if (previousEnding.Length > 0)
            {
                return previousEnding;
            }
        }

        return source.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }
}
