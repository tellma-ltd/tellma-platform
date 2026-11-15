// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Crud.Validation
{
    /// <summary>
    ///     The validation scheduler: validators run concurrently, park at the loader, and share one
    ///     dispatch per round; late tasks join; the round ceiling, a fault and a cancellation stop the
    ///     rounds and settle every task.
    /// </summary>
    public sealed class ValidationSchedulerTests : IDisposable
    {
        /// <summary>The composed validation model.</summary>
        private readonly ValidationRig _rig = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _rig.Dispose();
        }

        /// <summary>Concurrent validators share one dispatch per round, and the rounds are as many as the most loads any one awaits.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task Validators_share_one_dispatch_per_round()
        {
            using MetricCollector<long> rounds = Collector(CrudTelemetryNames.ValidationRounds);
            BatchScript script = WidgetScript();
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script, metrics: Metrics());

            scheduler.Run("one", async () =>
            {
                _ = loader.ByIds<VmWidget, int>([1]);
                await loader.LoadAsync();
            });
            scheduler.Run("three", async () =>
            {
                for (int id = 2; id <= 4; id++)
                {
                    _ = loader.ByIds<VmWidget, int>([id]);
                    await loader.LoadAsync();
                }
            });
            scheduler.Run("none", static () => Task.CompletedTask);

            Assert.Equal(3, await scheduler.RunAsync());
            Assert.Equal([[1, 2], [3], [4]], script.Batches.Select(static batch => Assert.Single(batch.Of(ScriptedKind.Query)).KeySet<IdList>().Select(static row => row.Id).ToArray()));
            CollectedMeasurement<long> recorded = Assert.Single(rounds.GetMeasurementSnapshot());
            Assert.Equal(3, recorded.Value);
            Assert.Equal("save", recorded.Tags[CrudTelemetryNames.OperationTag]);
            Assert.Equal(CrudMetrics.RoundsOk, recorded.Tags[CrudTelemetryNames.OutcomeTag]);
        }

        /// <summary>A task registered by a running validator joins the round in progress; one registered by a dispatch joins the next.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_late_task_joins_the_round_in_progress_or_the_next()
        {
            BatchScript script = WidgetScript();
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            bool enlisted = false;
            script.OnExecute((batch, token) =>
            {
                // Registered while the first round is dispatched: it requests and parks for the second.
                if (batch.Number == 1)
                {
                    scheduler.Run("from-dispatch", async () =>
                    {
                        _ = loader.ByIds<VmWidget, int>([3]);
                        await loader.LoadAsync();
                    });
                }

                return Task.CompletedTask;
            });

            scheduler.Run("validator", async () =>
            {
                _ = loader.ByIds<VmWidget, int>([1]);

                // An enlisted write's validator joins this round before it closes.
                scheduler.Run("enlisted", async () =>
                {
                    await Task.Yield();
                    enlisted = true;
                    _ = loader.ByIds<VmWidget, int>([2]);
                    await loader.LoadAsync();
                });
                await loader.LoadAsync();
            });

            Assert.Equal(2, await scheduler.RunAsync());
            Assert.True(enlisted);
            Assert.Equal([[1, 2], [3]], script.Batches.Select(static batch => Assert.Single(batch.Of(ScriptedKind.Query)).KeySet<IdList>().Select(static row => row.Id).ToArray()));
            _ = Assert.Throws<InvalidOperationException>(() => scheduler.Run("after", static () => Task.CompletedTask));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(scheduler.RunAsync);
        }

        /// <summary>A round beyond the ceiling is an authoring bug naming the validator, recorded as exceeded, and every task settles.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_round_beyond_the_ceiling_names_the_validator()
        {
            using MetricCollector<long> rounds = Collector(CrudTelemetryNames.ValidationRounds);
            BatchScript script = WidgetScript();
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script, maxRounds: 2, metrics: Metrics());
            bool thirdResumed = false;

            scheduler.Run("Acme.GreedyValidator", async () =>
            {
                for (int id = 1; id <= 3; id++)
                {
                    _ = loader.ByIds<VmWidget, int>([id]);
                    await loader.LoadAsync();
                    thirdResumed = id == 3;
                }
            });

            InvalidOperationException exceeded = await Assert.ThrowsAsync<InvalidOperationException>(scheduler.RunAsync);
            Assert.Contains("'Acme.GreedyValidator'", exceeded.Message, StringComparison.Ordinal);
            Assert.Contains("MaxValidationRounds", exceeded.Message, StringComparison.Ordinal);
            Assert.False(thirdResumed);
            Assert.Equal(2, script.RoundTrips);
            CollectedMeasurement<long> recorded = Assert.Single(rounds.GetMeasurementSnapshot());
            Assert.Equal(2, recorded.Value);
            Assert.Equal(CrudMetrics.RoundsExceeded, recorded.Tags[CrudTelemetryNames.OutcomeTag]);
        }

        /// <summary>A validator's fault stops the rounds and is the outcome; a parked validator is torn down without running on.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_fault_stops_the_rounds_and_tears_down_the_parked()
        {
            BatchScript script = WidgetScript();
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            TaskCompletionSource faulting = new(TaskCreationOptions.RunContinuationsAsynchronously);
            bool parkedResumed = false;

            scheduler.Run("parked", async () =>
            {
                _ = loader.ByIds<VmWidget, int>([1]);
                faulting.SetResult();
                await loader.LoadAsync();
                parkedResumed = true;
            });
            scheduler.Run("faulty", async () =>
            {
                await faulting.Task;
                throw new FormatException("broken rule");
            });

            FormatException fault = await Assert.ThrowsAsync<FormatException>(scheduler.RunAsync);
            Assert.Equal("broken rule", fault.Message);
            Assert.False(parkedResumed);
            Assert.Equal(0, script.RoundTrips);
        }

        /// <summary>A failing dispatch fails the rounds with the dispatch's error and tears down the round's tasks.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_failing_dispatch_is_the_outcome()
        {
            BatchScript script = WidgetScript().OnExecute(static (_, _) => throw new TimeoutException("the database went away"));
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            bool resumed = false;

            scheduler.Run("validator", async () =>
            {
                _ = loader.ByIds<VmWidget, int>([1]);
                await loader.LoadAsync();
                resumed = true;
            });

            _ = await Assert.ThrowsAsync<TimeoutException>(scheduler.RunAsync);
            Assert.False(resumed);
        }

        /// <summary>The operation's cancellation stops the rounds even while a validator awaits foreign work, and while a round is in flight.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_cancellation_stops_the_rounds()
        {
            // --- While a validator awaits work that never ends, the parked one fails at once ---
            using var first = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(WidgetScript(), cancellationToken: first.Token);
            scheduler.Run("parked", async () =>
            {
                _ = loader.ByIds<VmWidget, int>([1]);
                await loader.LoadAsync();
            });
            scheduler.Run("stuck", () => never.Task.WaitAsync(first.Token));
            Task<int> running = scheduler.RunAsync();
            await first.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);

            // --- While the round's batch is in flight ---
            using var second = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            TaskCompletionSource sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
            BatchScript inFlight = WidgetScript().OnExecute((_, token) =>
            {
                // The round trip never answers: only the cancellation ends it.
                sent.SetResult();
                return never.Task.WaitAsync(token);
            });
            (ValidationScheduler later, ContextLoader laterLoader) = _rig.Loader(inFlight, cancellationToken: second.Token);
            later.Run("validator", async () =>
            {
                _ = laterLoader.ByIds<VmWidget, int>([1]);
                await laterLoader.LoadAsync();
            });
            Task<int> dispatching = later.RunAsync();
            await sent.Task;
            await second.CancelAsync();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatching);
        }

        /// <summary>The pipeline's own loads ask for a round with no task parked; a load awaited outside every task is refused.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task The_pipelines_own_loads_ride_a_requested_round()
        {
            BatchScript script = WidgetScript();
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(script);
            ContextRef<IReadOnlyDictionary<int, VmWidget>> own = loader.ByIds<VmWidget, int>([1]);

            Assert.False(scheduler.IsInTask);
            _ = Assert.Throws<InvalidOperationException>(() => { _ = loader.LoadAsync(); });
            scheduler.RequestRound();

            Assert.Equal(1, await scheduler.RunAsync());
            Assert.True(own.IsLoaded);
            Assert.True(loader.LoadAsync().IsCompleted);
        }

        /// <summary>One task parks at one load at a time.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public async Task A_task_parks_at_one_load_at_a_time()
        {
            (ValidationScheduler scheduler, ContextLoader loader) = _rig.Loader(WidgetScript());
            scheduler.Run("double", async () =>
            {
                _ = loader.ByIds<VmWidget, int>([1]);
                Task first = loader.LoadAsync();
                Assert.Contains("already parked", Assert.Throws<InvalidOperationException>(() => { _ = loader.LoadAsync(); }).Message, StringComparison.Ordinal);
                await first;
            });

            Assert.Equal(1, await scheduler.RunAsync());
        }

        /// <summary>A collector of one validation instrument.</summary>
        /// <param name="instrument">The instrument's name.</param>
        /// <returns>The collector.</returns>
        private MetricCollector<long> Collector(string instrument)
        {
            return new MetricCollector<long>(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, instrument);
        }

        /// <summary>The composed instruments.</summary>
        /// <returns>The instruments.</returns>
        private CrudMetrics Metrics()
        {
            return _rig.Services.GetRequiredService<CrudMetrics>();
        }

        /// <summary>A script answering every widget query with a widget per key.</summary>
        /// <returns>The script.</returns>
        private static BatchScript WidgetScript()
        {
            return new BatchScript().OnQuery<VmWidget>(static statement =>
                [.. statement.KeySet<IdList>().Select(static row => new VmWidget { Id = row.Id, Code = "W" })]);
        }
    }
}
