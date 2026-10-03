using Pusula.Indexing;
using Pusula.Overview;
using Pusula.Tree;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Tree;

public sealed class TreeEndpointsTests
{
    [Fact]
    public void GetTree_ReturnsTheTreeOfTheCurrentIndex()
    {
        var provider = new FakeIndexProvider(IndexFactory.Index([IndexFactory.File("rules/a.md")], version: 4));

        TreeResponse response = TreeEndpoints.GetTree(provider).Value.ShouldNotBeNull();

        response.Version.ShouldBe(4);
        response.Nodes.Single().Name.ShouldBe("rules");
    }

    [Fact]
    public void GetOverview_ReturnsTheOverviewOfTheCurrentIndex()
    {
        var provider = new FakeIndexProvider(IndexFactory.Index([IndexFactory.File("CLAUDE.md", Layer.ClaudeMd)], version: 5, outputStyle: "Terse"));

        OverviewResponse response = OverviewEndpoints.GetOverview(provider).Value.ShouldNotBeNull();

        response.Version.ShouldBe(5);
        response.OutputStyle.ShouldBe("Terse");
        response.FileCount.ShouldBe(1);
    }
}
