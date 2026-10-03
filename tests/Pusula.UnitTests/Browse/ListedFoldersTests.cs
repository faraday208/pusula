using Pusula.Browse;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

public sealed class ListedFoldersTests
{
    [Fact]
    public void Contains_FolderOfASource_IsListed()
    {
        var listed = new ListedFolders(new FakeSourceRegistry("/data/notes", "/data/work"));

        listed.Contains("/data/notes").ShouldBeTrue();
        listed.Contains("/data/work").ShouldBeTrue();
    }

    [Fact]
    public void Contains_SeparatorAtTheEnd_DoesNotMatter()
    {
        var listed = new ListedFolders(new FakeSourceRegistry("/data/notes/"));

        listed.Contains("/data/notes").ShouldBeTrue();
        listed.Contains("/data/notes/").ShouldBeTrue();
    }

    [Fact]
    public void Contains_FolderAboveOrBelowOrBesideASource_IsNotListed()
    {
        var listed = new ListedFolders(new FakeSourceRegistry("/data/notes"));

        listed.Contains("/data").ShouldBeFalse();
        listed.Contains("/data/notes/sub").ShouldBeFalse();
        listed.Contains("/data/notes2").ShouldBeFalse();
        listed.Contains("/data/note").ShouldBeFalse();
    }

    [Fact]
    public void Contains_CaseOfThePath_IsComparedTheWayThePlatformDoes()
    {
        var listed = new ListedFolders(new FakeSourceRegistry("/data/Notes"));

        listed.Contains("/data/notes").ShouldBe(OperatingSystem.IsWindows());
    }

    [Fact]
    public void Contains_NoSources_NothingIsListed()
    {
        new ListedFolders(new FakeSourceRegistry()).Contains("/data/notes").ShouldBeFalse();
        ListedFolders.None.Contains("/data/notes").ShouldBeFalse();
    }
}
