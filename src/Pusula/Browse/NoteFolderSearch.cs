namespace Pusula.Browse;

/// <summary>
/// The second part of the search for folders: of the folders that the walk over the disk found an entry note in (the
/// candidates), which are folders of notes, <see cref="FolderKind.Notes"/>. It has three steps, the cheap one before the
/// costly one. The first looks into the candidates one after the other, the shallowest first (the order the walk met them
/// in): <see cref="NoteFolderScanner"/> counts the notes below a candidate, and only a candidate that has notes enough has a
/// sample of them read; it is a folder of notes when <see cref="NoteFolderCheck.IsFolderOfNotes"/> says so. The second does
/// the same for the folders at the last level of the walk, which the walk did not read the entries of and so could not see an
/// entry note in: each is asked for one (<see cref="NoteFolderScanner.HasEntryNote"/>, which reads its first
/// <see cref="NoteFolderCheck.ProbeEntries"/> entries at most, each taken from the budget) and one that has one is looked into like
/// any candidate. They come after the others (they are the deepest) and with what is left of the budget, so the walk keeps its
/// whole reach; the ones inside a folder of notes come first, for whether one of them is a folder of notes decides whether the folder around
/// it is shown. Only the folders below a root that asks its last level are among them (<see cref="SearchRoot.ProbesLastLevel"/>: the home
/// directory and, on Windows, the system drive; the other drives may sleep or be slow, and opening a folder for each of them would hold the search
/// back): with these a folder of notes is found as deep as a vault is, and below the other roots one level less deep. The third settles the
/// folders that lie inside one another: a folder of notes that has a folder inside it (a
/// vault, or another folder of notes) that holds at least <see cref="NoteFolderCheck.InnerPercent"/> percent of its wikilinked
/// notes (<see cref="NoteFolderCheck.Holds"/>) is not shown, the one inside is: a project folder with a README and a vault in it
/// is the vault, not the project. A vault is never left out, and what a vault holds is looked into only when it lies inside a
/// folder of notes. Everything read comes from the budget of the search; a candidate that the budget did not reach is not shown,
/// and neither is a folder of notes whose nesting the budget did not let be settled (a vault or a candidate inside it was not
/// looked into): such a folder cannot be told from the parent of a vault. A folder of the last level that was not asked is not
/// known to be a candidate, and holds nothing back. Which folder lies inside which is told in one pass over the paths, each looked up by
/// the folders above it, and not by asking every folder about every other: the work is that of the folders, which the budget does not count.
/// The folders of notes that are known are reported when the candidates have been looked into, before the folders of the last level are asked: what is
/// reported is what the search would give if it ended there, so that a folder with a vault or a candidate inside it that was not looked into is not among them.
/// </summary>
internal static class NoteFolderSearch
{
    /// <summary>Finds the folders of notes among what the walk came across.</summary>
    /// <param name="walked">What the walk found: the candidates, the folders at its last level, and the vaults.</param>
    /// <param name="limits">How much one folder may be read.</param>
    /// <param name="budget">What the search may still read; this is where the look into the candidates takes from.</param>
    /// <param name="stats">Where what each stage of the search cost is noted, for the log.</param>
    /// <param name="progress">Told the folders of notes that are known, as the search would give them if it ended now, after the candidates and before the folders of the last level are asked; nothing is read for it.</param>
    /// <param name="cancellationToken">Stops the search when the request is gone.</param>
    /// <returns>The full paths of the folders of notes, in no particular order.</returns>
    public static IReadOnlyList<string> Find(
        FolderFinder.WalkFindings walked,
        BrowseLimits limits,
        ScanBudget budget,
        SearchStats stats,
        Action<IReadOnlyList<string>>? progress,
        CancellationToken cancellationToken)
    {
        var qualified = new List<Qualified>();
        var unreached = new List<string>();

        foreach (FolderFinder.Level candidate in walked.Candidates)
        {
            LookInto(candidate);
        }

        stats.NoteBudget(budget, "the looking into the candidates");
        progress?.Invoke(Shown(lookIntoVaults: false));

        foreach (FolderFinder.Level folder in InsideFirst(walked.Deepest, qualified))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (budget.IsSpent)
            {
                break;
            }

            if (NoteFolderScanner.HasEntryNote(folder.Path, budget, cancellationToken, stats.Probes) == true)
            {
                stats.ProbesFound++;
                LookInto(folder);
            }
        }

        stats.NoteBudget(budget, "the asking of the folders of the last level");

        List<string> shown = Shown(lookIntoVaults: true);
        stats.FoldersOfNotes = qualified.Count;
        stats.Unreached = unreached.Count;
        stats.NoteBudget(budget, "the settling of the folders that lie inside one another");
        return shown;

        // The folders of notes that are not given way to by what lies inside them. A vault inside one is looked into only when asked to (that reads the
        // vault, and takes from the budget); otherwise it counts as not looked into, which keeps the folder around it out, as a budget that ran out would.
        List<string> Shown(bool lookIntoVaults)
        {
            var nesting = new Nesting(qualified, unreached, walked.Vaults, limits, budget, cancellationToken);
            return [.. qualified.Where(outer => !nesting.IsGivenWayTo(outer, lookIntoVaults)).Select(outer => outer.Path)];
        }

