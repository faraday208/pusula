using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Pusula.Indexing;
using Pusula.Links;
using Pusula.Overview;
using Pusula.Startup;
using Pusula.UnitTests.Support;
using Shouldly;
using Xunit;

namespace Pusula.UnitTests.Overview;

public sealed class OverviewMapperTests
{
    private static OverviewResponse Map(ConfigFile[] files, string? outputStyle = null) =>
        OverviewMapper.ToResponse(IndexFactory.Index(files, root: "/home/u/.claude", version: 3, outputStyle: outputStyle));

    private static ConfigFile Loaded(string path, int everySession, Layer layer = Layer.Rule, LoadMode loadMode = LoadMode.EverySession) =>
        IndexFactory.File(path, layer, loadMode, new TokenCounts(everySession * 2, everySession, 0));

    [Fact]
    public void ToResponse_EmptyIndex_HasEmptyListsAndZeroTotals()
    {
        OverviewResponse response = Map([]);

        response.FileCount.ShouldBe(0);
        response.EverySessionTokens.ShouldBe(0);
        response.OutputStyle.ShouldBeNull();
        response.Layers.ShouldBeEmpty();
        response.Heaviest.ShouldBeEmpty();
        response.ProjectMemory.ShouldBeEmpty();
        response.Broken.ShouldBeEmpty();
        response.Pending.ShouldBeEmpty();
        response.Orphans.ShouldBeEmpty();
        response.FrontmatterErrors.ShouldBeEmpty();
    }

    [Fact]
    public void ToResponse_CarriesRootVersionOutputStyleAndTotals()
    {
        OverviewResponse response = Map([Loaded("a.md", 10), Loaded("b.md", 5, loadMode: LoadMode.OnDemand)], outputStyle: "Terse");

        response.Root.ShouldBe("/home/u/.claude");
        response.Version.ShouldBe(3);
        response.BuiltAt.ShouldBe(DateTimeOffset.UnixEpoch);
        response.OutputStyle.ShouldBe("Terse");
        response.FileCount.ShouldBe(2);
        response.EverySessionTokens.ShouldBe(15);
    }

    [Fact]
    public void ToResponse_Layers_ListOnlyLayersWithFilesInLayerOrder()
    {
        OverviewResponse response = Map(
        [
            IndexFactory.File("m.md", Layer.Memory, LoadMode.OnDemand, new TokenCounts(4, 0, 0)),
            IndexFactory.File("CLAUDE.md", Layer.ClaudeMd, LoadMode.EverySession, new TokenCounts(100, 100, 0)),
            IndexFactory.File("m2.md", Layer.Memory, LoadMode.OnDemand, new TokenCounts(6, 0, 0)),
            IndexFactory.File("skills/s/SKILL.md", Layer.Skill, LoadMode.DescriptionEverySession, new TokenCounts(50, 8, 0)),
        ]);

        response.Layers.ShouldBe(
        [
            new OverviewLayer(Layer.ClaudeMd, 1, 100, 100),
            new OverviewLayer(Layer.Skill, 1, 50, 8),
            new OverviewLayer(Layer.Memory, 2, 10, 0),
        ]);
    }

    [Fact]
    public void ToResponse_Heaviest_IsTheTenLargestEverySessionContributorsDescending()
    {
        ConfigFile[] files =
        [
            .. Enumerable.Range(1, 12).Select(n => Loaded($"f{n:D2}.md", n * 10)),
            Loaded("zero.md", 0),
            Loaded("on-demand.md", 0, loadMode: LoadMode.OnDemand),
        ];

        OverviewResponse response = Map(files);

        response.Heaviest.Select(entry => entry.Path).ShouldBe(["f12.md", "f11.md", "f10.md", "f09.md", "f08.md", "f07.md", "f06.md", "f05.md", "f04.md", "f03.md"]);
        response.Heaviest.Select(entry => entry.EverySessionTokens).ShouldBe([120, 110, 100, 90, 80, 70, 60, 50, 40, 30]);
        response.Heaviest[0].ShouldBe(new OverviewHeaviestFile("f12.md", Layer.Rule, LoadMode.EverySession, 120));
    }

