using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

#pragma warning disable RSEXPERIMENTAL002 // InterceptableLocation API is experimental

namespace IfItQuacks.Generator;

/// <summary>
/// Generates adapters and interceptors that let <c>[DuckTyped]</c> methods accept any type structurally matching a <c>[DuckShape]</c> interface.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class IfItQuacksGenerator : IIncrementalGenerator
{
    private const string DuckTypedAttributeName = "IfItQuacks.DuckTypedAttribute";
    private const string DuckShapeAttributeName = "IfItQuacks.DuckShapeAttribute";
    private const string GeneratedNamespace = "IfItQuacks.Generated";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(ctx =>
            ctx.AddSource("IfItQuacksAttributes.g.cs", SourceText.From(EmbeddedSources.Attributes, Encoding.UTF8)));

        var duckTypedMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DuckTypedAttributeName,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, _) => AnalyzeDuckTypedMethod(ctx))
            .Collect();

        var compilationAndMethods = context.CompilationProvider.Combine(duckTypedMethods);

        context.RegisterSourceOutput(compilationAndMethods, static (spc, pair) =>
            Execute(spc, pair.Left, pair.Right));
    }

    private static DuckTypedMethodModel? AnalyzeDuckTypedMethod(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method)
            return null;

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var location = ctx.TargetNode.GetLocation();

        if (method.ContainingType.DeclaringSyntaxReferences
                .Select(r => r.GetSyntax())
                .OfType<TypeDeclarationSyntax>()
                .Any(t => !t.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ContainingTypeNotPartial, location,
                method.Name, method.ContainingType.Name));
        }

        if (!method.IsStatic || method.Parameters.Length != 1)
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedSignature, location, method.Name,
                !method.IsStatic ? "it is an instance method" : $"it has {method.Parameters.Length} parameters"));
            return new DuckTypedMethodModel(method, null, diagnostics.ToImmutable());
        }

        if (method.Parameters[0].RefKind != RefKind.None)
        {
            var modifiers = ((MethodDeclarationSyntax)ctx.TargetNode).ParameterList.Parameters[0].Modifiers;
            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedSignature, location, method.Name,
                $"its parameter is declared '{modifiers}'"));
            return new DuckTypedMethodModel(method, null, diagnostics.ToImmutable());
        }

        var parameterType = method.Parameters[0].Type;
        if (parameterType is not INamedTypeSymbol { TypeKind: TypeKind.Interface } shapeType ||
            !shapeType.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == DuckShapeAttributeName))
        {
            diagnostics.Add(Diagnostic.Create(Diagnostics.ParameterNotShape, location, method.Name,
                parameterType.ToDisplayString()));
            return new DuckTypedMethodModel(method, null, diagnostics.ToImmutable());
        }

        return new DuckTypedMethodModel(method, shapeType, diagnostics.ToImmutable());
    }

    private static void Execute(SourceProductionContext spc, Compilation compilation,
        ImmutableArray<DuckTypedMethodModel?> models)
    {
        var validMethods = new List<DuckTypedMethodModel>();
        foreach (var model in models)
        {
            if (model is null) continue;
            foreach (var d in model.Diagnostics) spc.ReportDiagnostic(d);
            if (model.Shape is not null) validMethods.Add(model);
        }

        if (validMethods.Count == 0) return;

        var methodsByName = validMethods
            .GroupBy(m => m.Method.Name)
            .Where(g =>
            {
                if (g.Count() > 1)
                {
                    foreach (var method in g.Select(m => m.Method))
                        spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedSignature,
                            method.Locations.FirstOrDefault(), method.Name,
                            "it is overloaded (only one [DuckTyped] method per name is supported)"));
                    return false;
                }
                return true;
            })
            .ToDictionary(g => g.Key, g => g.Single());

        foreach (var model in methodsByName.Values)
            EmitFallbackOverload(spc, model);

        var adaptersEmitted = new HashSet<string>();
        var adapterSources = new StringBuilder();
        var interceptorSources = new StringBuilder();
        var interceptorCount = 0;

        interceptorSources.AppendLine("// <auto-generated/>");
        interceptorSources.AppendLine("#nullable enable");
        interceptorSources.AppendLine(EmbeddedSources.InterceptsLocationAttributePolyfill);
        interceptorSources.AppendLine("namespace " + GeneratedNamespace);
        interceptorSources.AppendLine("{");
        interceptorSources.AppendLine("    file static class IfItQuacksInterceptors");
        interceptorSources.AppendLine("    {");

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot(spc.CancellationToken);
            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
            SemanticModel? semanticModel = null;

            foreach (var invocation in invocations)
            {
                var name = GetInvokedName(invocation);
                if (name is null || !methodsByName.TryGetValue(name, out var duckMethod))
                    continue;

                semanticModel ??= compilation.GetSemanticModel(tree);

                var symbolInfo = semanticModel.GetSymbolInfo(invocation, spc.CancellationToken);
                var candidates = symbolInfo.Symbol is IMethodSymbol s
                    ? [s]
                    : symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToImmutableArray();

                if (!candidates.Any(c => SymbolEqualityComparer.Default.Equals(c.OriginalDefinition, duckMethod.Method)))
                    continue;

                if (invocation.ArgumentList.Arguments.Count != 1)
                    continue;

                var argExpr = invocation.ArgumentList.Arguments[0].Expression;
                var argType = semanticModel.GetTypeInfo(argExpr, spc.CancellationToken).Type;
                if (argType is not INamedTypeSymbol concreteType || concreteType.TypeKind == TypeKind.Error)
                    continue;

                var mismatch = ShapeMatcher.FindMismatch(duckMethod.Shape!, concreteType);
                if (mismatch is not null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.ShapeMismatch, argExpr.GetLocation(),
                        concreteType.ToDisplayString(), duckMethod.Shape!.ToDisplayString(), mismatch));
                    continue;
                }

                var implementsDirectly = concreteType.AllInterfaces
                    .Any(i => SymbolEqualityComparer.Default.Equals(i, duckMethod.Shape));

                var unsupportedStructKind = GetUnsupportedStructKind(concreteType, implementsDirectly);
                if (unsupportedStructKind is not null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.UnsupportedStructArgument, argExpr.GetLocation(),
                        concreteType.ToDisplayString(), duckMethod.Shape!.ToDisplayString(), unsupportedStructKind));
                    continue;
                }

                var interceptableLocation = semanticModel.GetInterceptableLocation(invocation, spc.CancellationToken);
                if (interceptableLocation is null)
                    continue;

                var adapterTypeName = "";
                if (!implementsDirectly)
                {
                    adapterTypeName = AdapterEmitter.GetAdapterName(duckMethod.Shape!, concreteType);
                    if (adaptersEmitted.Add(adapterTypeName))
                        AdapterEmitter.Emit(adapterSources, duckMethod.Shape!, concreteType, adapterTypeName);
                }

                interceptorCount++;
                EmitInterceptor(interceptorSources, interceptableLocation, duckMethod.Method, concreteType,
                    implementsDirectly, adapterTypeName, interceptorCount);
            }
        }

        interceptorSources.AppendLine("    }");
        interceptorSources.AppendLine("}");

        if (interceptorCount > 0)
        {
            spc.AddSource("IfItQuacks.Interceptors.g.cs", SourceText.From(interceptorSources.ToString(), Encoding.UTF8));
        }

        if (adaptersEmitted.Count > 0)
        {
            var full = new StringBuilder();
            full.AppendLine("// <auto-generated/>");
            full.AppendLine("#nullable enable");
            full.AppendLine("namespace " + GeneratedNamespace);
            full.AppendLine("{");
            full.Append(adapterSources);
            full.AppendLine("}");
            spc.AddSource("IfItQuacks.Adapters.g.cs", SourceText.From(full.ToString(), Encoding.UTF8));
        }
    }

    // A struct implementing the shape is boxed by the compiler itself, so only adapter-wrapped mutable structs would silently lose mutations.
    private static string? GetUnsupportedStructKind(INamedTypeSymbol type, bool implementsDirectly)
    {
        if (type.IsRefLikeType)
            return "ref structs cannot be converted to an interface";

        if (type.IsValueType && !type.IsReadOnly && !implementsDirectly)
            return "mutable structs are copied into an adapter, so mutations made through the shape would be lost";

        return null;
    }

    private static string? GetInvokedName(InvocationExpressionSyntax invocation) => invocation.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
        _ => null,
    };

    private static void EmitFallbackOverload(SourceProductionContext spc, DuckTypedMethodModel model)
    {
        var method = model.Method;
        var containingType = method.ContainingType;
        var returnType = method.ReturnType.ToDisplayString();
        var accessibility = Utilities.AccessibilityKeyword(method.DeclaredAccessibility);

        var body = $$"""
            throw new global::IfItQuacks.DuckShapeMismatchException(typeof(T), typeof({{method.Parameters[0].Type.ToDisplayString()}}));
            """;

        var methodSource = $$"""
            {{accessibility}} static {{returnType}} {{method.Name}}<T>(T value)
            {
                {{body}}
            }
            """;

        var wrapped = TypeWrapper.WrapInContainingScope(containingType, methodSource);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.Append(wrapped);

        spc.AddSource($"IfItQuacks.Fallback.{containingType.ToDisplayString().Replace('.', '_').Replace('<', '_').Replace('>', '_')}.{method.Name}.g.cs",
            SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void EmitInterceptor(StringBuilder sb, InterceptableLocation location, IMethodSymbol method,
        INamedTypeSymbol concreteType, bool implementsDirectly, string adapterTypeName, int index)
    {
        var concreteTypeName = concreteType.ToDisplayString();
        var methodOwner = method.ContainingType.ToDisplayString();
        var shapeTypeName = method.Parameters[0].Type.ToDisplayString();
        var rawArgExpr = implementsDirectly ? "value" : $"new global::{GeneratedNamespace}.{adapterTypeName}(value)";
        // Cast to the shape interface so overload resolution picks the real, non-generic
        // method instead of the generated generic fallback (identity conversion otherwise wins).
        var argExpr = $"(global::{shapeTypeName})({rawArgExpr})";

        sb.AppendLine();
        sb.AppendLine("        " + location.GetInterceptsLocationAttributeSyntax());
        sb.AppendLine($"        public static {method.ReturnType.ToDisplayString()} Interceptor_{index}({concreteTypeName} value)");
        sb.AppendLine("        {");
        sb.AppendLine(method.ReturnsVoid
            ? $"            global::{methodOwner}.{method.Name}({argExpr});"
            : $"            return global::{methodOwner}.{method.Name}({argExpr});");
        sb.AppendLine("        }");
    }
}
