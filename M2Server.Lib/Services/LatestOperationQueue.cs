namespace M2Server.Lib.Services;

public readonly record struct QueuedOperationResult<T>(bool Superseded, T? Value);

/// <summary>Serializes operations while retaining only the latest pending request.</summary>
public sealed class LatestOperationQueue<T>
{
    private readonly object _gate = new();
    private Request? _active;
    private CancellationTokenSource? _activeCancellation;
    private Request? _pending;
    private bool _running;

    public Task<QueuedOperationResult<T>> EnqueueAsync(Func<CancellationToken, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var request = new Request(operation);
        bool start;
        lock (this._gate)
        {
            this._pending?.Completion.TrySetResult(new QueuedOperationResult<T>(true, default));
            this._pending = request;
            if (this._active is not null)
            {
                this._active.Superseded = true;
                _ = this._activeCancellation!.CancelAsync();
            }

            start = !this._running;
            this._running = true;
        }

        if (start)
        {
            _ = this.RunAsync();
        }

        return request.Completion.Task;
    }

    private async Task RunAsync()
    {
        while (true)
        {
            Request request;
            CancellationTokenSource cancellation;
            lock (this._gate)
            {
                if (this._pending is null)
                {
                    this._running = false;
                    return;
                }

                request = this._pending;
                this._pending = null;
                this._active = request;
                cancellation = new CancellationTokenSource();
                this._activeCancellation = cancellation;
            }

            try
            {
                var value = await request.Operation(cancellation.Token).ConfigureAwait(false);
                lock (this._gate)
                {
                    request.Completion.TrySetResult(
                        request.Superseded
                            ? new QueuedOperationResult<T>(true, default)
                            : new QueuedOperationResult<T>(false, value)
                    );
                }
            }
            catch (Exception error)
            {
                lock (this._gate)
                {
                    if (request.Superseded)
                    {
                        request.Completion.TrySetResult(new QueuedOperationResult<T>(true, default));
                    }
                    else
                    {
                        request.Completion.TrySetException(error);
                    }
                }
            }
            finally
            {
                lock (this._gate)
                {
                    this._active = null;
                    this._activeCancellation = null;
                }

                cancellation.Dispose();
            }
        }
    }

    private sealed record Request(Func<CancellationToken, Task<T>> Operation)
    {
        public bool Superseded { get; set; }

        public TaskCompletionSource<QueuedOperationResult<T>> Completion { get; }
            = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}