    [Fact]
    public void ToResponse_Heaviest_BreaksTiesByPathAndSkipsFilesThatAddNothing()
    {
        OverviewResponse response = Map([Loaded("b.md", 5), Loaded("a.md", 5), Loaded("c.md", 0)]);

        response.Heaviest.Select(entry => entry.Path).ShouldBe(["a.md", "b.md"]);
    }

    [Fact]
    public void ToResponse_ProjectMemory_ListsOnlyMemoryIndexesLargestFirst()
    {
        OverviewResponse response = Map(
        [
            IndexFactory.File("projects/small/memory/MEMORY.md", Layer.MemoryIndex, LoadMode.ProjectSession, new TokenCounts(10, 0, 10)),
            IndexFactory.File("projects/big/memory/MEMORY.md", Layer.MemoryIndex, LoadMode.ProjectSession, new TokenCounts(300, 0, 300)),
            IndexFactory.File("projects/tie/memory/MEMORY.md", Layer.MemoryIndex, LoadMode.ProjectSession, new TokenCounts(10, 0, 10)),
            IndexFactory.File("projects/big/memory/other.md", Layer.Memory, LoadMode.OnDemand, new TokenCounts(999, 0, 0)),
        ]);

        response.ProjectMemory.ShouldBe(
        [
            new OverviewProjectMemory("projects/big/memory/MEMORY.md", "big", 300),
            new OverviewProjectMemory("projects/small/memory/MEMORY.md", "small", 10),
            new OverviewProjectMemory("projects/tie/memory/MEMORY.md", "tie", 10),
        ]);
    }

    [Fact]
    public void ToResponse_BrokenAndPendingLinks_AreListedPerSourceInDocumentOrder()
    {
        OverviewResponse response = Map(
        [
            IndexFactory.File(
                "z.md",
                links:
                [
                    IndexFactory.Link(LinkKind.MarkdownLink, "gone.md", 2, LinkStatus.Broken, "gone.md"),
                    IndexFactory.Link(LinkKind.WikiLink, "later", 5, LinkStatus.Pending),
                    IndexFactory.Link(LinkKind.WikiLink, "nowhere", 6, LinkStatus.Broken),
                    IndexFactory.Link(LinkKind.MarkdownLink, "ok.md", 7, LinkStatus.Resolved, "a.md"),
                    IndexFactory.Link(LinkKind.MarkdownLink, "dir/", 8, LinkStatus.NonMarkdown, "dir"),
                    IndexFactory.Link(LinkKind.MarkdownLink, "../x", 9, LinkStatus.External),
                ]),
            IndexFactory.File(
                "a.md",
                links:
                [
                    IndexFactory.Link(LinkKind.ClaudePath, "~/.claude/x.md", 1, LinkStatus.Broken, "x.md"),
                    IndexFactory.Link(LinkKind.WikiLink, "soon", 3, LinkStatus.Pending),
                ]),
        ]);

        response.Broken.ShouldBe(
        [
            new OverviewBrokenLink("a.md", 1, LinkKind.ClaudePath, "~/.claude/x.md", "x.md"),
            new OverviewBrokenLink("z.md", 2, LinkKind.MarkdownLink, "gone.md", "gone.md"),
            new OverviewBrokenLink("z.md", 6, LinkKind.WikiLink, "nowhere"),
        ]);
        response.Pending.ShouldBe(
        [
            new OverviewPendingLink("a.md", 3, LinkKind.WikiLink, "soon"),
            new OverviewPendingLink("z.md", 5, LinkKind.WikiLink, "later"),
        ]);
    }

    [Fact]
    public void ToResponse_OrphansAndFrontmatterErrors_ListTheFlaggedFiles()
    {
        OverviewResponse response = Map(
        [
            IndexFactory.File("linked.md", Layer.Shared),
            IndexFactory.File("lonely.md", Layer.Memory, isOrphan: true),
            IndexFactory.File("ref.md", Layer.Reference, isOrphan: true, frontmatterError: "Line 3: bad", frontmatterErrorLine: 3),
            IndexFactory.File("rule.md", frontmatterError: "Frontmatter is not a mapping"),
        ]);

        response.Orphans.ShouldBe([new OverviewOrphan("lonely.md", Layer.Memory), new OverviewOrphan("ref.md", Layer.Reference)]);
        response.FrontmatterErrors.ShouldBe(
        [
            new OverviewFrontmatterError("ref.md", "Line 3: bad", 3),
            new OverviewFrontmatterError("rule.md", "Frontmatter is not a mapping"),
        ]);
    }

