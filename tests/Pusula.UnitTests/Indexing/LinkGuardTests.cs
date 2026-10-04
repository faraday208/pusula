using Pusula.Indexing;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Indexing;

// Which links may be followed. The decision is made by reading the links and nothing they lead to; the tests make real links, and a folder called "net" stands
// for the network (see FakeNetwork), which cannot be made to appear. What the system itself takes for a network location is tried on Windows only, with an
// address that nothing answers at (192.0.2.1): a guard that went there would take a long time, which is what the tests wait for.
public sealed class LinkGuardTests
{
    // ---- Which paths are network paths, by the way they are written --------------------------------------------------

    [Theory]
    [InlineData(@"\\server\share\x.md")]
    [InlineData(@"\\192.0.2.1\share")]
    [InlineData(@"\\server")]
    [InlineData("//server/share/x.md")]
    [InlineData(@"\/server\share")]
    [InlineData(@"/\server/share")]
    [InlineData(@"\\?\UNC\server\share\x.md")]
    [InlineData(@"\\?\unc\server\share\x.md")]
    [InlineData(@"\\.\UNC\server\share")]
    [InlineData(@"\??\UNC\server\share\x.md")]
    [InlineData(@"\\?\GLOBALROOT\Device\Mup\server\share")]
    [InlineData(@"\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}\x.md")]
    public void IsNetworkPath_ShareWrittenAnyWayOnWindows_IsANetworkPath(string path) =>
        LinkGuard.IsNetworkPath(path, windows: true).ShouldBeTrue();

    [Theory]
    [InlineData(@"C:\notes\x.md")]
    [InlineData("C:/notes/x.md")]
    [InlineData(@"\\?\C:\notes\x.md")]
    [InlineData(@"\\.\C:\notes\x.md")]
    [InlineData(@"\??\C:\notes\x.md")]
    [InlineData(@"..\notes\x.md")]
    [InlineData("notes/x.md")]
    [InlineData(@"\notes\x.md")]
    [InlineData("/notes/x.md")]
    [InlineData("x.md")]
    [InlineData("")]
    public void IsNetworkPath_DriveOrFolderWrittenAnyWayOnWindows_IsNotANetworkPath(string path) =>
        LinkGuard.IsNetworkPath(path, windows: true).ShouldBeFalse();

    [Theory]
    [InlineData(@"\\server\share\x.md")]
    [InlineData("//server/share/x.md")]
    [InlineData(@"\\?\UNC\server\share\x.md")]
    public void IsNetworkPath_NotOnWindows_IsNeverANetworkPath(string path) =>
        LinkGuard.IsNetworkPath(path, windows: false).ShouldBeFalse();

    // ---- Which links may be followed ---------------------------------------------------------------------------------

    // A tree with a folder that stands for the network, and a file and a folder in it.
    private static string MakeNetwork(TempDirectory temp)
    {
        temp.Write("net/x.md", "[[y]]");
        temp.CreateDirectory("net/folder");
        return temp.Resolve("net");
    }

    [Fact]
    public void MayFollow_EntryThatIsNoLink_IsFollowed()
    {
        using var temp = new TempDirectory();
        temp.Write("note.md", "x");
        temp.CreateDirectory("folder");
        using IDisposable network = FakeNetwork.In(MakeNetwork(temp));

        LinkGuard.MayFollow(new FileInfo(temp.Resolve("note.md"))).ShouldBeTrue();
        LinkGuard.MayFollow(new DirectoryInfo(temp.Resolve("folder"))).ShouldBeTrue();
        LinkGuard.MayFollow(new FileInfo(temp.Resolve("missing.md"))).ShouldBeTrue();
    }

    [Fact]
    public void MayFollow_LinksToWhatIsHere_AreFollowedWhetherOrNotTheyLeadAnywhere()
    {
        using var temp = new TempDirectory();
        temp.Write("real.md", "x");
        temp.CreateDirectory("folder");
        TestLinks.ToFile(temp.Resolve("to-file.md"), temp.Resolve("real.md"));
        TestLinks.ToFolder(temp.Resolve("to-folder"), temp.Resolve("folder"));
        TestLinks.ToFile(temp.Resolve("relative.md"), "real.md");
        TestLinks.ToFile(temp.Resolve("dangling.md"), temp.Resolve("missing.md"));
        using IDisposable network = FakeNetwork.In(MakeNetwork(temp));

        // A link to nothing leads to no network location: it is for whoever opens it to find that there is nothing there.
        LinkGuard.MayFollow(new FileInfo(temp.Resolve("to-file.md"))).ShouldBeTrue();
        LinkGuard.MayFollow(new DirectoryInfo(temp.Resolve("to-folder"))).ShouldBeTrue();
        LinkGuard.MayFollow(new FileInfo(temp.Resolve("relative.md"))).ShouldBeTrue();
        LinkGuard.MayFollow(new FileInfo(temp.Resolve("dangling.md"))).ShouldBeTrue();
    }

