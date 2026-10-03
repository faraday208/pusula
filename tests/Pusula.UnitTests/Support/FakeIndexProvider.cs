using Pusula.Indexing;

namespace Pusula.UnitTests.Support;

/// <summary>An <see cref="IIndexProvider"/> that serves one fixed index and never announces a change.</summary>
internal sealed class FakeIndexProvider(ConfigIndex index) : IIndexProvider
{
    public ConfigIndex Current => index;

    public async IAsyncEnumerable<IndexChange> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        yield break;
    }
}
