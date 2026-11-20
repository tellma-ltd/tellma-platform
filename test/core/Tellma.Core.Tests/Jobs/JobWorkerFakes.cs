// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Time.Testing;
using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Composition;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>The worker tests' handlers: two of the job tests' own, and one that runs while the tenant is read-only.</summary>
    internal static class WorkerTestModel
    {
        /// <summary>The key of the handler that runs while the tenant is read-only.</summary>
        public const string AuditKey = "acme.audit";

        /// <summary>The key of an arguments-only handler that stops while the tenant is read-only.</summary>
        public const string RecomputeKey = "acme.recompute-balances";

        /// <summary>Adds the worker tests' feature, named <c>acme</c>.</summary>
        /// <param name="tellma">The composition's builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.D>("acme", contribute: static contribution => contribution
                .JobHandler<RecomputeHandler>()
                .JobHandler<AuditHandler>()));
        }
    }

    /// <summary>A handler that runs while the tenant is read-only, two rows a batch and two batches at once.</summary>
    [JobHandler(WorkerTestModel.AuditKey, RunsWhenReadOnly = true, BatchSize = 2, MaxConcurrency = 2)]
    public sealed class AuditHandler : IJobHandler
    {
        /// <inheritdoc />
        public Task ExecuteAsync(JobBatch<Job> batch)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>A fake clock that signals each timer created on it, so a test advances it only once a wait began.</summary>
    internal sealed class SignallingTimeProvider : FakeTimeProvider, IDisposable
    {
        /// <summary>Released once per timer created.</summary>
        private readonly SemaphoreSlim _created = new(0);

        /// <summary>Creates the clock at a fixed instant.</summary>
        public SignallingTimeProvider()
            : base(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero))
        {
        }

        /// <inheritdoc />
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer timer = base.CreateTimer(callback, state, dueTime, period);
            _ = _created.Release();
            return timer;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _created.Dispose();
        }

        /// <summary>Waits for the next timer created.</summary>
        /// <returns>A task that completes once one was.</returns>
        public async Task TimerCreatedAsync()
        {
            Assert.True(await _created.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), "No timer was created.");
        }
    }

    /// <summary>A poll that records each plan and answers it as the test says, no database involved.</summary>
    internal sealed class FakeJobPoll : IJobPoll, IDisposable
    {
        /// <summary>Released once per poll begun.</summary>
        private readonly SemaphoreSlim _polled = new(0);

        /// <summary>The polls begun, so each concurrent poll draws a number of its own.</summary>
        private int _begun;

        /// <summary>The plans, in poll order.</summary>
        public ConcurrentQueue<JobPollPlan> Plans { get; } = new();

        /// <summary>How each poll answers: from its plan and its number, from 1; by default every claim leases nothing.</summary>
        public Func<JobPollPlan, int, Task<JobPollResult>> Answer { get; set; } = static (plan, _) => Task.FromResult(Result(plan, static _ => 0));

        /// <summary>The tokens each poll was given.</summary>
        public ConcurrentQueue<CancellationToken> Tokens { get; } = new();

        /// <summary>A poll's result: each claim leasing the rows the function gives its handler.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="rows">The rows each handler's claim leases.</param>
        /// <returns>The result.</returns>
        public static JobPollResult Result(JobPollPlan plan, Func<JobHandlerRegistration, int> rows)
        {
            List<JobPolledClaim> claims = [];
            foreach (JobPollClaimRequest request in plan.Claims)
            {
                ClaimedJob[] leased = [.. Enumerable.Range(1, rows(request.Handler)).Select(id => new ClaimedJob(new Job { Id = id, HandlerKey = request.Handler.Key, RunAsUserId = 7, Attempts = 1 }, null, null, Guid.Empty))];
                JobClaim claim = new(plan.TenantId, request.Handler, Guid.NewGuid(), new BatchResult<IReadOnlyList<ClaimedJob>>(leased), null);
                claims.Add(new JobPolledClaim(claim, request.Reservation));
            }

            return new JobPollResult(plan.TenantId, claims, new JobGapCheck(null, DateTimeOffset.UnixEpoch, TimeSpan.FromHours(6), []), null, null, null, [], null);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _polled.Dispose();
        }

        /// <summary>Waits for the poll numbered <paramref name="count" /> to begin.</summary>
        /// <param name="count">The poll's number, from 1.</param>
        /// <returns>A task that completes once it began.</returns>
        public async Task PolledAsync(int count)
        {
            while (Plans.Count < count)
            {
                Assert.True(await _polled.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken), $"Poll {count} never began; {Plans.Count} did.");
            }
        }

        /// <inheritdoc />
        public async Task<JobPollResult> PollAsync(JobPollPlan plan, Action<JobPollResult> onExecuted, CancellationToken cancellationToken)
        {
            int number = Interlocked.Increment(ref _begun);
            Plans.Enqueue(plan);
            Tokens.Enqueue(cancellationToken);
            _ = _polled.Release();
            JobPollResult result = await Answer(plan, number).ConfigureAwait(false);
            onExecuted(result);
            return result;
        }
    }

    /// <summary>A dispatcher that records the claims it was given; their slots stay reserved until the test disposes them.</summary>
    internal sealed class FakeDispatch : IJobDispatch
    {
        /// <summary>The claims started, in order.</summary>
        public ConcurrentQueue<JobPolledClaim> Started { get; } = new();

        /// <inheritdoc />
        public void Start(JobPolledClaim polled)
        {
            Started.Enqueue(polled);
        }
    }

    /// <summary>A renewal that records each call and answers as the test says.</summary>
    internal sealed class FakeRenewal : IJobRenewal
    {
        /// <summary>The tenants renewed, in call order.</summary>
        public ConcurrentQueue<int> Renewed { get; } = new();

        /// <summary>The final renewals, as tenant and length.</summary>
        public ConcurrentQueue<(int TenantId, int LeaseSeconds)> Final { get; } = new();

        /// <summary>What each renewal does; by default nothing.</summary>
        public Func<int, CancellationToken, Task> Renew { get; set; } = static (_, _) => Task.CompletedTask;

        /// <inheritdoc />
        public async Task RenewAsync(int tenantId, CancellationToken cancellationToken = default)
        {
            Renewed.Enqueue(tenantId);
            await Renew(tenantId, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<JobLease>> RenewFinalAsync(int tenantId, int leaseSeconds, CancellationToken cancellationToken = default)
        {
            Final.Enqueue((tenantId, leaseSeconds));
            return Task.FromResult<IReadOnlyList<JobLease>>([]);
        }
    }

    /// <summary>A claim runner that tracks each claim's lease, as the real one does, and runs as the test says.</summary>
    /// <param name="leases">The leases the claims are tracked in.</param>
    internal sealed class FakeClaimRunner(JobLeases leases) : IJobClaimRunner
    {
        /// <summary>The host tokens each run was given.</summary>
        public ConcurrentQueue<CancellationToken> HostTokens { get; } = new();

        /// <summary>What each run does once its lease is tracked; by default it returns at once.</summary>
        public Func<JobLease, CancellationToken, Task> Run { get; set; } = static (_, _) => Task.CompletedTask;

        /// <inheritdoc />
        public async Task<IReadOnlyList<JobSettled>> RunAsync(JobClaim claim, CancellationToken hostToken)
        {
            HostTokens.Enqueue(hostToken);
            JobLease lease = leases.Track(claim.TenantId, claim.Handler, claim.Token, claim.Rows.Value, hostToken);
            try
            {
                await Run(lease, hostToken).ConfigureAwait(false);
                return [];
            }
            finally
            {
                // Every row leaves, as once completed, so the lease stops being tracked.
                lease.Leave([.. lease.Slots.Select(static slot => slot.JobId)]);
            }
        }
    }
}
