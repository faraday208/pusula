namespace Pusula.LiveReload;

/// <summary>
/// A lock for asynchronous code (one caller at a time) that can be closed for good while callers still hold it or wait
/// for it. A <see cref="SemaphoreSlim"/> cannot be closed like that: disposed while a caller is inside, it throws at
/// that caller when the caller gives it back, and it never wakes a caller that waits for it. The gate counts its callers
/// instead, from before they ask for the semaphore until after they gave it back. After <see cref="Close"/> nobody new
/// gets in, a caller that was waiting is turned away when its turn comes, and the one that is inside is left to finish.
/// When the last of them is out, the cleanup that <see cref="Close"/> was given runs, and only then is the semaphore
/// disposed. So the cleanup never takes down what a caller is using, and the semaphore is never touched after it was
/// disposed, however the gate is closed and used, and however often and at the same time.
/// </summary>
internal sealed class ClosableGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Lock _sync = new();
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // The callers that are inside or waiting to be inside.
    private int _callers;
    private bool _closed;
    private Action? _cleanup;

    /// <summary>
    /// Waits for the gate. The caller must give it back with <see cref="Exit"/> when this returns true, and must not
    /// when it returns false or throws.
    /// </summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>True when the caller is inside; false when the gate is closed, which it was before the call or while the caller waited.</returns>
    public async ValueTask<bool> TryEnterAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_closed)
            {
                return false;
            }

            _callers++;
        }

        try
        {
            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Leave();
            throw;
        }

        if (IsClosed)
        {
            Exit();
            return false;
        }

        return true;
    }

    /// <summary>Gives the gate back to the next caller.</summary>
    public void Exit()
    {
        _semaphore.Release();
        Leave();
    }

    /// <summary>
    /// Closes the gate: nobody gets in from now on, and <paramref name="cleanup"/> runs once the last caller that is
    /// inside or waiting is out (at once when there is none), on the thread of whoever that is. Only the first call
    /// gives the cleanup; a later call, or one at the same time, only waits.
    /// </summary>
    /// <param name="cleanup">What to take down. Nobody is in the gate while it runs, and it must not throw.</param>
    /// <param name="timeout">How long to wait for the cleanup to have run. The cleanup runs later all the same when this is too short.</param>
    /// <returns>True when the cleanup has run; false when the callers are not out yet after <paramref name="timeout"/>.</returns>
    public bool Close(Action cleanup, TimeSpan timeout)
    {
        bool idle = false;
        lock (_sync)
        {
            if (!_closed)
            {
                _closed = true;
                _cleanup = cleanup;
                idle = _callers == 0;
            }
        }

        if (idle)
        {
            Finish();
        }

        return _finished.Task.Wait(timeout);
    }

    /// <summary>Closes the gate when there is nothing to take down (see <see cref="Close"/>), without waiting for anyone to leave.</summary>
    public void Dispose() => Close(static () => { }, TimeSpan.Zero);

    private bool IsClosed
    {
        get
        {
            lock (_sync)
            {
                return _closed;
            }
        }
    }

    private void Leave()
    {
        bool last;
        lock (_sync)
        {
            _callers--;
            last = _closed && _callers == 0;
        }

        if (last)
        {
            Finish();
        }
    }

    // Once: either Close finds nobody in the gate, or the last caller leaves after Close. Nobody is in the gate and nobody
    // can come in, so the semaphore is not in use.
    private void Finish()
    {
        try
        {
            _cleanup!();
        }
        finally
        {
            _semaphore.Dispose();
            _finished.SetResult();
        }
    }
}
