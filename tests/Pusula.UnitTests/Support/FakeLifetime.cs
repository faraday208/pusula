using Microsoft.Extensions.Hosting;

namespace Pusula.UnitTests.Support;

/// <summary>An <see cref="IHostApplicationLifetime"/> that is started when a test says so.</summary>
internal sealed class FakeLifetime : IHostApplicationLifetime, IDisposable
{
    private readonly CancellationTokenSource _started = new();

    public CancellationToken ApplicationStarted => _started.Token;

    public CancellationToken ApplicationStopping => CancellationToken.None;

    public CancellationToken ApplicationStopped => CancellationToken.None;

    /// <summary>The server has started.</summary>
    public void Start() => _started.Cancel();

    public void Dispose() => _started.Dispose();

    public void StopApplication()
    {
    }
}
