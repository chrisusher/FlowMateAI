using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Shared.Enums;

namespace FlowMate.Formatting;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BlankLineBeforeReturnAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "FM0001";
    public const string BlankLineBeforeControlStatementDiagnosticId = "FM0002";

    public const string ActionProperty = "Action";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Blank line before return",
        "{0}",
        "Formatting",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor BlankLineBeforeControlStatementRule = new(
        BlankLineBeforeControlStatementDiagnosticId,
        "Blank line before control statement",
        "{0}",
        "Formatting",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule, BlankLineBeforeControlStatementRule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeReturn, SyntaxKind.ReturnStatement);
        context.RegisterSyntaxNodeAction(
            AnalyzeControlStatement,
            SyntaxKind.IfStatement,
            SyntaxKind.TryStatement,
            SyntaxKind.WhileStatement,
            SyntaxKind.ForStatement,
            SyntaxKind.ForEachStatement,
            SyntaxKind.ForEachVariableStatement);
    }

    private static void AnalyzeControlStatement(SyntaxNodeAnalysisContext context)
    {
        var statement = (StatementSyntax)context.Node;
        var keyword = statement.GetFirstToken();
        var text = statement.SyntaxTree.GetText(context.CancellationToken);
        var line = text.Lines.GetLineFromPosition(keyword.SpanStart);
        var beforeKeywordOnLine = text.ToString(TextSpan.FromBounds(line.Start, keyword.SpanStart));

        if (!string.IsNullOrWhiteSpace(beforeKeywordOnLine) || !HasPreviousStatement(statement))
        {
            return;
        }

        var hasBlankLineBefore = line.LineNumber > 0
            && string.IsNullOrWhiteSpace(text.Lines[line.LineNumber - 1].ToString());

        if (!hasBlankLineBefore)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                BlankLineBeforeControlStatementRule,
                keyword.GetLocation(),
                ImmutableDictionary<string, string?>.Empty.Add(ActionProperty, BlankLineAction.Insert.ToString()),
                "Add a blank line before this control statement."));
        }
    }

    private static void AnalyzeReturn(SyntaxNodeAnalysisContext context)
    {
        var statement = (ReturnStatementSyntax)context.Node;
        var text = statement.SyntaxTree.GetText(context.CancellationToken);
        var line = text.Lines.GetLineFromPosition(statement.ReturnKeyword.SpanStart);
        var beforeReturnOnLine = text.ToString(TextSpan.FromBounds(line.Start, statement.ReturnKeyword.SpanStart));

        if (!string.IsNullOrWhiteSpace(beforeReturnOnLine))
        {
            return;
        }

        var hasBlankLineBefore = line.LineNumber > 0
            && string.IsNullOrWhiteSpace(text.Lines[line.LineNumber - 1].ToString());
        var isInsideSwitchSection = IsInsideSwitchSection(statement);

        if (isInsideSwitchSection && hasBlankLineBefore)
        {
            Report(context, statement, BlankLineAction.Remove, "Remove the blank line before this return in a switch statement.");
            return;
        }

        if (!isInsideSwitchSection && !hasBlankLineBefore && HasPreviousStatement(statement))
        {
            Report(context, statement, BlankLineAction.Insert, "Add a blank line before this return.");
        }
    }

    private static bool IsInsideSwitchSection(ReturnStatementSyntax statement)
    {
        foreach (var ancestor in statement.Ancestors())
        {
            if (ancestor is LocalFunctionStatementSyntax
                or AnonymousFunctionExpressionSyntax
                or AnonymousMethodExpressionSyntax
                or BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax)
            {
                return false;
            }

            if (ancestor is SwitchSectionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPreviousStatement(ReturnStatementSyntax statement)
    {
        if (statement.Parent is BlockSyntax block)
        {
            return block.Statements.IndexOf(statement) > 0;
        }

        return false;
    }

    private static bool HasPreviousStatement(StatementSyntax statement)
    {
        if (statement.Parent is BlockSyntax block)
        {
            return block.Statements.IndexOf(statement) > 0;
        }

        if (statement.Parent is SwitchSectionSyntax switchSection)
        {
            return switchSection.Statements.IndexOf(statement) > 0;
        }

        return false;
    }

    private static void Report(SyntaxNodeAnalysisContext context, ReturnStatementSyntax statement, BlankLineAction action, string message)
    {
        var properties = ImmutableDictionary<string, string?>.Empty.Add(ActionProperty, action.ToString());
        context.ReportDiagnostic(Diagnostic.Create(Rule, statement.ReturnKeyword.GetLocation(), properties, message));
    }
}
