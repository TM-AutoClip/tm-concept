using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TMConcept.Analyzers;

/// <summary>
/// One top-level type per file (class, record, interface, enum, struct, delegate; any accessibility) and the file is named after it.
/// Parts of the same partial type may live together; a part in another file is named <c>Type.Part.cs</c>. Generic types with the same
/// name use <c>Name{T}.cs</c>. Nested types and <c>file</c>-local types do not count; in a top-level-statements file only
/// <c>partial class Program</c> is allowed. Generated code (XAML) is not analyzed.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OneTypePerFileAnalyzer : DiagnosticAnalyzer
{
    /// <summary>A second (or later) top-level type in a file.</summary>
    public static readonly DiagnosticDescriptor OneType = new(
        id: "TMC0001",
        title: "One top-level type per file",
        messageFormat: "'{0}' is the second top-level type in this file; move it to its own file ({0}.cs)",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Every top-level type lives in its own file, so the file name tells where a type is.");

    /// <summary>The file is not named after its type.</summary>
    public static readonly DiagnosticDescriptor FileName = new(
        id: "TMC0002",
        title: "File name must match the type name",
        messageFormat: "File name '{0}' does not match its type '{1}'; rename the file to '{1}.cs' (a partial part: '{1}.<Part>.cs')",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The part of the file name before the first dot (before the brace for generic types) is the name of its type.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(OneType, FileName);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(Analyze);
    }

    private static void Analyze(SyntaxTreeAnalysisContext context)
    {
        if (context.Tree.GetRoot(context.CancellationToken) is not CompilationUnitSyntax root)
        {
            return;
        }

        var types = new List<MemberDeclarationSyntax>();
        Collect(root.Members, types);
        if (types.Count == 0)
        {
            return;
        }

        // A top-level-statements file belongs to the compiler's Program class; elsewhere the first type owns the file.
        var topLevel = root.Members.Any(m => m is GlobalStatementSyntax);
        var owner = topLevel ? "`Program`0" : Key(types[0]);
        var single = true;
        foreach (var type in types)
        {
            if (Key(type) != owner)
            {
                single = false;
                context.ReportDiagnostic(Diagnostic.Create(OneType, Identifier(type).GetLocation(), Identifier(type).Text));
            }
        }

        var fileName = Path.GetFileName(context.Tree.FilePath);
        if (!topLevel && single && fileName.Length > 0 && BaseName(fileName) != Identifier(types[0]).Text)
        {
            context.ReportDiagnostic(Diagnostic.Create(FileName, Identifier(types[0]).GetLocation(), fileName, Identifier(types[0]).Text));
        }
    }

    private static void Collect(SyntaxList<MemberDeclarationSyntax> members, List<MemberDeclarationSyntax> types)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    Collect(ns.Members, types);
                    break;
                case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax when !member.Modifiers.Any(SyntaxKind.FileKeyword):
                    types.Add(member);
                    break;
            }
        }
    }

    private static SyntaxToken Identifier(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax type => type.Identifier,
        DelegateDeclarationSyntax del => del.Identifier,
        _ => default,
    };

    private static string Key(MemberDeclarationSyntax member)
    {
        var arity = member switch
        {
            TypeDeclarationSyntax type => type.TypeParameterList?.Parameters.Count ?? 0,
            DelegateDeclarationSyntax del => del.TypeParameterList?.Parameters.Count ?? 0,
            _ => 0,
        };
        return Namespace(member) + "`" + Identifier(member).Text + "`" + arity.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Namespace(MemberDeclarationSyntax member) =>
        string.Join(".", member.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString()));

    private static string BaseName(string fileName)
    {
        var dot = fileName.IndexOf('.');
        var name = dot >= 0 ? fileName.Substring(0, dot) : fileName;
        var brace = name.IndexOf('{');
        return brace >= 0 ? name.Substring(0, brace) : name;
    }
}
