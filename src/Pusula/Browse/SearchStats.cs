using System.Diagnostics;

namespace Pusula.Browse;

/// <summary>
/// What one search for folders did and what each stage of it cost, for the log at Debug level: counters and times, kept as the
/// search goes (a timestamp and a few additions for each folder it reads or each note it opens, never for each entry) and written out
/// once, at its end. It decides nothing: the search does what it does with or without it.
/// </summary>
internal sealed class SearchStats
{
    /// <summary>The walk over the disk: folders read, what they took from the budget, and how long.</summary>
    public Part Walk { get; } = new();

    /// <summary>The asking of the folders at the last level of the walk for an entry note.</summary>
    public Part Probes { get; } = new();

    /// <summary>The looking into the candidates for folders of notes: counting what is below one, and sampling its notes.</summary>
    public ScanPart Scans { get; } = new();

    /// <summary>The describing of the folders that were found (what they look like, how many Markdown files are in them): one folder at a time, with the budget of the counting.</summary>
    public Part Describes { get; } = new();

    /// <summary>How many folders with an entry note the walk read the entries of.</summary>
    public int Candidates { get; set; }

    /// <summary>How many folders the walk left at its last level, to be asked for an entry note.</summary>
    public int LastLevelFolders { get; set; }

    /// <summary>How many vaults the walk found.</summary>
    public int VaultsFound { get; set; }

    /// <summary>How many of the folders at the last level that were asked have an entry note.</summary>
    public int ProbesFound { get; set; }

    /// <summary>How many candidates were folders of notes, before the folders that lie inside one another were settled.</summary>
    public int FoldersOfNotes { get; set; }

    /// <summary>How many candidates the budget did not reach.</summary>
    public int Unreached { get; set; }

    /// <summary>The stage of the search that the budget ran out in; null when it did not run out.</summary>
    public string? SpentDuring { get; private set; }

    /// <summary>What ran the budget out, <c>entries</c> or <c>time</c>.</summary>
    public string? SpentBy { get; private set; }

    /// <summary>How many entries had been taken when the budget ran out.</summary>
    public int SpentEntries { get; private set; }

    /// <summary>How long it was from the start of the search when the budget ran out: the moment itself, and not the end of the stage it ran out in.</summary>
    public TimeSpan SpentAfter { get; private set; }

    /// <summary>How long the stages that are measured took in all, in milliseconds.</summary>
    public double AccountedMilliseconds => Ms(Walk.Ticks + Probes.Ticks + Scans.Ticks + Describes.Ticks);

    /// <summary>Notes where the budget ran out, if it has, at the end of a stage: the first stage that ends with it spent is the one it ran out in.</summary>
    /// <param name="budget">The budget of the search.</param>
    /// <param name="stage">What the search was doing, in words for the log.</param>
    public void NoteBudget(ScanBudget budget, string stage)
    {
        if (SpentDuring is null && budget.IsSpent)
        {
            SpentDuring = stage;
            SpentBy = budget.SpentBy;
            SpentEntries = budget.Taken;
            SpentAfter = budget.SpentAt;
        }
    }

    /// <summary>The milliseconds of stopwatch ticks.</summary>
    /// <param name="ticks">A time taken as a difference of <see cref="Stopwatch.GetTimestamp"/>.</param>
    public static double Ms(long ticks) => Stopwatch.GetElapsedTime(0, ticks).TotalMilliseconds;

    /// <summary>One kind of work: how many times it was done, what it took from the budget, how long it took, and the one time that took longest.</summary>
    internal class Part
    {
        /// <summary>How many times it was done.</summary>
        public int Count { get; private set; }

        /// <summary>How many entries it took from the budget in all.</summary>
        public long Entries { get; private set; }

        /// <summary>How long it took in all, in stopwatch ticks.</summary>
        public long Ticks { get; private set; }

        /// <summary>The longest one time took, in stopwatch ticks.</summary>
        public long SlowestTicks { get; private set; }

        /// <summary>The folder the slowest one was about.</summary>
        public string? Slowest { get; private set; }

        /// <summary>How long it took in all, in milliseconds.</summary>
        public double Milliseconds => Ms(Ticks);

        /// <summary>The longest one time took, in milliseconds.</summary>
        public double SlowestMilliseconds => Ms(SlowestTicks);

        /// <summary>Notes one time it was done.</summary>
        /// <param name="ticks">How long it took, in stopwatch ticks.</param>
        /// <param name="entries">What it took from the budget.</param>
        /// <param name="folder">The folder it was about.</param>
        public void Add(long ticks, long entries, string folder)
        {
            Count++;
            Entries += entries;
            Ticks += ticks;
            if (ticks > SlowestTicks)
            {
                SlowestTicks = ticks;
                Slowest = folder;
            }
        }
    }

    /// <summary>The looking into folders for their notes: besides what every kind of work has, how many were cut at the limit of entries, and how many notes were opened and for how long.</summary>
    internal sealed class ScanPart : Part
    {
        /// <summary>How many of the folders had more entries than the limit for one folder, and were judged by what was read.</summary>
        public int Capped { get; set; }

        /// <summary>How many notes were opened for the sample.</summary>
        public int NotesOpened { get; private set; }

        /// <summary>How long opening and reading the notes took in all, in stopwatch ticks (it is part of <see cref="Part.Ticks"/> too).</summary>
        public long NoteTicks { get; private set; }

        /// <summary>The longest one note took to open and read, in stopwatch ticks.</summary>
        public long SlowestNoteTicks { get; private set; }

        /// <summary>The folder of the note that took longest (the name of a note is not logged).</summary>
        public string? SlowestNoteFolder { get; private set; }

        /// <summary>How long opening and reading the notes took in all, in milliseconds.</summary>
        public double NoteMilliseconds => Ms(NoteTicks);

        /// <summary>The longest one note took to open and read, in milliseconds.</summary>
        public double SlowestNoteMilliseconds => Ms(SlowestNoteTicks);

        /// <summary>Notes one note that was opened for the sample.</summary>
        /// <param name="ticks">How long opening and reading it took, in stopwatch ticks.</param>
        /// <param name="note">Full path of the note, of which only the folder is kept, and only if it is the slowest.</param>
        public void AddNote(long ticks, string note)
        {
            NotesOpened++;
            NoteTicks += ticks;
            if (ticks > SlowestNoteTicks)
            {
                SlowestNoteTicks = ticks;
                SlowestNoteFolder = Path.GetDirectoryName(note);
            }
        }
    }
}
