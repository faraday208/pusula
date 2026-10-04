using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using Pusula.Indexing;

namespace Pusula.Browse;

/// <summary>
/// Looks into a folder to see what <see cref="NoteFolderCheck"/> asks about it: how many Markdown files and how many files
/// there are in it and up to <see cref="NoteFolderCheck.Levels"/> levels below it (hidden files, and the folders that the
/// search leaves alone, see <see cref="FolderNames.IsLeftOutOfSearch"/>, are not counted), and, for a folder that has notes
/// enough, whether a sample of them has a wikilink. The sample is at most <see cref="NoteFolderCheck.SampleSize"/> notes,
/// spread over the folder: the paths of the notes are put in order and every n-th one is taken, so the same folder gives the
/// same sample. Of each only the first <see cref="NoteFolderCheck.SampleBytes"/> bytes are read, to look for <c>[[</c>.
/// The folder is only ever read, and what is in a note never leaves this class: only whether it has <c>[[</c>. A note that is a
/// symbolic link is read through the link, and judged by the file it leads to; one that leads to nothing is no part of the sample. A folder that the
/// walk over the disk did not read (it is at the last level of the walk) is asked <see cref="HasEntryNote"/> before it is looked into.
/// Everything here that reads a folder takes each entry it reads from the budget (and each note it opens), so that the budget, the
/// time included, can stop it at any point.
/// </summary>
internal static class NoteFolderScanner
{
    /// <summary>
    /// Whether a folder has an entry note (see <see cref="NoteFolderCheck.IsEntryNote"/>), for a folder that the walk over the disk
    /// has not read the entries of. Its entries are read one after the other, as the walk reads them, until the first entry note
    /// or until <see cref="NoteFolderCheck.ProbeEntries"/> of them have been read, whichever comes first, and every entry that is
    /// read is taken from the budget before it is looked at: the number of entries and the time both stop the asking, in the middle
    /// of a folder too, however big it is. A folder that has no entry note among its first entries is taken to have none (a choice,
    /// see <see cref="NoteFolderCheck.ProbeEntries"/>: a folder of notes is small near its root, and the entries come in the order of
    /// the file system, so the six names are not looked for further down a big folder). The case of the name does not matter. The
    /// asking costs the budget one entry for the folder, so that the time is asked for even where there is nothing to read, and one
    /// for each entry read: at most 1 + <see cref="NoteFolderCheck.ProbeEntries"/>. The folder is only ever read.
    /// </summary>
    /// <param name="folder">Full path of the folder.</param>
    /// <param name="budget">What the asking may still read, shared with the search.</param>
    /// <param name="cancellationToken">Stops the asking when the request is gone.</param>
    /// <param name="part">Where to note what the asking cost, for the log; nowhere when null.</param>
    /// <returns>Whether there is an entry note among the first entries; null when the budget ran out before that was known. A folder that cannot be read has none.</returns>
    public static bool? HasEntryNote(string folder, ScanBudget budget, CancellationToken cancellationToken, SearchStats.Part? part = null)
    {
        if (part is null)
        {
            return Ask(folder, budget, cancellationToken);
        }

        long started = Stopwatch.GetTimestamp();
        int taken = budget.Taken;
        try
        {
            return Ask(folder, budget, cancellationToken);
        }
        finally
        {
            part.Add(Stopwatch.GetTimestamp() - started, budget.Taken - taken, folder);
        }
    }

    private static bool? Ask(string folder, ScanBudget budget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!budget.TrySpend())
        {
            return null;
        }

        int read = 0;
        try
        {
            foreach (FileSystemInfo entry in FolderWalk.Entries(folder))
            {
                // The entries after the first ones are not read, and so cost nothing.
                if (++read > NoteFolderCheck.ProbeEntries)
                {
                    break;
                }

                if (!budget.TrySpend())
                {
                    return null;
                }

                // As the walk takes an entry note: a file (a link to one included), not a folder that is called like one.
                if (entry is not DirectoryInfo && NoteFolderCheck.IsEntryNote(entry.Name))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // What cannot be read has no entry note that is known; what was read of it stays read.
        }

        return false;
    }

    /// <summary>
    /// Looks into a folder. Every entry read, and every note opened, is taken from the budget, and the look stops when the
    /// budget has no more to give.
    /// </summary>
    /// <param name="folder">Full path of the folder, as it was reached.</param>
    /// <param name="realPath">Its real path (see <see cref="FolderWalk.RealPath"/>): a link that leads back to a folder the look is in is not entered.</param>
    /// <param name="sampleAlways">
    /// Whether to read the sample of a folder that has not notes enough as well. A folder that may be one of notes is read
    /// only when it has them (reading is what costs); a vault that lies inside one is read always, for it is the number of its
    /// wikilinked notes that is wanted.
    /// </param>
    /// <param name="maxEntries">The most entries to read below the folder; with more, it is judged by what was read (see <see cref="BrowseLimits.CheckEntriesPerFolder"/>).</param>
    /// <param name="budget">What the look may still read, shared with the search.</param>
    /// <param name="cancellationToken">Stops the look when the request is gone.</param>
    /// <param name="part">Where to note what the look cost (entries, notes opened, time), for the log; nowhere when null.</param>
    /// <returns>
    /// What was found; null when the budget ran out before the look was done (a look that is only half is not given). A folder
    /// that cannot be read has no notes: its counts are 0.
    /// </returns>
    public static NoteFolderCheck? Scan(string folder, string realPath, bool sampleAlways, int maxEntries, ScanBudget budget, CancellationToken cancellationToken, SearchStats.ScanPart? part = null)
    {
        if (part is null)
        {
            return Look(folder, realPath, sampleAlways, maxEntries, budget, part: null, cancellationToken);
        }

        long started = Stopwatch.GetTimestamp();
        int taken = budget.Taken;
        int opened = part.NotesOpened;
        try
        {
            return Look(folder, realPath, sampleAlways, maxEntries, budget, part, cancellationToken);
        }
        finally
        {
            // What the sample took from the budget is the notes it opened, and is counted with them: the entries are what the look read of the folders.
            part.Add(Stopwatch.GetTimestamp() - started, budget.Taken - taken - (part.NotesOpened - opened), folder);
        }
    }

