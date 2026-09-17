using System.Xml.Linq;
using Xunit;

namespace IfItQuacks.Tests;

public class PackagingTests
{
    [Fact]
    public void PackageProps_EnablesInterceptorsForGeneratedNamespace()
    {
        var props = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "IfItQuacks.Generator", "build", "IfItQuacks.props"));

        var value = props.Descendants("InterceptorsNamespaces").Single().Value;

        Assert.Equal("$(InterceptorsNamespaces);IfItQuacks.Generated", value);
    }

    [Fact]
    public void PackageProps_IsPackedForDirectAndTransitiveReferences()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "IfItQuacks.Generator", "IfItQuacks.Generator.csproj"));

        var packed = project.Descendants("None").Single(e => (string?)e.Attribute("Include") == @"build\IfItQuacks.props");

        Assert.Equal("build;buildTransitive", (string?)packed.Attribute("PackagePath"));
    }

    // CallerFilePath is remapped for deterministic CI builds, so the root is located from the test output instead.
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "IfItQuacks.slnx")))
            directory = directory.Parent ?? throw new DirectoryNotFoundException("Repository root not found.");
        return directory.FullName;
    }
}
