#if DEBUG
using CasCap.Constants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace CasCap.Tests.Integration;

/// <summary>Verifies relational admission state; SQLite does not validate PostgreSQL locks or races.</summary>
[Trait("Category", "Ingestion")]
[Trait("Category", "Integration")]
public sealed class DocumentIngestionInboxTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private Factory _factory = null!;
    private readonly ManualClock _clock = new();

    public async ValueTask InitializeAsync()
    {
        await _connection.OpenAsync(TestContext.Current.CancellationToken);
        _factory = new Factory(new DbContextOptionsBuilder<AgentRuntimeDbContext>().UseSqlite(_connection).Options);
        await using var db = _factory.CreateDbContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    [Fact]
    public async Task DuplicateDeliveryCreatesOneJob()
    {
        var inbox = Create();
        var notification = DocumentIngestionTestData.Notification();
        await inbox.AcceptAsync(notification, TestContext.Current.CancellationToken);
        await inbox.AcceptAsync(notification, TestContext.Current.CancellationToken);
        await using var db = _factory.CreateDbContext();
        Assert.Single(await db.DocumentIngestionJobs.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(DocumentIngestionStates.Queued,
            (await db.KnowledgeDocuments.SingleAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task EventIdentityCannotBeReused()
    {
        var inbox = Create();
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification(), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<JsonException>(() =>
            inbox.AcceptAsync(DocumentIngestionTestData.Notification(sequencer: "2"), TestContext.Current.CancellationToken));
        await using var db = _factory.CreateDbContext();
        Assert.Single(await db.DocumentIngestionJobs.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OlderNotificationCannotReplaceNewerState()
    {
        var inbox = Create();
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("new", "10"), TestContext.Current.CancellationToken);
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("old", "f"), TestContext.Current.CancellationToken);
        await using var db = _factory.CreateDbContext();
        var document = await db.KnowledgeDocuments.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("10".PadLeft(128, '0'), document.Sequencer);
        Assert.Equal(1, await db.DocumentIngestionJobs.CountAsync(job => job.Status == DocumentIngestionStates.Ignored,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteSupersedesJobsAndPreventsResurrection()
    {
        var inbox = Create();
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("create", "1"), TestContext.Current.CancellationToken);
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("delete", "2", deleted: true), TestContext.Current.CancellationToken);
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("overwrite", "3"), TestContext.Current.CancellationToken);
        await using var db = _factory.CreateDbContext();
        Assert.Equal(DocumentIngestionStates.Deleted,
            (await db.KnowledgeDocuments.SingleAsync(TestContext.Current.CancellationToken)).Status);
        Assert.Equal(0, await db.DocumentIngestionJobs.CountAsync(job => job.Status == DocumentIngestionStates.Queued,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CollectionsAndTenantsHaveSeparateDocuments()
    {
        var inbox = Create();
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("a"), TestContext.Current.CancellationToken);
        await inbox.AcceptAsync(DocumentIngestionTestData.Notification("b", tenant: "tenant-b"), TestContext.Current.CancellationToken);
        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, await db.KnowledgeDocuments.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.KnowledgeDocuments.CountAsync(document => document.TenantId == "tenant-a",
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisabledProcessingLeavesJobsUntouched()
    {
        await AcceptAsync();
        var options = Options.Create(DocumentIngestionTestData.Config);
        var jobs = new DocumentIngestionJobService(options, _clock, _factory);
        var processing = new DocumentIngestionProcessingService(NullLogger<DocumentIngestionProcessingService>.Instance,
            options, _clock, jobs, []);
        Assert.Null(await jobs.ClaimAsync(TestContext.Current.CancellationToken));
        Assert.False(await processing.ProcessOneAsync(TestContext.Current.CancellationToken));
        var job = await JobAsync();
        Assert.Equal(DocumentIngestionStates.Queued, job.Status);
        Assert.Equal(0, job.AttemptCount);
    }

    [Fact]
    public async Task MissingProcessorFailsBeforeClaim()
    {
        await AcceptAsync();
        var processing = Processing([]);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processing.ProcessOneAsync(TestContext.Current.CancellationToken));
        Assert.Contains("real registered document processor", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, (await JobAsync()).AttemptCount);
    }

    [Theory]
    [InlineData(false, DocumentIngestionStates.Completed)]
    [InlineData(true, DocumentIngestionStates.Failed)]
    public async Task ProcessingOutcome(bool permanentFailure, string expectedStatus)
    {
        await AcceptAsync();
        var outcome = permanentFailure ? DocumentProcessingOutcome.PermanentFailure : DocumentProcessingOutcome.Completed;
        var processor = new Processor((_, _) => Task.FromResult(outcome));
        Assert.True(await Processing([processor]).ProcessOneAsync(TestContext.Current.CancellationToken));
        var job = await JobAsync();
        Assert.Equal(expectedStatus, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.NotNull(job.FinishedAtUtc);
        Assert.Null(job.ClaimId);
        Assert.Null(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
        await using var db = _factory.CreateDbContext();
        Assert.Equal(DocumentIngestionStates.Queued,
            (await db.KnowledgeDocuments.SingleAsync(TestContext.Current.CancellationToken)).Status);
    }

    [Fact]
    public async Task RetryDelayAndAttemptLimit()
    {
        await AcceptAsync();
        var processing = Processing([new Processor((_, _) => Task.FromResult(DocumentProcessingOutcome.RetryableFailure))]);
        for (var attempt = 1; attempt <= DocumentProcessingPolicy.MaxAttempts; attempt++)
        {
            Assert.True(await processing.ProcessOneAsync(TestContext.Current.CancellationToken));
            var job = await JobAsync();
            Assert.Equal(attempt, job.AttemptCount);
            Assert.Equal(DocumentProcessingPolicy.RetryableFailure, job.LastFailureCode);
            if (attempt < DocumentProcessingPolicy.MaxAttempts)
            {
                Assert.Equal(DocumentIngestionStates.RetryPending, job.Status);
                Assert.Equal(_clock.GetUtcNow().AddSeconds(30), job.NextAttemptAtUtc);
                _clock.Advance(TimeSpan.FromSeconds(29));
                Assert.False(await processing.ProcessOneAsync(TestContext.Current.CancellationToken));
                _clock.Advance(TimeSpan.FromSeconds(1));
            }
        }
        Assert.Equal(DocumentIngestionStates.Failed, (await JobAsync()).Status);
        _clock.Advance(TimeSpan.FromDays(1));
        Assert.False(await processing.ProcessOneAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExpiredClaimRecoveryRejectsLateCompletion()
    {
        await AcceptAsync();
        var jobs = Jobs();
        var first = Assert.IsType<DocumentIngestionClaim>(await jobs.ClaimAsync(TestContext.Current.CancellationToken));
        Assert.Null(await jobs.ClaimAsync(TestContext.Current.CancellationToken));
        _clock.Advance(TimeSpan.FromSeconds(300));
        Assert.False(await jobs.FinishAsync(first, DocumentProcessingOutcome.Completed, TestContext.Current.CancellationToken));
        Assert.Null(await jobs.ClaimAsync(TestContext.Current.CancellationToken));
        Assert.Equal(DocumentProcessingPolicy.DeadlineExpired, (await JobAsync()).LastFailureCode);
        _clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Null(await jobs.ClaimAsync(TestContext.Current.CancellationToken));
        _clock.Advance(TimeSpan.FromSeconds(1));
        var second = Assert.IsType<DocumentIngestionClaim>(await jobs.ClaimAsync(TestContext.Current.CancellationToken));
        Assert.NotEqual(first.ClaimId, second.ClaimId);
        Assert.False(await jobs.FinishAsync(first, DocumentProcessingOutcome.Completed, TestContext.Current.CancellationToken));
        Assert.True(await jobs.FinishAsync(second, DocumentProcessingOutcome.Completed, TestContext.Current.CancellationToken));
        Assert.False(await jobs.FinishAsync(second, DocumentProcessingOutcome.Completed, TestContext.Current.CancellationToken));
        Assert.Equal(2, (await JobAsync()).AttemptCount);
    }

    [Fact]
    public async Task AbandonedAttemptsAreBounded()
    {
        await AcceptAsync();
        var jobs = Jobs();
        for (var attempt = 1; attempt <= DocumentProcessingPolicy.MaxAttempts; attempt++)
        {
            Assert.NotNull(await jobs.ClaimAsync(TestContext.Current.CancellationToken));
            _clock.Advance(TimeSpan.FromSeconds(300));
            Assert.Null(await jobs.ClaimAsync(TestContext.Current.CancellationToken));
            _clock.Advance(TimeSpan.FromSeconds(30));
        }
        var job = await JobAsync();
        Assert.Equal(DocumentIngestionStates.Failed, job.Status);
        Assert.Equal(3, job.AttemptCount);
        Assert.NotNull(job.FinishedAtUtc);
        Assert.Null(await jobs.ClaimAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task SupersessionRevokesActiveAndRetryJobs(bool deleted, bool retry)
    {
        await AcceptAsync();
        var jobs = Jobs();
        var claim = Assert.IsType<DocumentIngestionClaim>(await jobs.ClaimAsync(TestContext.Current.CancellationToken));
        if (retry)
            Assert.True(await jobs.FinishAsync(claim, DocumentProcessingOutcome.RetryableFailure, TestContext.Current.CancellationToken));
        await Create().AcceptAsync(DocumentIngestionTestData.Notification("new", "2", deleted: deleted),
            TestContext.Current.CancellationToken);
        Assert.False(await jobs.FinishAsync(claim, DocumentProcessingOutcome.Completed, TestContext.Current.CancellationToken));
        await using var db = _factory.CreateDbContext();
        var old = await db.DocumentIngestionJobs.SingleAsync(job => job.Id == claim.JobId, TestContext.Current.CancellationToken);
        Assert.Equal(DocumentIngestionStates.Ignored, old.Status);
        Assert.Null(old.ClaimId);
        Assert.Null(old.NextAttemptAtUtc);
        var replacement = await jobs.ClaimAsync(TestContext.Current.CancellationToken);
        Assert.Equal(deleted, replacement is null);
        if (replacement is not null)
            Assert.NotEqual(claim.JobId, replacement.JobId);
    }

    [Fact]
    public async Task CancellationLeavesClaimRecoverable()
    {
        await AcceptAsync();
        using var owner = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var processor = new BlockingProcessor();
        var task = Processing([processor]).ProcessOneAsync(owner.Token);
        await processor.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        await owner.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(processor.Stopped);
        Assert.Equal(DocumentIngestionStates.Processing, (await JobAsync()).Status);
        Assert.Null((await JobAsync()).LastFailureCode);
        _clock.Advance(TimeSpan.FromSeconds(300));
        Assert.Null(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.NotNull(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeadlineCancelsAndAwaitsProcessor()
    {
        await AcceptAsync();
        var processor = new BlockingProcessor();
        var task = Processing([processor]).ProcessOneAsync(TestContext.Current.CancellationToken);
        await processor.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromSeconds(300));
        Assert.True(await task);
        Assert.True(processor.Stopped);
        Assert.Equal(DocumentIngestionStates.Processing, (await JobAsync()).Status);
        Assert.Null(await Jobs().ClaimAsync(TestContext.Current.CancellationToken));
        Assert.Equal(DocumentIngestionStates.RetryPending, (await JobAsync()).Status);
    }

    [Fact]
    public async Task UnknownFaultPropagates()
    {
        await AcceptAsync();
        var processing = Processing([new Processor((_, _) => throw new InvalidOperationException("Synthetic fault."))]);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processing.ProcessOneAsync(TestContext.Current.CancellationToken));
        Assert.Equal(DocumentIngestionStates.Processing, (await JobAsync()).Status);
        Assert.Null((await JobAsync()).LastFailureCode);
    }

    private DocumentIngestionInboxService Create() => new(_clock, _factory);

    private Task AcceptAsync() => Create().AcceptAsync(DocumentIngestionTestData.Notification(), TestContext.Current.CancellationToken);

    private DocumentIngestionJobService Jobs() => new(ProcessingOptions(), _clock, _factory);

    private static IOptions<DocumentIngestionConfig> ProcessingOptions() =>
        Options.Create(DocumentIngestionTestData.Config with { ProcessingEnabled = true });

    private DocumentIngestionProcessingService Processing(IEnumerable<IDocumentProcessor> processors) =>
        new(NullLogger<DocumentIngestionProcessingService>.Instance, ProcessingOptions(), _clock, Jobs(), processors);

    private async Task<Entities.DocumentIngestionJobEntity> JobAsync()
    {
        await using var db = _factory.CreateDbContext();
        return await db.DocumentIngestionJobs.SingleAsync(TestContext.Current.CancellationToken);
    }

    private sealed class Processor(
        Func<DocumentIngestionClaim, CancellationToken, Task<DocumentProcessingOutcome>> process) : IDocumentProcessor
    {
        public Task<DocumentProcessingOutcome> ProcessAsync(DocumentIngestionClaim claim, CancellationToken cancellationToken) =>
            process(claim, cancellationToken);
    }

    private sealed class BlockingProcessor : IDocumentProcessor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Stopped { get; private set; }
        public async Task<DocumentProcessingOutcome> ProcessAsync(DocumentIngestionClaim claim, CancellationToken cancellationToken)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return DocumentProcessingOutcome.Completed;
            }
            finally
            {
                Stopped = true;
            }
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> _timers = [];
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed)
        {
            _now += elapsed;
            foreach (var timer in _timers.ToArray())
                timer.Fire(_now);
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? _due;
            private TimeSpan _period;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetUtcNow() + dueTime;
                _period = period;
                return true;
            }
            public void Fire(DateTimeOffset now)
            {
                if (_due is null || _due > now)
                    return;
                _due = _period == Timeout.InfiniteTimeSpan ? null : now + _period;
                callback(state);
            }
            public void Dispose() => _due = null;
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class Factory(DbContextOptions<AgentRuntimeDbContext> options) : IDbContextFactory<AgentRuntimeDbContext>
    {
        public AgentRuntimeDbContext CreateDbContext() => new(options);
    }
}
#endif