    private static NoteFolderCheck? Look(string folder, string realPath, bool sampleAlways, int maxEntries, ScanBudget budget, SearchStats.ScanPart? part, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        int files = 0;
        int entries = 0;
        bool stopped = false;
        var seen = new HashSet<string>(FolderWalk.PathComparer) { realPath };
        var queue = new Queue<Folder>();
        queue.Enqueue(new Folder(folder, realPath, Depth: 0));

        // Level by level, as the search is: what the limit of entries cuts off is what is deepest.
        while (!stopped && queue.TryDequeue(out Folder current))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (FileSystemInfo entry in FolderWalk.Entries(current.Path))
                {
                    // The entries of one folder are counted before the budget of the search is: what is not read is not spent.
                    if (++entries > maxEntries)
                    {
                        stopped = true;
                        break;
                    }

                    if (!budget.TrySpend())
                    {
                        return null;
                    }

                    if (entry is DirectoryInfo child)
                    {
                        if (current.Depth < NoteFolderCheck.Levels
                            && !FolderNames.IsLeftOutOfSearch(child)
                            && FolderWalk.RealPath(child, current.RealPath) is { } childRealPath
                            && seen.Add(childRealPath))
                        {
                            queue.Enqueue(new Folder(child.FullName, childRealPath, current.Depth + 1));
                        }
                    }
                    else if (!FolderNames.IsHidden(entry.Name))
                    {
                        files++;
                        if (entry.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        {
                            notes.Add(entry.FullName);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // What cannot be read is not counted; what was read of the folder stays.
            }
        }

        if (stopped && part is not null)
        {
            part.Capped++;
        }

        var counted = new NoteFolderCheck(notes.Count, files, Sampled: 0, Linked: 0);
        if (notes.Count == 0 || !(sampleAlways || counted.HasEnoughNotes))
        {
            return counted;
        }

        return Sample(notes, budget, part, cancellationToken) is { } sample ? counted with { Sampled = sample.Sampled, Linked = sample.Linked } : null;
    }

    /// <summary>
    /// The notes to read for the sample: at most <see cref="NoteFolderCheck.SampleSize"/> of them, every n-th one of the paths in order, so
    /// that they are spread over the folder. The order of a listing is the file system's; ordered paths make the sample the same one every
    /// time. Each one is taken from the middle of its stretch of notes, so that the sample leans neither to the start of the order nor to
    /// its end. All of the notes when there are no more than the sample.
    /// </summary>
    /// <param name="notes">The full paths of the notes; put in order by this.</param>
    internal static List<string> Choose(List<string> notes)
    {
        notes.Sort(StringComparer.Ordinal);
        int take = Math.Min(NoteFolderCheck.SampleSize, notes.Count);
        var chosen = new List<string>(take);
        for (int i = 0; i < take; i++)
        {
            chosen.Add(notes[(int)((2L * i + 1) * notes.Count / (2L * take))]);
        }

        return chosen;
    }

    // Reads the start of the chosen notes, and says how many could be read and how many of those have a wikilink; null when the budget ran
    // out before the sample was done.
    private static (int Sampled, int Linked)? Sample(List<string> notes, ScanBudget budget, SearchStats.ScanPart? part, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[NoteFolderCheck.SampleBytes];
        int sampled = 0;
        int linked = 0;
        foreach (string note in Choose(notes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!budget.TrySpend())
            {
                return null;
            }

            long opened = part is null ? 0 : Stopwatch.GetTimestamp();
            bool? hasWikilink = HasWikilink(note, buffer);
            part?.AddNote(Stopwatch.GetTimestamp() - opened, note);

            switch (hasWikilink)
            {
                case true:
                    sampled++;
                    linked++;
                    break;
                case false:
                    sampled++;
                    break;
            }
        }

        return (sampled, linked);
    }

    // Whether the start of a note has "[["; null when it cannot be read (it went away, it is not ours to read, it is a link to nothing): such a note is no
    // part of the sample.
    private static bool? HasWikilink(string path, byte[] buffer)
    {
        try
        {
            var note = new FileInfo(path);

            // A link that leads to a network location is not followed, not even to see whether there is a file (see LinkGuard): the note is no part of the sample.
            if (!LinkGuard.MayFollow(note))
            {
                return null;
            }

            // A link is judged by the file it leads to. The length of a link is its own, which says nothing about that: on Windows it is none, whatever the file
            // holds (so that a note that is a link was never read), and elsewhere it is the length of the path it holds, which is never none (so that a link to a
            // pipe was opened). A link that leads to nothing, or to a folder, leaves the note out of the sample.
            long length;
            if (note.LinkTarget is null)
            {
                length = note.Length;
            }
            else if (note.ResolveLinkTarget(returnFinalTarget: true) is FileInfo { Exists: true } target)
            {
                length = target.Length;
            }
            else
            {
                return null;
            }

            // A file with no length is empty, or none to read from (a pipe or a device, which opening would wait on for ever); so is what a link leads to.
            if (length == 0)
            {
                return false;
            }

            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int total = 0;
            while (total < buffer.Length)
            {
                int read = RandomAccess.Read(handle, buffer.AsSpan(total), total);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return buffer.AsSpan(0, total).IndexOf("[["u8) >= 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private readonly record struct Folder(string Path, string RealPath, int Depth);
}
