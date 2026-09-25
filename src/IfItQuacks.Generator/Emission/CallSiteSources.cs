using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;

namespace IfItQuacks.Generator;

/// <summary>Merges what the call sites of a compilation need into the shared generated files.</summary>
internal static class CallSiteSources
{
    public static EquatableArray<Diagnostic> CollectDiagnostics(EquatableArray<CallSiteOutput> sites) =>
        new([.. sites.SelectMany(site => site.Diagnostics)]);

    public static EquatableArray<GeneratedFile> CreateAdaptersFile(EquatableArray<CallSiteOutput> sites)
    {
        var adapters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var adapter in sites.SelectMany(site => site.Adapters))
            adapters[adapter.Name] = adapter.Source;

        if (adapters.Count == 0)
            return default;

        var source = new StringBuilder(GeneratedCode.Header);
        source.AppendLine("namespace " + GeneratedCode.Namespace);
        source.AppendLine("{");
        foreach (var adapter in adapters.Values)
            source.Append(adapter);
        source.AppendLine("}");
        return new([new GeneratedFile(GeneratedCode.AdaptersHintName, source.ToString())]);
    }

    public static EquatableArray<GeneratedFile> CreateOverloadFiles(EquatableArray<CallSiteOutput> sites)
    {
        var files = new SortedDictionary<string, (OverloadMember Template, SortedSet<string> Members)>(StringComparer.Ordinal);
        foreach (var overload in sites.SelectMany(site => site.OverloadMembers))
        {
            if (!files.TryGetValue(overload.FileName, out var file))
                files[overload.FileName] = file = (overload, new SortedSet<string>(StringComparer.Ordinal));
            file.Members.Add(overload.Member);
        }

        return new([.. files.Values.Select(file =>
        {
            var indent = file.Template.Prefix.Substring(file.Template.Prefix.LastIndexOf('\n') + 1);
            return new GeneratedFile(file.Template.FileName, file.Template.Prefix + string.Join("\n" + indent, file.Members) + file.Template.Suffix);
        })]);
    }

    public static EquatableArray<GeneratedFile> CreateInterceptorsFile(EquatableArray<CallSiteOutput> sites)
    {
        var interceptors = sites.Select(site => site.Interceptor).OfType<string>().ToList();
        if (interceptors.Count == 0)
            return default;

        var source = new StringBuilder(GeneratedCode.Header);
        source.AppendLine(EmbeddedSources.InterceptsLocationAttributePolyfill);
        source.AppendLine("namespace " + GeneratedCode.Namespace);
        source.AppendLine("{");
        source.AppendLine("    file static class IfItQuacksInterceptors");
        source.AppendLine("    {");
        for (var index = 0; index < interceptors.Count; index++)
        {
            source.AppendLine();
            source.Append(interceptors[index].Replace(GeneratedCode.InterceptorIndexPlaceholder, (index + 1).ToString(CultureInfo.InvariantCulture)));
        }
        source.AppendLine("    }");
        source.AppendLine("}");
        return new([new GeneratedFile(GeneratedCode.InterceptorsHintName, source.ToString())]);
    }
}
