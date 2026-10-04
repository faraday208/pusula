namespace Pusula.Browse;

/// <summary>
/// What looking into a folder showed about the notes in it, and the rules that make a folder without a <c>.obsidian</c>
/// directory a folder of notes (<see cref="FolderKind.Notes"/>). All four have to hold: the folder itself has an entry note
/// (<see cref="IsEntryNote"/>); it has at least <see cref="MinNotes"/> Markdown files in it and up to <see cref="Levels"/>
/// levels below it; they are at least <see cref="MinNotesPercent"/> percent of the files there (<see cref="HasEnoughNotes"/>);
/// and at least <see cref="MinLinkedPercent"/> percent of a sample of the notes have a wikilink (<see cref="IsFolderOfNotes"/>).
/// The numbers come from measuring the folders of two real machines: with them every Obsidian vault was found at its own root,
/// the vaults that have no <c>.obsidian</c> directory were found, and a folder of documentation (a README and many pages, but no
/// wikilinks between them) was not. Without the wikilink rule such folders came in, and parent folders were chosen in place of the
/// vaults. They are not to be tuned by feel: change them with a new measurement. (A folder at the last level of the walk is not read
/// by it, so there the entry note is looked for among the first <see cref="ProbeEntries"/> entries of the folder only: a choice.)
/// </summary>
/// <param name="Notes">The Markdown files in the folder and up to <see cref="Levels"/> levels below it.</param>
/// <param name="Files">All the files there, the Markdown ones included; hidden files are left out.</param>
/// <param name="Sampled">How many of the notes were read to see whether they have a wikilink (those that could be read, at most <see cref="SampleSize"/>); 0 when none was.</param>
/// <param name="Linked">How many of the sampled notes have a wikilink in their first <see cref="SampleBytes"/> bytes.</param>
internal readonly record struct NoteFolderCheck(int Notes, int Files, int Sampled, int Linked)
{
    /// <summary>How many levels below a folder the look into it goes: the folders inside it are level 1, so the files of a folder three levels down count and the folders below that are not entered.</summary>
    internal const int Levels = 3;

    /// <summary>The fewest Markdown files a folder of notes has.</summary>
    internal const int MinNotes = 10;

    /// <summary>The share of the files of a folder of notes that are Markdown, at least, in percent.</summary>
    internal const int MinNotesPercent = 50;

    /// <summary>The share of the sampled notes that have a wikilink, at least, in percent.</summary>
    internal const int MinLinkedPercent = 20;

    /// <summary>How many notes are read at most, spread over the folder.</summary>
    internal const int SampleSize = 20;

    /// <summary>How much of the start of a note is read; a wikilink after that is not seen.</summary>
    internal const int SampleBytes = 4096;

    /// <summary>The share of the wikilinked notes of a folder of notes that a folder inside it holds, in percent, from which the one inside is shown and the outer one is not (<see cref="Holds"/>).</summary>
    internal const int InnerPercent = 60;

    /// <summary>
    /// How many of its first entries a folder at the last level of the walk is asked for an entry note: one that has none among them is
    /// taken to have none (see <see cref="NoteFolderScanner.HasEntryNote"/>). This is a choice, not a measurement: the folders of notes that
    /// people keep are small near their root, where the entry note is, and the six names are not looked for further down a folder of
    /// thousands of entries (pictures, downloads), which asking all the folders of the last level could not afford.
    /// </summary>
    internal const int ProbeEntries = 200;

    // The notes that a person starts from; one of them in a folder is what makes the folder worth looking into.
    private static readonly string[] EntryNotes = ["Home.md", "index.md", "README.md", "MOC.md", "_index.md", "start.md"];

    /// <summary>Whether a file is an entry note: <c>Home.md</c>, <c>index.md</c>, <c>README.md</c>, <c>MOC.md</c>, <c>_index.md</c> or <c>start.md</c>, in any case.</summary>
    /// <param name="fileName">The name of the file, not a path.</param>
    public static bool IsEntryNote(string fileName)
    {
        // Asked of every file the search reads, so no closure and no allocation.
        foreach (string entry in EntryNotes)
        {
            if (string.Equals(entry, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether there are notes enough: at least <see cref="MinNotes"/> of them, and at least <see cref="MinNotesPercent"/> percent of the files. The notes are read only for a folder that has that.</summary>
    public bool HasEnoughNotes => Notes >= MinNotes && Notes * 100L >= Files * (long)MinNotesPercent;

    /// <summary>Whether the notes tell a folder of notes: <see cref="HasEnoughNotes"/>, and at least <see cref="MinLinkedPercent"/> percent of the sampled notes have a wikilink. (The entry note is the search's to see, not part of the counts.)</summary>
    public bool IsFolderOfNotes => HasEnoughNotes && Sampled > 0 && Linked * 100L >= Sampled * (long)MinLinkedPercent;

    /// <summary>
    /// Whether a folder inside another holds at least <see cref="InnerPercent"/> percent of the wikilinked notes of the outer
    /// one. The number of wikilinked notes of a folder is estimated as the share of the sample that has a wikilink times the
    /// number of notes; the comparison is made without rounding. A folder with nothing sampled holds none.
    /// </summary>
    /// <param name="inner">What the folder inside showed.</param>
    /// <param name="outer">What the folder around it showed.</param>
    public static bool Holds(NoteFolderCheck inner, NoteFolderCheck outer) =>
        inner.Sampled > 0
        && outer.Sampled > 0
        && (long)inner.Linked * inner.Notes * outer.Sampled * 100 >= (long)outer.Linked * outer.Notes * inner.Sampled * InnerPercent;
}