    [Fact]
    public void MayFollow_LinksIntoTheNetwork_AreRefused()
    {
        using var temp = new TempDirectory();
        string net = MakeNetwork(temp);
        TestLinks.ToFile(temp.Resolve("to-file.md"), Path.Join(net, "x.md"));
        TestLinks.ToFolder(temp.Resolve("to-folder"), Path.Join(net, "folder"));
        TestLinks.ToFolder(temp.Resolve("to-network"), net);
        using IDisposable network = FakeNetwork.In(net);

        LinkGuard.MayFollow(new FileInfo(temp.Resolve("to-file.md"))).ShouldBeFalse();
        LinkGuard.MayFollow(new DirectoryInfo(temp.Resolve("to-folder"))).ShouldBeFalse();
        LinkGuard.MayFollow(new DirectoryInfo(temp.Resolve("to-network"))).ShouldBeFalse();
    }

    [Fact]
    public void MayFollow_ChainOfLocalLinks_IsFollowedAndOneThatEndsInTheNetworkIsRefused()
    {
        using var temp = new TempDirectory();
        string net = MakeNetwork(temp);
        temp.Write("real.md", "x");
        TestLinks.ToFile(temp.Resolve("local-c.md"), temp.Resolve("real.md"));
        TestLinks.ToFile(temp.Resolve("local-b.md"), "local-c.md");
        TestLinks.ToFile(temp.Resolve("local-a.md"), temp.Resolve("local-b.md"));
        TestLinks.ToFile(temp.Resolve("remote-c.md"), Path.Join(net, "x.md"));
        TestLinks.ToFile(temp.Resolve("remote-b.md"), "remote-c.md");
        TestLinks.ToFile(temp.Resolve("remote-a.md"), temp.Resolve("remote-b.md"));
        using IDisposable network = FakeNetwork.In(net);

        // Each link is read for what it says, so that the one at the end is found whatever the first one looks like.
        LinkGuard.MayFollow(new FileInfo(temp.Resolve("local-a.md"))).ShouldBeTrue();
        LinkGuard.MayFollow(new FileInfo(temp.Resolve("remote-a.md"))).ShouldBeFalse();
    }

    // The links of the folders of a path count too, and a path may be in a folder that is a link (as /var is on macOS): room is left for them.
    [Theory]
    [InlineData(-2, true)]
    [InlineData(1, false)]
    public void MayFollow_ChainOfLinks_IsFollowedUpToTheLimitAndNoFurther(int fromTheLimit, bool followed)
    {
        using var temp = new TempDirectory();
        temp.Write("real.md", "x");
        int length = LinkGuard.MaxLinks + fromTheLimit;
        for (int i = 1; i <= length; i++)
        {
            TestLinks.ToFile(temp.Resolve($"link{i}.md"), i == length ? "real.md" : $"link{i + 1}.md");
        }

        using IDisposable network = FakeNetwork.In(temp.Resolve("net"));

        LinkGuard.MayFollow(new FileInfo(temp.Resolve("link1.md"))).ShouldBe(followed);
    }

    [Fact]
    public void MayFollow_LinkToItselfAndLinksInACircle_AreRefused()
    {
        using var temp = new TempDirectory();
        TestLinks.ToFile(temp.Resolve("self.md"), "self.md");
        TestLinks.ToFile(temp.Resolve("a.md"), "b.md");
        TestLinks.ToFile(temp.Resolve("b.md"), "a.md");
        using IDisposable network = FakeNetwork.In(temp.Resolve("net"));

        LinkGuard.MayFollow(new FileInfo(temp.Resolve("self.md"))).ShouldBeFalse();
        LinkGuard.MayFollow(new FileInfo(temp.Resolve("a.md"))).ShouldBeFalse();
    }

