#nullable enable
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Coclico.Services.Algorithms;

/// <summary>
/// Replaces a specific method in a C# source file using Roslyn AST rewriting.
/// Preserves formatting, comments, and trivia.
/// </summary>
public sealed class MethodReplacer : CSharpSyntaxRewriter
{
    private readonly string _targetMethodName;
    private readonly string _targetClassName;
    private readonly MethodDeclarationSyntax _replacement;

    public bool DidReplace { get; private set; }

    private MethodReplacer(string className, string methodName, MethodDeclarationSyntax replacement)
    {
        _targetClassName = className;
        _targetMethodName = methodName;
        _replacement = replacement;
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.Identifier.Text != _targetMethodName)
            return base.VisitMethodDeclaration(node);

        var parent = node.Parent;
        while (parent is not null)
        {
            if (parent is ClassDeclarationSyntax cls && cls.Identifier.Text == _targetClassName)
            {
                DidReplace = true;
                return _replacement.WithLeadingTrivia(node.GetLeadingTrivia())
                                   .WithTrailingTrivia(node.GetTrailingTrivia());
            }
            parent = parent.Parent;
        }

        return base.VisitMethodDeclaration(node);
    }

    /// <summary>
    /// Replace a method in sourceCode. Returns null if method not found.
    /// </summary>
    public static string? ReplaceMethod(
        string sourceCode,
        string className,
        string methodName,
        string newMethodSource)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetRoot();

        var tempTree = CSharpSyntaxTree.ParseText($"class _Temp_ {{ {newMethodSource} }}");
        var tempRoot = tempTree.GetRoot();
        var newMethod = tempRoot.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (newMethod is null) return null;

        var rewriter = new MethodReplacer(className, methodName, newMethod);
        var newRoot = rewriter.Visit(root);

        return rewriter.DidReplace ? newRoot.ToFullString() : null;
    }
}
