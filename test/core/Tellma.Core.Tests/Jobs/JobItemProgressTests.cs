// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     A handler's progress reports under a fake clock with no database: a jump of five points
    ///     renews at once, at most once per job every five seconds, and a jump's failed renewal is
    ///     logged, never thrown at the handler.
    /// </summary>
    public sealed class JobItemProgressTests : IDisposable
    {
        /// <summary>The tenant the claim was made in.</summary>
        private const int TenantId = 5;

        /// <summary>The worker tests' handlers.</summary>
        private readonly BatchHarness _harness = new(compose: WorkerTestModel.Compose);

        /// <summary>The clock a jump is timed by.</summary>
        private readonly SignallingTimeProvider _time = new();

        /// <summary>Collects the progress's logs.</summary>
        private readonly FakeLoggerProvider _logs = new();

        /// <summary>The factory the logger comes from.</summary>
        private readonly LoggerFactory _loggers;

        /// <summary>The renewal a jump asks for.</summary>
        private readonly FakeRenewal _renewal = new();

        /// <summary>The claim's lease.</summary>
        private readonly JobLease _lease;

        /// <summary>The progress under test.</summary>
        private readonly JobItemProgress _progress;

        /// <summary>Creates one item's progress over a tracked lease.</summary>
        public JobItemProgressTests()
        {
            _loggers = new LoggerFactory([_logs]);
            JobHandlerRegistration audit = _harness.Services.GetRequiredService<JobHandlerRegistry>().TryFind(WorkerTestModel.AuditKey, out JobHandlerRegistration? handler)
                ? handler
                : throw new InvalidOperationException("The worker test model registers the handler.");
            ClaimedJob row = new(new Job { Id = 1, HandlerKey = WorkerTestModel.AuditKey, RunAsUserId = 7, Attempts = 1 }, null, null, Guid.Empty);
            _lease = new JobLeases(_time).Track(TenantId, audit, Guid.NewGuid(), [row], TestContext.Current.CancellationToken);
            _progress = new JobItemProgress(
                _lease,
                _lease.Slot(1)!,
                _harness.Batch(BatchPurpose.Persist),
                static _ => { },
                _renewal,
                _time,
                new SilentPublisher(),
                _harness.Services.GetRequiredService<JobLogText>(),
                _loggers.CreateLogger(JobsLog.Category));
        }

        [Fact]
        public async Task A_jump_renews_at_once_and_another_waits_five_seconds()
        {
            // --- Four points from the claimed percent is no jump; ten is ---
            _progress.Report(4, null, null);
            _progress.Report(10, null, null);
            await WaitUntilAsync(() => _renewal.Renewed.Count == 1);
            Assert.Equal([TenantId], _renewal.Renewed);

            // --- A bigger jump within five seconds buffers for the next renewal only ---
            _time.Advance(TimeSpan.FromSeconds(4));
            _progress.Report(50, null, null);
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            Assert.Single(_renewal.Renewed);
            Assert.Equal((byte)50, _lease.Slot(1)!.Buffered()!.Value.Row.ProgressPercent);

            // --- Five seconds after the last jump, the next one renews ---
            _time.Advance(TimeSpan.FromSeconds(1));
            _progress.Report(60, null, null);
            await WaitUntilAsync(() => _renewal.Renewed.Count == 2);
        }

        [Fact]
        public async Task A_jump_whose_renewal_fails_is_logged_and_the_report_returns()
        {
            _renewal.Renew = static (_, _) => throw new InvalidOperationException("The tenant's database is unreachable.");

            _progress.Report(30, null, null);

            await WaitUntilAsync(() => _logs.Collector.GetSnapshot().Any(static record => record.Id.Name == "JobRenewalFailed"));
            Assert.Equal((byte)30, _lease.Slot(1)!.Buffered()!.Value.Row.ProgressPercent);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _lease.Dispose();
            _time.Dispose();
            _loggers.Dispose();
            _logs.Dispose();
            _harness.Dispose();
        }

        /// <summary>Waits until a condition holds.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes once it held.</returns>
        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (!condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }
        }

        /// <summary>A publisher that drops every event: a report publishes none.</summary>
        private sealed class SilentPublisher : IClientEventPublisher
        {
            /// <inheritdoc />
            public void Publish(IDataBatch? batch, ClientEvent clientEvent)
            {
            }

            /// <inheritdoc />
            public Task PublishAsync(ClientEvent clientEvent)
            {
                return Task.CompletedTask;
            }
        }
    }
}