    [Fact]
    public void MayFollow_RelativeLinkThroughAFolderThatIsALinkIntoTheNetwork_IsRefused()
    {
        using var temp = new TempDirectory();
        string net = MakeNetwork(temp);
        temp.CreateDirectory("docs");
        TestLinks.ToFolder(temp.Resolve("docs/a"), net);
        TestLinks.ToFile(temp.Resolve("docs/note.md"), "a/x.md");
        TestLinks.ToFolder(temp.Resolve("docs/b"), temp.Resolve("docs"));
        TestLinks.ToFile(temp.Resolve("docs/other.md"), "b/note.md");
        using IDisposable network = FakeNetwork.In(net);

        // The text of the link names nothing but a folder beside it: it is that folder that leads to the network, which only looking at it tells.
        LinkGuard.MayFollow(new FileInfo(temp.Resolve("docs/note.md"))).ShouldBeFalse();
        LinkGuard.MayFollow(new FileInfo(temp.Resolve("docs/other.md"))).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MayFollow_LinkWithParentFoldersInItsTarget_IsJudgedFromTheFolderItReallyIsIn(bool evilIsInTheNetwork)
    {
        using var temp = new TempDirectory();
        string net = MakeNetwork(temp);
        temp.CreateDirectory("tree/real/nested");
        temp.CreateDirectory("tree/here");
        temp.Write("tree/here/x.md", "x");
        TestLinks.ToFolder(temp.Resolve("tree/evil"), evilIsInTheNetwork ? net : temp.Resolve("tree/here"));
        TestLinks.ToFile(temp.Resolve("tree/real/nested/note.md"), "../../evil/x.md");
        TestLinks.ToFolder(temp.Resolve("tree/via"), temp.Resolve("tree/real/nested"));
        using IDisposable network = FakeNetwork.In(net);

        // The link is reached through "via", which is another name for "nested": its two parents are "real" and "tree", and "evil" is a folder of "tree". Read from
        // the path as it is written (tree/via/note.md) they would be "tree" and the folder around it, where there is no "evil".
        LinkGuard.MayFollow(new FileInfo(temp.Resolve("tree/via/note.md"))).ShouldBe(!evilIsInTheNetwork);
        LinkGuard.MayFollow(new FileInfo(temp.Resolve("tree/real/nested/note.md"))).ShouldBe(!evilIsInTheNetwork);
    }

    [Fact]
    public void MayFollow_NotOnWindowsAndWithNoScope_FollowsEverything()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows has the rule that this is not subject to.");
        using var temp = new TempDirectory();

        // Written as a share on Windows, here it is the name of a file that is not there: a network share is mounted first, and already logged on to.
        TestLinks.ToFile(temp.Resolve("share.md"), @"\\192.0.2.1\share\x.md");

        LinkGuard.MayFollow(new FileInfo(temp.Resolve("share.md"))).ShouldBeTrue();
    }

    [Fact]
    public void Use_Scope_ChangesWhatThreadTakesForTheNetworkUntilItEndsAndNoOtherThread()
    {
        using var temp = new TempDirectory();
        string net = MakeNetwork(temp);
        TestLinks.ToFile(temp.Resolve("to-net.md"), Path.Join(net, "x.md"));
        var link = new FileInfo(temp.Resolve("to-net.md"));
        bool seenElsewhere = false;

        using (FakeNetwork.In(net))
        {
            // Another thread has no scope: it is the system that decides there, for which a folder like any other is no network location.
            var thread = new Thread(() => seenElsewhere = LinkGuard.MayFollow(link));
            thread.Start();
            thread.Join();

            LinkGuard.MayFollow(link).ShouldBeFalse();
        }

        LinkGuard.MayFollow(link).ShouldBeTrue();
        seenElsewhere.ShouldBeTrue();
    }

    // ---- Which paths may be opened -----------------------------------------------------------------------------------

