using System.Xml.Linq;

namespace Flenta.Tests.Architecture;

public sealed class DependencyTests
{
    [Theory]
    [InlineData("Flenta.Domain", "")]
    [InlineData("Flenta.Application", "Flenta.Domain")]
    [InlineData("Flenta.Infrastructure", "Flenta.Application,Flenta.Domain")]
    [InlineData("Flenta.Api", "Flenta.Application,Flenta.Infrastructure")]
    public void Project_dependencies_point_towards_the_inner_layers(string project, string expected)
    {
        var document = ReadProject(project);
        var actual = document.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var allowed = expected.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(allowed, actual);
    }

    [Fact]
    public void Domain_is_independent_of_external_packages_and_frameworks()
    {
        var document = ReadProject("Flenta.Domain");

        Assert.Empty(document.Descendants("PackageReference"));
        Assert.Empty(document.Descendants("FrameworkReference"));
    }

    [Fact]
    public void Application_has_no_database_web_or_authentication_framework_dependency()
    {
        var document = ReadProject("Flenta.Application");
        var packages = document.Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")!.Value);

        Assert.All(packages, package => Assert.Equal("Microsoft.Extensions.DependencyInjection.Abstractions", package));
        Assert.Empty(document.Descendants("FrameworkReference"));
    }

    private static XDocument ReadProject(string project)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Flenta.Backend.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return XDocument.Load(Path.Combine(directory.FullName, "src", project, $"{project}.csproj"));
    }
}
