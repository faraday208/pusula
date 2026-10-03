namespace Pusula.Indexing;

/// <summary>Serves the current index and announces changes to it.</summary>
internal interface IIndexProvider
{
    /// <summary>The latest index.</summary>
    ConfigIndex Current { get; }

    /// <summary>Yields a change every time the index changes, until <paramref name="cancellationToken"/> is cancelled.</summary>
    IAsyncEnumerable<IndexChange> WatchAsync(CancellationToken cancellationToken);
}