    [Fact]
    public void MayTouch_PathThroughAFolderThatIsALinkIntoTheNetwork_IsRefused()
    {
        using var temp = new TempDirectory();
        string net = MakeNetwork(temp);
        temp.Write("tree/rules/a.md", "x");
        temp.CreateDirectory("tree/here");
        TestLinks.ToFolder(temp.Resolve("tree/evil"), net);
        TestLinks.ToFolder(temp.Resolve("tree/ok"), temp.Resolve("tree/here"));
        TestLinks.ToFile(temp.Resolve("tree/rules/to-net.md"), Path.Join(net, "x.md"));
        using IDisposable network = FakeNetwork.In(net);

        string root = temp.Resolve("tree");
        LinkGuard.MayTouch(root, "evil/x.md").ShouldBeFalse();
        LinkGuard.MayTouch(root, "evil").ShouldBeFalse();
        LinkGuard.MayTouch(root, "rules/to-net.md").ShouldBeFalse();
        LinkGuard.MayTouch(root, "ok/anything.md").ShouldBeTrue();
        LinkGuard.MayTouch(root, "rules/a.md").ShouldBeTrue();
        LinkGuard.MayTouch(root, "rules/missing.md").ShouldBeTrue();
    }

    // ---- On Windows, with the system's own way of telling a network location -----------------------------------------

    // Waits a while for the decision: a guard that went to the address would not come back before long.
    private static async Task<bool> MayFollowWithinAWhile(FileSystemInfo link)
    {
        Task<bool> decision = Task.Run(() => LinkGuard.MayFollow(link), TestContext.Current.CancellationToken);

        Task first = await Task.WhenAny(decision, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        first.ShouldBeSameAs(decision, "The guard did not come back: it went to the network.");
        return await decision;
    }

    [Theory]
    [InlineData(@"\\192.0.2.1\share\x.md")]
    [InlineData("//192.0.2.1/share/x.md")]
    public async Task MayFollow_Windows_FileLinkToAShareWrittenAnyWay_IsRefusedWithoutGoingThere(string target)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows and its network paths.");
        using var temp = new TempDirectory();
        TestLinks.ToFile(temp.Resolve("share.md"), target);

        (await MayFollowWithinAWhile(new FileInfo(temp.Resolve("share.md")))).ShouldBeFalse();
    }

    [Fact]
    public async Task MayFollow_Windows_FolderLinkToAShare_IsRefusedWithoutGoingThere()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows and its network paths.");
        using var temp = new TempDirectory();
        TestLinks.ToFolder(temp.Resolve("share"), @"\\192.0.2.1\share\folder");

        (await MayFollowWithinAWhile(new DirectoryInfo(temp.Resolve("share")))).ShouldBeFalse();
    }

    [Fact]
    public async Task MayFollow_Windows_LinkToALinkToAShareAndLinkThroughAFolderThatLeadsToOne_AreRefusedWithoutGoingThere()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows and its network paths.");
        using var temp = new TempDirectory();
        temp.CreateDirectory("docs");
        TestLinks.ToFile(temp.Resolve("docs/second.md"), @"\\192.0.2.1\share\x.md");
        TestLinks.ToFile(temp.Resolve("docs/first.md"), "second.md");
        TestLinks.ToFolder(temp.Resolve("docs/a"), @"\\192.0.2.1\share");
        TestLinks.ToFile(temp.Resolve("docs/note.md"), @"a\x.md");

        (await MayFollowWithinAWhile(new FileInfo(temp.Resolve("docs/first.md")))).ShouldBeFalse();
        (await MayFollowWithinAWhile(new FileInfo(temp.Resolve("docs/note.md")))).ShouldBeFalse();
    }

    [Fact]
    public async Task MayFollow_Windows_LinksToWhatIsOnThisDriveAreFollowed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Needs Windows and its drives.");
        using var temp = new TempDirectory();
        temp.Write("real.md", "x");
        temp.CreateDirectory("folder");
        TestLinks.ToFile(temp.Resolve("to-file.md"), temp.Resolve("real.md"));
        TestLinks.ToFile(temp.Resolve("relative.md"), @"..\" + Path.GetFileName(temp.Path) + @"\real.md");
        TestLinks.ToFolder(temp.Resolve("to-folder"), temp.Resolve("folder"));

        (await MayFollowWithinAWhile(new FileInfo(temp.Resolve("to-file.md")))).ShouldBeTrue();
        (await MayFollowWithinAWhile(new FileInfo(temp.Resolve("relative.md")))).ShouldBeTrue();
        (await MayFollowWithinAWhile(new DirectoryInfo(temp.Resolve("to-folder")))).ShouldBeTrue();
    }
}
