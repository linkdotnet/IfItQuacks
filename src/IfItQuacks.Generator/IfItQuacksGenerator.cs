using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace IfItQuacks.Generator;

/// <summary>
/// Generates adapters and interceptors that let <c>[DuckTyped]</c> methods accept any type structurally matching their interface parameters.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class IfItQuacksGenerator : IIncrementalGenerator
{
    internal const string CallSitesTrackingName = "IfItQuacks.CallSites";
    internal const string MethodGroupsTrackingName = "IfItQuacks.MethodGroups";
    internal const string MappedShapesTrackingName = "IfItQuacks.MappedShapes";
    internal const string CallSiteDiagnosticsTrackingName = "IfItQuacks.CallSiteDiagnostics";
    internal const string AdaptersTrackingName = "IfItQuacks.Adapters";
    internal const string OverloadsTrackingName = "IfItQuacks.Overloads";
    internal const string InterceptorsTrackingName = "IfItQuacks.Interceptors";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource("IfItQuacksAttributes.g.cs", SourceText.From(EmbeddedSources.Attributes, Encoding.UTF8)));

        RegisterMappedShapes(context);
        var duckTypedMethods = RegisterDuckTypedMethods(context);
        RegisterCallSiteOutputs(context, SelectCallSites(context, duckTypedMethods), SelectMethodGroups(context, duckTypedMethods));
    }

    private static IncrementalValueProvider<DuckTypedMethodIndex> RegisterDuckTypedMethods(IncrementalGeneratorInitializationContext context)
    {
        var duckTypedMethods = context.SyntaxProvider.ForAttributeWithMetadataName(
            KnownSymbols.DuckTypedAttributeMetadataName,
            predicate: static (node, _) => node is MethodDeclarationSyntax,
            transform: static (attributed, _) => DuckTypedMethodAnalysis.Analyze(attributed));

        context.RegisterSourceOutput(duckTypedMethods, static (output, method) => GeneratedSourceWriter.Write(output, method));

        return duckTypedMethods
            .Where(static method => method.ValidMethod is not null)
            .Select(static (method, _) => method.ValidMethod!)
            .Collect()
            .Select(static (methods, _) => new DuckTypedMethodIndex(methods));
    }

    private static void RegisterMappedShapes(IncrementalGeneratorInitializationContext context)
    {
        var mappedShapes = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownSymbols.DuckShapeAttributeMetadataName,
                predicate: static (node, _) => node is InterfaceDeclarationSyntax,
                transform: static (attributed, _) => MappedShapeAnalysis.Analyze(attributed))
            .WithTrackingName(MappedShapesTrackingName);

        context.RegisterSourceOutput(mappedShapes, static (output, shape) => GeneratedSourceWriter.Write(output, shape));
    }

    private static IncrementalValuesProvider<CallSiteOutput?> SelectCallSites(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DuckTypedMethodIndex> duckTypedMethods) =>
        context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InvocationExpressionSyntax invocation && InvocationSyntax.GetInvokedName(invocation) is not null,
                transform: static (syntax, _) => (InvocationExpressionSyntax)syntax.Node)
            .Combine(duckTypedMethods)
            .Where(static pair => MayNeedGeneratedCode(pair.Left, pair.Right))
            .Combine(context.CompilationProvider)
            .Select(static (pair, ct) => CallSiteAnalysis.Analyze(pair.Left.Left, pair.Left.Right, pair.Right, ct))
            .WithTrackingName(CallSitesTrackingName);

    private static IncrementalValuesProvider<CallSiteOutput?> SelectMethodGroups(IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<DuckTypedMethodIndex> duckTypedMethods) =>
        context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => MethodGroupAnalysis.GetMethodGroupExpression(node) is not null,
                transform: static (syntax, _) => MethodGroupAnalysis.GetMethodGroupExpression(syntax.Node)!)
            .Combine(duckTypedMethods)
            .Where(static pair => pair.Right.ContainsName(MethodGroupAnalysis.GetName(pair.Left)))
            .Combine(context.CompilationProvider)
            .Select(static (pair, ct) => MethodGroupAnalysis.Analyze(pair.Left.Left, pair.Left.Right, pair.Right, ct))
            .WithTrackingName(MethodGroupsTrackingName);

    private static bool MayNeedGeneratedCode(InvocationExpressionSyntax invocation, DuckTypedMethodIndex duckTypedMethods) =>
        InvocationSyntax.GetInvokedNameSyntax(invocation) is { } name &&
        (InvocationSyntax.CanBeDuckConversion(name) || duckTypedMethods.ContainsName(name.Identifier.Text));

    // Each output is cached on its own, so an edit that only moves call sites regenerates the interceptors alone.
    private static void RegisterCallSiteOutputs(IncrementalGeneratorInitializationContext context,
        IncrementalValuesProvider<CallSiteOutput?> callSites, IncrementalValuesProvider<CallSiteOutput?> methodGroups)
    {
        var sites = callSites.Collect()
            .Combine(methodGroups.Collect())
            .Select(static (sites, _) => new EquatableArray<CallSiteOutput>([.. sites.Left.OfType<CallSiteOutput>(), .. sites.Right.OfType<CallSiteOutput>()]));

        context.RegisterSourceOutput(
            sites.Select(static (sites, _) => CallSiteSources.CollectDiagnostics(sites)).WithTrackingName(CallSiteDiagnosticsTrackingName),
            static (output, diagnostics) => GeneratedSourceWriter.Report(output, diagnostics));
        context.RegisterSourceOutput(
            sites.Select(static (sites, _) => CallSiteSources.CreateAdaptersFile(sites)).WithTrackingName(AdaptersTrackingName),
            static (output, files) => GeneratedSourceWriter.Add(output, files));
        context.RegisterSourceOutput(
            sites.Select(static (sites, _) => CallSiteSources.CreateOverloadFiles(sites)).WithTrackingName(OverloadsTrackingName),
            static (output, files) => GeneratedSourceWriter.Add(output, files));
        context.RegisterSourceOutput(
            sites.Select(static (sites, _) => CallSiteSources.CreateInterceptorsFile(sites)).WithTrackingName(InterceptorsTrackingName),
            static (output, files) => GeneratedSourceWriter.Add(output, files));
    }
}
