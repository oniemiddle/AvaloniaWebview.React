namespace AvaloniaWebview.React.Tests;

public sealed class ReactMessageQueueTests
{
    [Fact]
    public async Task Flushes_scripts_in_order_and_completes_callers()
    {
        var queue = new ReactMessageQueue();
        var first = queue.Enqueue("first", capacity: 10);
        var second = queue.Enqueue("second", capacity: 10);
        var sent = new List<string>();

        await queue.FlushAsync(script =>
        {
            sent.Add(script);
            return Task.FromResult<string?>(script.ToUpperInvariant());
        });

        Assert.Equal(new[] { "first", "second" }, sent);
        Assert.Equal("FIRST", await first);
        Assert.Equal("SECOND", await second);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Keeps_messages_queued_until_flushed()
    {
        var queue = new ReactMessageQueue();
        var pending = queue.Enqueue("later", capacity: 10);

        Assert.Equal(1, queue.Count);
        Assert.False(pending.IsCompleted);

        await queue.FlushAsync(_ => Task.FromResult<string?>(null));

        Assert.True(pending.IsCompletedSuccessfully);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Fails_every_queued_caller_when_the_page_fails()
    {
        var queue = new ReactMessageQueue();
        var first = queue.Enqueue("first", capacity: 10);
        var second = queue.Enqueue("second", capacity: 10);

        queue.FailAll(new InvalidOperationException("navigation failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task Rejects_messages_beyond_the_capacity()
    {
        var queue = new ReactMessageQueue();
        var first = queue.Enqueue("first", capacity: 1);

        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = queue.Enqueue("second", capacity: 1);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            queue.FailAll(new InvalidOperationException("done"));
            await first;
        });
    }

    [Fact]
    public async Task Flush_with_no_messages_is_a_noop()
    {
        var queue = new ReactMessageQueue();
        var calls = 0;

        await queue.FlushAsync(_ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Send_failures_complete_only_the_affected_caller()
    {
        var queue = new ReactMessageQueue();
        var failing = queue.Enqueue("boom", capacity: 10);
        var succeeding = queue.Enqueue("ok", capacity: 10);

        await queue.FlushAsync(script => script == "boom"
            ? Task.FromException<string?>(new InvalidOperationException("send failed"))
            : Task.FromResult<string?>("sent"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing);
        Assert.Equal("sent", await succeeding);
    }
}
