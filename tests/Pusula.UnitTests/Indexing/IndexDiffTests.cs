using System.Collections.Immutable;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Indexing;

public sealed class IndexDiffTests
{
    private static ConfigFile File(string path, string content, Layer layer = Layer.Rule, LoadMode loadMode = LoadMode.EverySession) =>
        new(path, path[(path.LastIndexOf('/') + 1)..], layer, loadMode, default, null, null, null, content, content, 1, [], false);

    private static ConfigIndex Index(params ConfigFile[] files) =>
        new(
            "/root",
            DateTimeOffset.UnixEpoch,
            null,
            files.ToImmutableSortedDictionary(file => file.Path, file => file, StringComparer.Ordinal),
            ImmutableSortedDictionary<string, IReadOnlyList<Backlink>>.Empty);

    [Fact]
    public void Compute_IdenticalIndexes_IsEmpty()
    {
        ConfigIndex index = Index(File("a.md", "x"), File("b.md", "y"));

        IndexDiff diff = IndexDiff.Compute(index, index with { Version = 5, BuiltAt = DateTimeOffset.UtcNow });

        diff.IsEmpty.ShouldBeTrue();
        diff.Added.ShouldBeEmpty();
        diff.Removed.ShouldBeEmpty();
        diff.Changed.ShouldBeEmpty();
    }

    [Fact]
    public void Compute_AddedRemovedAndChangedFiles_AreReportedSorted()
    {
        ConfigIndex previous = Index(File("keep.md", "same"), File("gone-b.md", "x"), File("gone-a.md", "x"), File("edit-b.md", "old"), File("edit-a.md", "old"));
        ConfigIndex current = Index(File("keep.md", "same"), File("new-b.md", "x"), File("new-a.md", "x"), File("edit-b.md", "new"), File("edit-a.md", "new"));

        IndexDiff diff = IndexDiff.Compute(previous, current);

        diff.IsEmpty.ShouldBeFalse();
        diff.Added.ShouldBe(["new-a.md", "new-b.md"]);
        diff.Removed.ShouldBe(["gone-a.md", "gone-b.md"]);
        diff.Changed.ShouldBe(["edit-a.md", "edit-b.md"]);
    }

    [Fact]
    public void Compute_SameContentButDifferentLayer_IsChanged()
    {
        IndexDiff diff = IndexDiff.Compute(
            Index(File("a.md", "x", Layer.Rule)),
            Index(File("a.md", "x", Layer.PathRule)));

        diff.Changed.ShouldBe(["a.md"]);
        diff.Added.ShouldBeEmpty();
        diff.Removed.ShouldBeEmpty();
    }

    [Fact]
    public void Compute_SameContentButDifferentLoadMode_IsChanged()
    {
        IndexDiff diff = IndexDiff.Compute(
            Index(File("a.md", "x", loadMode: LoadMode.Inactive)),
            Index(File("a.md", "x", loadMode: LoadMode.EverySession)));

        diff.Changed.ShouldBe(["a.md"]);
    }

    [Fact]
    public void Compute_OnlyLinkStatusesDiffer_IsNotAChange()
    {
        ConfigFile before = File("a.md", "[[b]]") with { Links = [new Link(LinkKind.WikiLink, "b", 1, null, null, LinkStatus.Broken)] };
        ConfigFile after = File("a.md", "[[b]]") with { Links = [new Link(LinkKind.WikiLink, "b", 1, "b.md", null, LinkStatus.Resolved)], IsOrphan = true };

        IndexDiff.Compute(Index(before), Index(after)).IsEmpty.ShouldBeTrue();
    }

    // Only a change of the content (or of the layer or load mode) is a change: the time of a file is not.
    [Fact]
    public void Compute_OnlyTheTimeOfAFileDiffers_IsNotAChange()
    {
        ConfigFile before = File("a.md", "x") with { ModifiedAt = DateTimeOffset.UnixEpoch };
        ConfigFile after = File("a.md", "x") with { ModifiedAt = DateTimeOffset.UnixEpoch.AddDays(1) };

        IndexDiff.Compute(Index(before), Index(after)).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Compute_RealBuilds_AFileThatWasOnlyTouchedIsNotAChangeEvenThoughItsTimeIs()
    {
        using var temp = new TempDirectory();
        string note = temp.Write("a.md", "same");
        System.IO.File.SetLastWriteTimeUtc(note, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var builder = new IndexBuilder(NullLogger<IndexBuilder>.Instance);
        ConfigIndex first = builder.Build(temp.Path);

        System.IO.File.SetLastWriteTimeUtc(note, new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc));
        ConfigIndex second = builder.Build(temp.Path);

        second.Files["a.md"].ModifiedAt.ShouldNotBe(first.Files["a.md"].ModifiedAt);
        IndexDiff.Compute(first, second).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Compute_ContentComparison_IsCaseSensitiveAndOrdinal() =>
        IndexDiff.Compute(Index(File("a.md", "Text")), Index(File("a.md", "text"))).Changed.ShouldBe(["a.md"]);

    [Fact]
    public void Compute_RealBuilds_SeeEditsAdditionsRemovalsAndStyleSelection()
    {
        using var temp = new TempDirectory();
        temp.Write("keep.md", "same");
        temp.Write("edit.md", "old");
        temp.Write("remove.md", "bye");
        temp.Write("output-styles/style.md", "style");
        var builder = new IndexBuilder(NullLogger<IndexBuilder>.Instance);
        ConfigIndex first = builder.Build(temp.Path);

        temp.Write("edit.md", "new");
        temp.Write("add.md", "hello");
        System.IO.File.Delete(temp.Resolve("remove.md"));
        temp.Write("settings.json", "{\"outputStyle\":\"style\"}");
        ConfigIndex second = builder.Build(temp.Path);

        IndexDiff diff = IndexDiff.Compute(first, second);

        diff.Added.ShouldBe(["add.md"]);
        diff.Removed.ShouldBe(["remove.md"]);
        diff.Changed.ShouldBe(["edit.md", "output-styles/style.md"]);
    }

    [Fact]
    public void IndexChange_BuiltFromADiff_CarriesTheVersionAndTheKeys()
    {
        IndexDiff diff = IndexDiff.Compute(Index(File("a.md", "x")), Index(File("a.md", "y"), File("b.md", "z")));

        var change = new IndexChange(7, diff.Added, diff.Removed, diff.Changed);

        change.Version.ShouldBe(7);
        change.Added.ShouldBe(["b.md"]);
        change.Removed.ShouldBeEmpty();
        change.Changed.ShouldBe(["a.md"]);
    }
}
