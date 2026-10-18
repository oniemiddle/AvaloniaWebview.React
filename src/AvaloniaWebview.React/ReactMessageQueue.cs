namespace AvaloniaWebview.React;

/// <summary>
/// Holds host-to-React messages that arrive before the page can execute JavaScript.
/// </summary>
/// <remarks>All members are expected to be used from the UI thread; the internal lock only protects
/// invariants if a caller violates that expectation while enqueueing.</remarks>
internal sealed class ReactMessageQueue
{
    private readonly Queue<QueuedMessage> _messages = new();
    private readonly Lock _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count;
            }
        }
    }

    /// <summary>Queues a script and returns a task completed when it is sent.</summary>
    public Task<string?> Enqueue(string script, int capacity)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (capacity <= 0)
        {
            throw new InvalidOperationException("Host-to-React messages cannot be queued because MaxQueuedBridgeMessages is not positive.");
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_messages.Count >= capacity)
            {
                throw new InvalidOperationException(
                    $"The host-to-React message queue is full ({capacity} messages). Wait for the page to load or increase MaxQueuedBridgeMessages.");
            }

            _messages.Enqueue(new QueuedMessage(script, completion));
        }

        return completion.Task;
    }

    /// <summary>Sends queued scripts in order, completing each caller's task.</summary>
    public async Task FlushAsync(Func<string, Task<string?>> send)
    {
        ArgumentNullException.ThrowIfNull(send);

        while (true)
        {
            QueuedMessage message;
            lock (_gate)
            {
                if (_messages.Count == 0)
                {
                    return;
                }

                message = _messages.Dequeue();
            }

            try
            {
                message.Completion.TrySetResult(await send(message.Script));
            }
            catch (Exception exception)
            {
                message.Completion.TrySetException(exception);
            }
        }
    }

    /// <summary>Fails every queued task, for example after a navigation failure.</summary>
    public void FailAll(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        QueuedMessage[] pending;
        lock (_gate)
        {
            if (_messages.Count == 0)
            {
                return;
            }

            pending = [.. _messages];
            _messages.Clear();
        }

        foreach (var message in pending)
        {
            message.Completion.TrySetException(exception);
        }
    }

    private sealed record QueuedMessage(string Script, TaskCompletionSource<string?> Completion);
}
