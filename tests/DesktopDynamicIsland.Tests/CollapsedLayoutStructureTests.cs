using System.IO;
using System.Xml.Linq;
using Xunit;

namespace DesktopDynamicIsland.Tests;

public sealed class CollapsedLayoutStructureTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void GpuTemperatureStatus_IsAPartialPathInsideTheCapsuleWithoutCollapsedText()
    {
        var document = XDocument.Load(FindProjectFile("src", "DesktopDynamicIsland", "MainWindow.xaml"));
        Assert.Equal("218", (string?)document.Root?.Attribute("Width"));
        var islandShell = FindNamedElement(document, "IslandShell");
        var collapsedView = FindNamedElement(document, "CollapsedView");
        var segment = FindNamedElement(document, "TemperatureBorderSegment");

        Assert.Equal("Path", segment.Name.LocalName);
        Assert.Contains(islandShell.Descendants(), element => element == segment);
        Assert.DoesNotContain(
            document.Descendants("TextBlock"),
            element => ((string?)element.Attribute(Xaml + "Name"))?.Contains("GpuTemperature", StringComparison.Ordinal) == true &&
                       !ReferenceEquals(element, FindNamedElement(document, "ExpandedGpuTemperatureText")));
        Assert.NotNull(FindNamedElement(document, "CollapsedSecondaryGroup"));
        Assert.Contains(collapsedView.Descendants(), element =>
            (string?)element.Attribute(Xaml + "Name") == "MetricPairToggleButton");
        Assert.Equal("Viewbox", FindNamedElement(document, "MetricPairToggleIcon").Name.LocalName);
        Assert.Equal("RotateTransform", FindNamedElement(document, "MetricPairToggleRotation").Name.LocalName);
        Assert.Equal("0,2,0,0", (string?)FindNamedElement(document, "CollapsedPrimaryIndicator").Attribute("Margin"));
        Assert.Equal("0,2,0,0", (string?)FindNamedElement(document, "CollapsedSecondaryIndicator").Attribute("Margin"));
        Assert.Equal("CollapsedMetricValueStyle", ((string?)FindNamedElement(document, "CollapsedPrimaryText").Attribute("Style"))?.Split(' ').Last().TrimEnd('}'));
        Assert.Equal("CollapsedMetricValueStyle", ((string?)FindNamedElement(document, "CollapsedSecondaryText").Attribute("Style"))?.Split(' ').Last().TrimEnd('}'));
        Assert.Equal("6", (string?)FindNamedElement(document, "MetricPairToggleButton").Attribute("Grid.Column"));

        var collapsedColumns = collapsedView
            .Element(Presentation + "Grid.ColumnDefinitions")!
            .Elements(Presentation + "ColumnDefinition")
            .Select(column => (string?)column.Attribute("Width"))
            .ToArray();
        Assert.Equal(new[] { "70", "5", "1", "9", "80", "6", "24" }, collapsedColumns);

        Assert.Equal(
            new[] { "6", "6", "24", "34" },
            GetColumnWidths(FindNamedElement(document, "CollapsedPrimaryGroup")));
        Assert.Equal(
            new[] { "6", "6", "34", "34" },
            GetColumnWidths(FindNamedElement(document, "CollapsedSecondaryGroup")));
    }

    private static XElement FindNamedElement(XDocument document, string name) =>
        document.Descendants().Single(element => (string?)element.Attribute(Xaml + "Name") == name);

    private static string?[] GetColumnWidths(XElement grid) =>
        grid.Element(Presentation + "Grid.ColumnDefinitions")!
            .Elements(Presentation + "ColumnDefinition")
            .Select(column => (string?)column.Attribute("Width"))
            .ToArray();

    private static string FindProjectFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DesktopDynamicIsland.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(relativeParts).ToArray());
    }
}
