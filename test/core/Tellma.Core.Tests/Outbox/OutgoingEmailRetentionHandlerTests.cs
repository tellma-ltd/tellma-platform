// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Outbox;
using Tellma.Core.Tests.Access;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The nightly retention's paging, over fake pages: it reads pages until one is short, stops
    ///     at a full page that deleted nothing rather than read it forever, and reads a page again
    ///     once when its delete is refused, the second refusal failing the run.
    /// </summary>
    public sealed class OutgoingEmailRetentionHandlerTests : IDisposable
    {
        /// <summary>The meter factory the instruments are created from.</summary>
        private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();

        /// <summary>Collects the handler's logs.</summary>
        private readonly FakeLoggerProvider _logs = new();

        /// <summary>The pages, which answer from a script and record each call.</summary>
        private readonly ScriptedPages _pages = new();

        /// <summary>The progress the handler reports, recorded.</summary>
        private readonly RecordingProgress _progress = new();

        /// <summary>The cancellation token of the running test.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public void Dispose()
        {
            _logs.Dispose();
            _services.Dispose();
        }

        [Fact]
        public async Task Full_pages_are_read_and_deleted_until_a_short_one_each_reporting_the_total_so_far()
        {
            using MetricCollector<long> retained = new(Meters, OutboxTelemetryNames.MeterName, OutboxTelemetryNames.Retained);
            _pages.Reads.Enqueue(Ids(1, OutgoingEmailRetentionHandler.PageSize));
            _pages.Reads.Enqueue(Ids(1001, OutgoingEmailRetentionHandler.PageSize));
            _pages.Reads.Enqueue(Ids(2001, 3));

            await Handler().ExecuteAsync(Batch());

            Assert.Equal(["read 500", "delete 500", "read 500", "delete 500", "read 500", "delete 3"], _pages.Calls);
            Assert.Equal([500, 1000, 1003], _progress.Totals);
            Assert.Equal([500L, 500L, 3L], retained.GetMeasurementSnapshot().Select(static point => point.Value));
        }

        [Fact]
        public async Task A_full_page_that_deleted_nothing_ends_the_run_rather_than_being_read_again()
        {
            _pages.Reads.Enqueue(Ids(1, OutgoingEmailRetentionHandler.PageSize));
            _pages.Deletes.Enqueue(static _ => 0);

            await Handler().ExecuteAsync(Batch());

            Assert.Equal(["read 500", "delete 500"], _pages.Calls);
            Assert.Equal([0], _progress.Totals);
        }

        [Fact]
        public async Task A_refused_delete_reads_the_page_once_more_and_deletes_it_as_it_stands_now()
        {
            _pages.Reads.Enqueue(Ids(1, 4));
            _pages.Reads.Enqueue(Ids(2, 3));
            _pages.Deletes.Enqueue(static _ => throw Refusal());

            await Handler().ExecuteAsync(Batch());

            Assert.Equal(["read 500", "delete 4", "read 500", "delete 3"], _pages.Calls);
            Assert.Equal([3], _progress.Totals);
            FakeLogRecord reread = Assert.Single(_logs.Collector.GetSnapshot(), static record => record.Id.Name == "OutboxRetentionPageReread");
            Assert.IsType<ValidationException>(reread.Exception);
        }

        [Fact]
        public async Task A_delete_refused_again_after_the_page_was_read_again_fails_the_run()
        {
            _pages.Reads.Enqueue(Ids(1, 4));
            _pages.Reads.Enqueue(Ids(1, 4));
            _pages.Deletes.Enqueue(static ids => throw new NotFoundException("core.OutgoingEmail", [ids[0].ToString(CultureInfo.InvariantCulture)]));
            _pages.Deletes.Enqueue(static _ => throw Refusal());

            _ = await Assert.ThrowsAsync<ValidationException>(() => Handler().ExecuteAsync(Batch()));

            Assert.Equal(["read 500", "delete 4", "read 500", "delete 4"], _pages.Calls);
            Assert.Empty(_progress.Totals);
        }

        /// <summary>The host's meter factory.</summary>
        private IMeterFactory Meters => _services.GetRequiredService<IMeterFactory>();

        /// <summary>The handler over the fakes, with the default retention.</summary>
        /// <returns>The handler.</returns>
        private OutgoingEmailRetentionHandler Handler()
        {
            return new OutgoingEmailRetentionHandler(_pages, Options.Create(new OutboxOptions()), new OutboxMetrics(Meters), new LoggerFactory([_logs]));
        }

        /// <summary>The run's batch: one item, the scheduled job, reporting to the recorded progress.</summary>
        /// <returns>The batch.</returns>
        private JobBatch<Job> Batch()
        {
            Job job = new() { Id = 1, HandlerKey = OutgoingEmailRetentionHandler.Key, Attempts = 1 };
            JobItem<Job> item = new() { Job = job, Item = job, Progress = _progress, CancellationToken = Token };
            return new JobBatch<Job> { Items = [item], Batch = Refusing.Of<IDataBatch>() };
        }

        /// <summary>A run of consecutive ids.</summary>
        /// <param name="first">The first id.</param>
        /// <param name="count">How many.</param>
        /// <returns>The ids.</returns>
        private static int[] Ids(int first, int count)
        {
            return [.. Enumerable.Range(first, count)];
        }

        /// <summary>The stack's refusal of a row that changed since its page was read.</summary>
        /// <returns>The refusal.</returns>
        private static ValidationException Refusal()
        {
            return new ValidationException([new ValidationError(ValidationPath.Root, "Email.NotDeletable", new Dictionary<string, object?>())]);
        }

        /// <summary>Pages that answer each read and each delete from a script, recording each call.</summary>
        private sealed class ScriptedPages : IOutboxRetentionPages
        {
            /// <summary>The pages the reads answer, in order; an empty page once they run out.</summary>
            public Queue<int[]> Reads { get; } = new();

            /// <summary>What the deletes do with their ids, in order; each deletes every id once they run out.</summary>
            public Queue<Func<IReadOnlyList<int>, int>> Deletes { get; } = new();

            /// <summary>Each call: a read with the page size it asked for, or a delete with how many ids it named.</summary>
            public List<string> Calls { get; } = [];

            /// <inheritdoc />
            public Task<IReadOnlyList<int>> ReadAsync(int size, TimeSpan retention, CancellationToken cancellationToken)
            {
                Calls.Add($"read {size}");
                return Task.FromResult<IReadOnlyList<int>>(Reads.TryDequeue(out int[]? page) ? page : []);
            }

            /// <inheritdoc />
            public Task<int> DeleteAsync(IReadOnlyList<int> ids, CancellationToken cancellationToken)
            {
                Calls.Add($"delete {ids.Count}");
                return Task.FromResult(Deletes.TryDequeue(out Func<IReadOnlyList<int>, int>? delete) ? delete(ids) : ids.Count);
            }
        }

        /// <summary>A progress that records the total each report carries.</summary>
        private sealed class RecordingProgress : IJobProgress
        {
            /// <summary>The emails deleted so far, by report.</summary>
            public List<int> Totals { get; } = [];

            /// <inheritdoc />
            public void Report(byte? percent, JobMessage? message, object? state)
            {
                Totals.Add((int)message!.Arguments!["deleted"]!);
            }

            /// <inheritdoc />
            public Task FlushAsync(CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public void Append(IDataBatch batch, byte? percent, JobMessage? message, object? state)
            {
            }
        }
    }
}
