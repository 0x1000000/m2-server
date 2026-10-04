using M2Server.Lib.Services;
using NUnit.Framework;

namespace M2Server.Lib.UnitTests;

[TestFixture]
public sealed class LatestOperationQueueTests
{
    [Test]
    public async Task LatestPendingRequestReplacesEarlierOneWithoutOverlappingActiveWork()
    {
        var queue = new LatestOperationQueue<int>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executed = new List<int>();
        var activeCount = 0;
        var maximumActive = 0;

        async Task<int> Run(int value, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref activeCount);
            maximumActive = Math.Max(maximumActive, active);
            try
            {
                executed.Add(value);
                if (value == 1)
                {
                    entered.SetResult();
                    await release.Task;
                }

                return value;
            }
            finally
            {
                Interlocked.Decrement(ref activeCount);
            }
        }

        var first = queue.EnqueueAsync(token => Run(1, token));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var replaced = queue.EnqueueAsync(token => Run(2, token));
        var latest = queue.EnqueueAsync(token => Run(3, token));

        Assert.That(await replaced.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(new QueuedOperationResult<int>(true, default)));
        Assert.That(executed, Is.EqualTo(new[] { 1 }));
        release.SetResult();

        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(5));
        var lastResult = await latest.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
            {
                Assert.That(firstResult.Superseded, Is.True);
                Assert.That(lastResult, Is.EqualTo(new QueuedOperationResult<int>(false, 3)));
                Assert.That(executed, Is.EqualTo(new[] { 1, 3 }));
                Assert.That(maximumActive, Is.EqualTo(1));
            }
        );
    }

    [Test]
    public async Task ActiveDelegateReceivesCancellationSignalAndCanFinish()
    {
        var queue = new LatestOperationQueue<int>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken activeToken = default;
        var first = queue.EnqueueAsync(async token =>
            {
                activeToken = token;
                entered.SetResult();
                await release.Task;
                return 1;
            }
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var next = queue.EnqueueAsync(_ => Task.FromResult(2));
        Assert.That(activeToken.IsCancellationRequested, Is.True);
        release.SetResult();
        Assert.That((await first.WaitAsync(TimeSpan.FromSeconds(5))).Superseded, Is.True);
        Assert.That((await next.WaitAsync(TimeSpan.FromSeconds(5))).Value, Is.EqualTo(2));
    }

    [Test]
    public void DelegateFailureIsReturnedToItsCaller()
    {
        var queue = new LatestOperationQueue<int>();
        Assert.ThrowsAsync<InvalidOperationException>(async ()
            => await queue.EnqueueAsync(_ => throw new InvalidOperationException("failed"))
        );
    }
}