using Pusula.Browse;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Browse;

// The way of listing a folder that a test can give to the calls of its own thread.
public sealed class FolderWalkTests
{
    private static string[] Names(IEnumerable<FileSystemInfo> entries) => [.. entries.Select(entry => entry.Name)];

    [Fact]
    public void Use_ListingOfThisThread_AnswersForEntriesUntilTheScopeEnds()
    {
        using var temp = new TempDirectory();
        temp.Write("real.md", "x");
        FileSystemInfo[] fake = [new FileInfo(temp.Resolve("fake.md"))];

        using (FolderWalk.Use(_ => fake))
        {
            Names(FolderWalk.Entries(temp.Path)).ShouldBe(["fake.md"]);
        }

        Names(FolderWalk.Entries(temp.Path)).ShouldBe(["real.md"]);
    }

    [Fact]
    public void Use_ListingOfThisThread_IsGivenTheFolderThatWasAskedFor()
    {
        using var temp = new TempDirectory();
        var asked = new List<string>();

        using (FolderWalk.Use(folder =>
        {
            asked.Add(folder);
            return [];
        }))
        {
            FolderWalk.Entries(temp.Path).ShouldBeEmpty();
        }

        asked.ShouldBe([temp.Path]);
    }

    [Fact]
    public void SystemEntries_WhateverTheListingOfTheThreadIs_IsWhatTheFileSystemSays()
    {
        using var temp = new TempDirectory();
        temp.Write("real.md", "x");

        using (FolderWalk.Use(_ => []))
        {
            Names(FolderWalk.SystemEntries(temp.Path)).ShouldBe(["real.md"]);
        }
    }

    [Fact]
    public void Use_ListingOfOneThread_DoesNotChangeWhatAnotherThreadSees()
    {
        using var temp = new TempDirectory();
        temp.Write("real.md", "x");
        string[] other = [];

        // The other thread is made and joined here, with no await: an await could bring the test back on a thread that the scope is not on.
        using (FolderWalk.Use(_ => []))
        {
            var thread = new Thread(() => other = Names(FolderWalk.Entries(temp.Path)));
            thread.Start();
            thread.Join();

            FolderWalk.Entries(temp.Path).ShouldBeEmpty();
        }

        other.ShouldBe(["real.md"]);
    }

    [Fact]
    public void Use_ScopesThatAreNested_GoBackToTheOneOutsideAndThenToTheFileSystem()
    {
        using var temp = new TempDirectory();
        temp.Write("real.md", "x");

        using (FolderWalk.Use(_ => [new FileInfo(temp.Resolve("outer.md"))]))
        {
            using (FolderWalk.Use(_ => [new FileInfo(temp.Resolve("inner.md"))]))
            {
                Names(FolderWalk.Entries(temp.Path)).ShouldBe(["inner.md"]);
            }

            Names(FolderWalk.Entries(temp.Path)).ShouldBe(["outer.md"]);
        }

        Names(FolderWalk.Entries(temp.Path)).ShouldBe(["real.md"]);
    }

    [Fact]
    public void Use_NoListing_LeavesTheFileSystemToAnswer()
    {
        using var temp = new TempDirectory();
        temp.Write("real.md", "x");

        using (FolderWalk.Use(null))
        {
            Names(FolderWalk.Entries(temp.Path)).ShouldBe(["real.md"]);
        }
    }
}