        // A candidate that the budget does not reach is kept apart: it may be what a folder around it holds its notes in.
        void LookInto(FolderFinder.Level candidate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            NoteFolderCheck? check = budget.IsSpent
                ? null
                : NoteFolderScanner.Scan(candidate.Path, candidate.RealPath, sampleAlways: false, limits.CheckEntriesPerFolder, budget, cancellationToken, stats.Scans);
            if (check is null)
            {
                unreached.Add(candidate.Path);
            }
            else if (check.Value.IsFolderOfNotes)
            {
                qualified.Add(new Qualified(candidate.Path, check.Value));
            }
        }
    }

    // The folders of the last level, the ones inside a folder of notes first (each group in the order the walk met them): what is asked first is
    // what the budget reaches. Each is looked up once by the folders above it, however many folders of notes there are.
    private static List<FolderFinder.Level> InsideFirst(List<FolderFinder.Level> folders, List<Qualified> qualified)
    {
        if (qualified.Count == 0)
        {
            return folders;
        }

        var outers = new HashSet<string>(FolderWalk.PathComparer);
        foreach (Qualified outer in qualified)
        {
            outers.Add(outer.Path);
        }

        HashSet<string>.AlternateLookup<ReadOnlySpan<char>> lookup = outers.GetAlternateLookup<ReadOnlySpan<char>>();
        var inside = new List<FolderFinder.Level>();
        var elsewhere = new List<FolderFinder.Level>();
        foreach (FolderFinder.Level folder in folders)
        {
            bool isInside = false;
            for (ReadOnlySpan<char> above = Path.GetDirectoryName(folder.Path.AsSpan()); !isInside && !above.IsEmpty; above = Path.GetDirectoryName(above))
            {
                isInside = lookup.Contains(above);
            }

            (isInside ? inside : elsewhere).Add(folder);
        }

        return [.. inside, .. elsewhere];
    }

    private sealed record Qualified(string Path, NoteFolderCheck Check);

    // What the third step knows: the folders of notes, the candidates it did not get to, the vaults, and what has been looked into of the vaults so far.
    private sealed class Nesting
    {
        private readonly Dictionary<string, Inside> _inside = new(FolderWalk.PathComparer);
        private readonly Dictionary<string, NoteFolderCheck?> _vaultChecks = new(FolderWalk.PathComparer);
        private readonly BrowseLimits _limits;
        private readonly ScanBudget _budget;
        private readonly CancellationToken _cancellationToken;

        // What lies inside each folder of notes is found here, once: each folder of notes, each candidate that was not reached and each vault is
        // looked up by the folders above it (a few steps up for each), so that the work is that of the folders and not of every pair of them.
        public Nesting(
            List<Qualified> qualified,
            List<string> unreached,
            IReadOnlyList<string> vaults,
            BrowseLimits limits,
            ScanBudget budget,
            CancellationToken cancellationToken)
        {
            _limits = limits;
            _budget = budget;
            _cancellationToken = cancellationToken;

            foreach (Qualified folder in qualified)
            {
                _inside[folder.Path] = new Inside();
            }

            Dictionary<string, Inside>.AlternateLookup<ReadOnlySpan<char>> lookup = _inside.GetAlternateLookup<ReadOnlySpan<char>>();
            foreach (Qualified inner in qualified)
            {
                for (ReadOnlySpan<char> above = Path.GetDirectoryName(inner.Path.AsSpan()); !above.IsEmpty; above = Path.GetDirectoryName(above))
                {
                    if (lookup.TryGetValue(above, out Inside? around))
                    {
                        around.Folders.Add(inner);
                    }
                }
            }

            foreach (string path in unreached)
            {
                for (ReadOnlySpan<char> above = Path.GetDirectoryName(path.AsSpan()); !above.IsEmpty; above = Path.GetDirectoryName(above))
                {
                    if (lookup.TryGetValue(above, out Inside? around))
                    {
                        around.HasUnreached = true;
                    }
                }
            }

            foreach (string vault in vaults)
            {
                for (ReadOnlySpan<char> above = Path.GetDirectoryName(vault.AsSpan()); !above.IsEmpty; above = Path.GetDirectoryName(above))
                {
                    if (lookup.TryGetValue(above, out Inside? around))
                    {
                        around.Vaults.Add(vault);
                    }
                }
            }
        }

        // Whether the folder is not shown because of what lies inside it. What costs nothing is asked first: the folders of notes inside it,
        // then the candidates that were not looked into; a vault is looked into last (and not at all, and so taken as not looked into, when
        // lookIntoVaults is false).
        public bool IsGivenWayTo(Qualified outer, bool lookIntoVaults)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            Inside inside = _inside[outer.Path];

            if (inside.Folders.Exists(inner => NoteFolderCheck.Holds(inner.Check, outer.Check)))
            {
                return true;
            }

            // A candidate inside that the budget did not reach might be the folder that holds the notes: it is not known.
            if (inside.HasUnreached)
            {
                return true;
            }

            foreach (string vault in inside.Vaults)
            {
                // A vault that could not be looked into (the budget ran out) is as unknown as a candidate that was not reached.
                if (!lookIntoVaults || LookIntoVault(vault) is not { } held || NoteFolderCheck.Holds(held, outer.Check))
                {
                    return true;
                }
            }

            return false;
        }

        private NoteFolderCheck? LookIntoVault(string vault)
        {
            if (_vaultChecks.TryGetValue(vault, out NoteFolderCheck? known))
            {
                return known;
            }

            NoteFolderCheck? check = _budget.IsSpent || FolderWalk.RealPath(new DirectoryInfo(vault), parentRealPath: null) is not { } realPath
                ? null
                : NoteFolderScanner.Scan(vault, realPath, sampleAlways: true, _limits.CheckEntriesPerFolder, _budget, _cancellationToken);
            _vaultChecks[vault] = check;
            return check;
        }

        // What lies inside one folder of notes.
        private sealed class Inside
        {
            public List<Qualified> Folders { get; } = [];

            public bool HasUnreached { get; set; }

            public List<string> Vaults { get; } = [];
        }
    }
}