    [Fact]
    public void ToResponse_Serialized_FrontmatterErrorHasTheLinePropertyOnlyWhenTheLineIsKnown()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJson.Configure(options);
        OverviewResponse response = Map(
        [
            IndexFactory.File("a.md", frontmatterError: "Line 4, column 2: bad", frontmatterErrorLine: 4),
            IndexFactory.File("b.md", frontmatterError: "Frontmatter is not a mapping"),
        ]);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(response, options));

        JsonElement[] errors = [.. json.RootElement.GetProperty("frontmatterErrors").EnumerateArray()];
        errors[0].EnumerateObject().Select(property => property.Name).ShouldBe(["path", "error", "line"]);
        errors[0].GetProperty("line").GetInt32().ShouldBe(4);
        errors[1].EnumerateObject().Select(property => property.Name).ShouldBe(["path", "error"]);
    }

    // ---- Profile and tags -------------------------------------------------------------------------------------

    [Fact]
    public void ToResponse_ClaudeFolder_HasTheClaudeProfileAndNoTags()
    {
        OverviewResponse response = Map([IndexFactory.File("CLAUDE.md", Layer.ClaudeMd)]);

        response.Profile.ShouldBe(SourceProfile.Claude);
        response.Tags.ShouldNotBeNull().ShouldBeEmpty();
    }

    [Fact]
    public void ToResponse_Tags_CountTheNotesThatCarryEachMostUsedFirstThenByName()
    {
        ConfigIndex index = IndexFactory.Index(
            [
                IndexFactory.File("a.md", Layer.Note, tags: ["todo", "Zebra"]),
                IndexFactory.File("b.md", Layer.Note, tags: ["TODO", "idea", "alpha"]),
                IndexFactory.File("c.md", Layer.Note, tags: ["todo", "ZEBRA"]),
                IndexFactory.File("d.md", Layer.Note),
            ],
            profile: SourceProfile.Vault);

        OverviewResponse response = OverviewMapper.ToResponse(index);

        response.Profile.ShouldBe(SourceProfile.Vault);

        // "todo" and "Zebra" are spelled in several ways: the first spelling in path order is the one that is shown.
        response.Tags.ShouldNotBeNull().ShouldBe(
        [
            new OverviewTag("todo", 3),
            new OverviewTag("Zebra", 2),
            new OverviewTag("alpha", 1),
            new OverviewTag("idea", 1),
        ]);
    }

    // ---- Notes: the entry note, the recent notes and the most linked ones ---------------------------------------------

    private static ConfigFile Note(string path, IReadOnlyList<Link>? links = null, string[]? tags = null, DateTimeOffset? modifiedAt = null) =>
        IndexFactory.File(path, Layer.Note, LoadMode.OnDemand, links: links, tags: tags, modifiedAt: modifiedAt);

    // A link to another note that was found: the raw text is the name of the note.
    private static Link LinkTo(string target) =>
        IndexFactory.Link(LinkKind.WikiLink, target[..^".md".Length], 1, LinkStatus.Resolved, target);

    private static OverviewResponse MapNotes(ConfigFile[] files, SourceProfile profile = SourceProfile.Vault) =>
        OverviewMapper.ToResponse(IndexFactory.Index(files, profile: profile));

    private static readonly DateTimeOffset Morning = new(2026, 10, 2, 8, 30, 15, TimeSpan.Zero);

    [Theory]
    [InlineData(new[] { "start.md", "MOC.md", "Readme.md", "INDEX.md", "home.md", "other.md" }, "home.md", "home")]
    [InlineData(new[] { "start.md", "MOC.md", "Readme.md", "INDEX.md", "other.md" }, "INDEX.md", "INDEX")]
    [InlineData(new[] { "start.md", "MOC.md", "Readme.md", "other.md" }, "Readme.md", "Readme")]
    [InlineData(new[] { "start.md", "MOC.md", "other.md" }, "MOC.md", "MOC")]
    [InlineData(new[] { "Start.md", "other.md" }, "Start.md", "Start")]
    public void ToResponse_Notes_EntryIsTheRootNoteWhoseNameComesFirstInTheOrderHomeIndexReadmeMocStart(string[] paths, string entryPath, string entryTitle)
    {
        OverviewResponse response = MapNotes([.. paths.Select(path => Note(path))]);

        response.Entry.ShouldBe(new OverviewEntryNote(entryPath, entryTitle));
    }

    [Theory]
    [InlineData(SourceProfile.Vault)]
    [InlineData(SourceProfile.Markdown)]
    public void ToResponse_NotesOfEveryProfile_HaveAnEntry(SourceProfile profile) =>
        MapNotes([Note("Home.md")], profile).Entry.ShouldBe(new OverviewEntryNote("Home.md", "Home"));

    [Fact]
    public void ToResponse_Notes_EntryNameIgnoresCaseAndTheCaseOfTheExtensionAndTheFirstInPathOrderWins()
    {
        MapNotes([Note("HOME.MD"), Note("Other.md")]).Entry.ShouldBe(new OverviewEntryNote("HOME.MD", "HOME"));
        MapNotes([Note("home.md"), Note("Home.md")]).Entry.ShouldBe(new OverviewEntryNote("Home.md", "Home"));
    }

    [Fact]
    public void ToResponse_Notes_EntryHasToBeAtTheRootAndItsNameHasToBeTheWholeName()
    {
        MapNotes(
        [
            Note("folder/Home.md"), Note("notes/Index.md"), Note("Homework.md"), Note("Home page.md"), Note("Home.v2.md"), Note("Start here.md"),
        ]).Entry.ShouldBeNull();

        // A note in a folder does not keep a root note with a worse name from being the entry.
        MapNotes([Note("folder/Home.md"), Note("Start.md")]).Entry.ShouldBe(new OverviewEntryNote("Start.md", "Start"));
    }

    [Fact]
    public void ToResponse_Notes_WithoutANamedNoteTheEntryIsTheTaggedNoteThatLinksToTheMostOtherNotes()
    {
        OverviewResponse response = MapNotes(
        [
            Note("maps/Wide map.md", [LinkTo("a.md"), LinkTo("b.md"), LinkTo("c.md")], tags: ["MOC"]),
            Note("maps/Small.md", [LinkTo("a.md")], tags: ["home"]),
            Note("busy.md", [LinkTo("a.md"), LinkTo("b.md"), LinkTo("c.md"), LinkTo("maps/Small.md")], tags: ["todo"]),
            Note("a.md"),
            Note("b.md"),
            Note("c.md"),
        ]);

        // The map with the most links is the entry, though the small one comes first by path.
        response.Entry.ShouldBe(new OverviewEntryNote("maps/Wide map.md", "Wide map"));
    }

    [Fact]
    public void ToResponse_Notes_TaggedNotesThatLinkToTheSameNumberOfNotesGoByPath()
    {
        OverviewResponse response = MapNotes(
        [
            Note("z.md", [LinkTo("a.md")], tags: ["moc"]),
            Note("m.md", [LinkTo("a.md")], tags: ["home"]),
            Note("a.md"),
        ]);

        response.Entry.ShouldBe(new OverviewEntryNote("m.md", "m"));
    }

    // Only a link that leads to another note counts: not one to itself, a note that is not written yet, an attachment or nothing.
    [Fact]
    public void ToResponse_Notes_OnlyLinksThatLeadToAnotherNoteCountForTheEntryThatIsPickedByTag()
    {
        OverviewResponse response = MapNotes(
        [
            Note(
                "maps/Selfish.md",
                [
                    LinkTo("maps/Selfish.md"),
                    LinkTo("maps/Selfish.md"),
                    IndexFactory.Link(LinkKind.WikiLink, "later", 3, LinkStatus.Pending),
                    IndexFactory.Link(LinkKind.Embed, "pic.png", 4, LinkStatus.NonMarkdown, "pic.png"),
                    IndexFactory.Link(LinkKind.MarkdownLink, "gone.md", 5, LinkStatus.Broken, "gone.md"),
                    IndexFactory.Link(LinkKind.MarkdownLink, "https://example.com", 6, LinkStatus.External),
                ],
                tags: ["moc"]),
            Note("maps/One link.md", [LinkTo("a.md")], tags: ["moc"]),
            Note("a.md"),
        ]);

        response.Entry.ShouldBe(new OverviewEntryNote("maps/One link.md", "One link"));
    }

    [Fact]
    public void ToResponse_Notes_WithoutANamedOrATaggedNoteThereIsNoEntry() =>
        MapNotes([Note("a.md", tags: ["todo"]), Note("folder/b.md", tags: ["mocking", "homework"]), Note("c.md")]).Entry.ShouldBeNull();

    [Fact]
    public void ToResponse_ClaudeFolder_HasNoEntryAndNoRecentOrMostLinkedNotes()
    {
        ConfigFile home = IndexFactory.File("Home.md", Layer.Reference, links: [LinkTo("b.md")], tags: ["moc"], modifiedAt: Morning);
        ConfigFile other = IndexFactory.File("b.md", Layer.Reference, modifiedAt: Morning.AddDays(1));

        OverviewResponse response = Map([home, other]);

        response.Profile.ShouldBe(SourceProfile.Claude);
        response.Entry.ShouldBeNull();
        response.Recent.ShouldBeEmpty();
        response.MostLinked.ShouldBeEmpty();
    }

    [Fact]
    public void ToResponse_Notes_RecentIsTheEightNotesWrittenLastNewestFirstAndTheSameTimeGoesByPath()
    {
        ConfigFile[] notes =
        [
            .. Enumerable.Range(0, 10).Select(number => Note($"n{number}.md", modifiedAt: Morning.AddHours(number))),
            Note("tie-b.md", modifiedAt: Morning.AddHours(20)),
            Note("tie-a.md", modifiedAt: Morning.AddHours(20)),
        ];

        OverviewResponse response = MapNotes(notes);

        response.Recent.Select(note => note.Path).ShouldBe(["tie-a.md", "tie-b.md", "n9.md", "n8.md", "n7.md", "n6.md", "n5.md", "n4.md"]);
        response.Recent[0].ShouldBe(new OverviewRecentNote("tie-a.md", Morning.AddHours(20)));
        response.Recent[^1].ShouldBe(new OverviewRecentNote("n4.md", Morning.AddHours(4)));
    }

    [Fact]
    public void ToResponse_Notes_RecentListsWhatThereIsWhenThereAreFewerThanEight()
    {
        MapNotes([Note("old.md", modifiedAt: Morning), Note("new.md", modifiedAt: Morning.AddMinutes(1))]).Recent.Select(note => note.Path).ShouldBe(["new.md", "old.md"]);
        MapNotes([]).Recent.ShouldBeEmpty();
    }

    [Fact]
    public void ToResponse_Notes_MostLinkedAreTheNotesWithTheMostBacklinksCountedPerLinkAndTheSameNumberGoesByPath()
    {
        OverviewResponse response = MapNotes(
        [
            Note("hub.md"),
            Note("one-b.md"),
            Note("one-a.md"),
            Note("nobody.md"),

            // Two links to the hub from one note count twice; the link to itself counts for nothing.
            Note("x.md", [LinkTo("hub.md"), LinkTo("hub.md"), LinkTo("one-b.md"), LinkTo("x.md")]),
            Note("y.md", [LinkTo("hub.md"), LinkTo("one-a.md")]),
        ]);

        response.MostLinked.ShouldBe(
        [
            new OverviewMostLinkedNote("hub.md", 3),
            new OverviewMostLinkedNote("one-a.md", 1),
            new OverviewMostLinkedNote("one-b.md", 1),
        ]);
    }

    [Fact]
    public void ToResponse_Notes_MostLinkedAreAtMostEightAndLinksThatLeadToNoNoteAreNoBacklinks()
    {
        ConfigFile[] targets = [.. Enumerable.Range(0, 10).Select(number => Note($"t{number}.md"))];
        ConfigFile source = Note("src.md", [.. Enumerable.Range(0, 10).Select(number => LinkTo($"t{number}.md"))]);

        OverviewResponse many = MapNotes([.. targets, source]);
        OverviewResponse none = MapNotes(
        [
            Note(
                "src.md",
                [
                    IndexFactory.Link(LinkKind.WikiLink, "later", 1, LinkStatus.Pending),
                    IndexFactory.Link(LinkKind.Embed, "pic.png", 2, LinkStatus.NonMarkdown, "pic.png"),
                    IndexFactory.Link(LinkKind.MarkdownLink, "gone.md", 3, LinkStatus.Broken, "gone.md"),
                ]),
        ]);

        many.MostLinked.Select(note => note.Path).ShouldBe(["t0.md", "t1.md", "t2.md", "t3.md", "t4.md", "t5.md", "t6.md", "t7.md"]);
        many.MostLinked.ShouldAllBe(note => note.Count == 1);
        none.MostLinked.ShouldBeEmpty();
    }

    // The same choices over a vault on disk: the tags and links are the ones the builder found, not made up by the test.
    [Fact]
    public void ToResponse_VaultOnDisk_EntryIsPickedByNameThenByTagAndThereIsNoneWhenNothingFits()
    {
        using var temp = new TempDirectory();
        temp.Write("Maps/Reading.md", "---\ntags: [moc]\n---\n[[a]] [[b]] [[c]]\n");
        temp.Write("Maps/Small.md", "#home\n[[a]]\n");
        temp.Write("a.md", "x\n");
        temp.Write("b.md", "x\n");
        temp.Write("c.md", "x\n");
        var builder = new IndexBuilder(NullLogger<IndexBuilder>.Instance);

        OverviewMapper.ToResponse(builder.Build(temp.Path, SourceProfile.Vault)).Entry.ShouldBe(new OverviewEntryNote("Maps/Reading.md", "Reading"));

        temp.Write("Start.md", "Begin here.\n");

        OverviewMapper.ToResponse(builder.Build(temp.Path, SourceProfile.Markdown)).Entry.ShouldBe(new OverviewEntryNote("Start.md", "Start"));

        File.Delete(temp.Resolve("Start.md"));
        File.Delete(temp.Resolve("Maps/Reading.md"));
        File.Delete(temp.Resolve("Maps/Small.md"));

        OverviewMapper.ToResponse(builder.Build(temp.Path, SourceProfile.Vault)).Entry.ShouldBeNull();
    }

    [Fact]
    public void ToResponse_Serialized_NoteListsCarryTheirFieldsAndEntryIsLeftOutWhenThereIsNone()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJson.Configure(options);
        OverviewResponse withEntry = MapNotes([Note("Home.md", [LinkTo("b.md")], modifiedAt: Morning), Note("b.md", modifiedAt: Morning.AddDays(-1))]);
        OverviewResponse withoutEntry = MapNotes([Note("b.md", modifiedAt: Morning)]);

        using JsonDocument first = JsonDocument.Parse(JsonSerializer.Serialize(withEntry, options));
        using JsonDocument second = JsonDocument.Parse(JsonSerializer.Serialize(withoutEntry, options));

        first.RootElement.GetProperty("entry").EnumerateObject().Select(property => property.Name).ShouldBe(["path", "title"]);
        first.RootElement.GetProperty("entry").GetProperty("title").GetString().ShouldBe("Home");
        JsonElement recent = first.RootElement.GetProperty("recent")[0];
        recent.EnumerateObject().Select(property => property.Name).ShouldBe(["path", "modifiedAt"]);
        recent.GetProperty("path").GetString().ShouldBe("Home.md");
        recent.GetProperty("modifiedAt").GetDateTimeOffset().ShouldBe(Morning);
        recent.GetProperty("modifiedAt").GetString().ShouldEndWith("+00:00");
        JsonElement linked = first.RootElement.GetProperty("mostLinked")[0];
        linked.EnumerateObject().Select(property => property.Name).ShouldBe(["path", "count"]);
        linked.GetProperty("count").GetInt32().ShouldBe(1);

        second.RootElement.TryGetProperty("entry", out _).ShouldBeFalse();
        second.RootElement.GetProperty("mostLinked").GetArrayLength().ShouldBe(0);
        second.RootElement.GetProperty("recent").GetArrayLength().ShouldBe(1);
    }
}
