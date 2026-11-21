// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Notifications;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Notifications
{
    /// <summary>
    ///     The client event publisher: its refusals, the coalescing of a batch's events into its one
    ///     commit callback, the logging of a failed delivery, and the hub-less defaults the composition
    ///     registers.
    /// </summary>
    public class ClientEventPublisherTests
    {
        private static readonly ClientEventRegistry Registry = new([new ClientEventContributionItem("inbox.changed"), new ClientEventContributionItem("cache.changed")]);

        // --- Refusals ---

        [Fact]
        public void The_null_publisher_refuses_an_unregistered_name_on_both_paths()
        {
            NullClientEventPublisher publisher = new(Registry, NullLogger<NullClientEventPublisher>.Instance);
            ClientEvent unregistered = new("job.changed", [1], null);

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => publisher.Publish(null, unregistered));
            Assert.Equal(
                "The client event 'job.changed' is not registered. Register it in the feature that publishes it with contribution.ClientEvent(name).",
                refused.Message);

            // Thrown by the call itself, not through the task it would return.
            _ = Assert.Throws<InvalidOperationException>(() => { _ = publisher.PublishAsync(unregistered); });
        }

        [Fact]
        public void A_payload_over_the_cap_is_refused_and_one_at_the_cap_is_not()
        {
            NullClientEventPublisher publisher = new(Registry, NullLogger<NullClientEventPublisher>.Instance);

            // {"tag":"…"} is 10 bytes around the text; a two-byte character counts twice.
            string atCap = new('x', ClientEvent.MaxPayloadBytes - 10);
            string overCap = new string('x', ClientEvent.MaxPayloadBytes - 11) + "é";

            publisher.Publish(null, new ClientEvent("cache.changed", [], new { tag = atCap }));
            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                () => publisher.Publish(null, new ClientEvent("cache.changed", [], new { tag = overCap })));
            Assert.StartsWith("The payload of the client event 'cache.changed' serialises to 4097 bytes, more than the 4096", refused.Message, StringComparison.Ordinal);
            _ = Assert.Throws<InvalidOperationException>(() => { _ = publisher.PublishAsync(new ClientEvent("cache.changed", [], new { tag = overCap })); });
        }

        [Fact]
        public void A_refused_event_registers_nothing_on_the_batch()
        {
            (IDataBatch batch, CommitOnlyBatch fake) = CommitOnlyBatch.Create();
            NullClientEventPublisher publisher = new(Registry, NullLogger<NullClientEventPublisher>.Instance);

            _ = Assert.Throws<InvalidOperationException>(() => publisher.Publish(batch, new ClientEvent("job.changed", [1], null)));
            Assert.Empty(fake.Callbacks);
        }

        [Fact]
        public void A_null_event_or_null_users_are_refused()
        {
            NullClientEventPublisher publisher = new(Registry, NullLogger<NullClientEventPublisher>.Instance);

            _ = Assert.Throws<ArgumentNullException>(() => publisher.Publish(null, null!));
            _ = Assert.Throws<ArgumentException>(() => publisher.Publish(null, new ClientEvent("inbox.changed", null!, null)));
            _ = Assert.Throws<ArgumentNullException>(() => { _ = publisher.PublishAsync(null!); });
        }

        [Fact]
        public void A_derived_publisher_adds_its_refusals_after_the_shared_ones()
        {
            RecordingPublisher publisher = new() { RefuseScope = true };

            // The shared refusal comes first; the host's own after it, before anything is sent.
            _ = Assert.Throws<InvalidOperationException>(() => publisher.Publish(null, new ClientEvent("job.changed", [1], null)));
            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => publisher.Publish(null, new ClientEvent("inbox.changed", [1], null)));
            Assert.Equal("No tenant.", refused.Message);
            Assert.Empty(publisher.Delivered);
        }

        // --- Delivery ---

        [Fact]
        public async Task Without_a_batch_an_event_is_delivered_at_once()
        {
            RecordingPublisher publisher = new();
            ClientEvent changed = new("inbox.changed", [7], null);

            publisher.Publish(null, changed);
            await publisher.PublishAsync(changed);

            Assert.Equal([changed, changed], publisher.Delivered);
        }

        [Fact]
        public async Task A_failed_delivery_is_logged_and_never_thrown()
        {
            FakeLogger logger = new();
            RecordingPublisher publisher = new(logger) { Fail = true };

            // Neither the immediate path nor the awaited one surfaces the failure.
            publisher.Publish(null, new ClientEvent("inbox.changed", [7], null));
            await publisher.PublishAsync(new ClientEvent("cache.changed", [], new { tag = "tenant" }));

            IReadOnlyList<FakeLogRecord> records = logger.Collector.GetSnapshot();
            Assert.Equal(["inbox.changed", "cache.changed"], records.Select(static record => record.GetStructuredStateValue("EventName")));
            Assert.All(records, static record =>
            {
                Assert.Equal(("ClientEventFailed", LogLevel.Warning), (record.Id.Name, record.Level));
                Assert.IsType<TimeoutException>(record.Exception);
            });
        }

        // --- Coalescing on a batch ---

        [Fact]
        public void A_batchs_events_share_one_callback_and_are_sent_only_after_commit_each_distinct_one_once()
        {
            (IDataBatch batch, CommitOnlyBatch fake) = CommitOnlyBatch.Create();
            RecordingPublisher publisher = new();

            ClientEvent toBoth = new("inbox.changed", [1, 2], null);
            ClientEvent tenant = new("cache.changed", [], new { tag = "settings" });
            publisher.Publish(batch, toBoth);
            publisher.Publish(batch, new ClientEvent("inbox.changed", [2, 1, 2], null));
            publisher.Publish(batch, tenant);
            publisher.Publish(batch, new ClientEvent("cache.changed", [], new { tag = "settings" }));
            ClientEvent otherTag = new("cache.changed", [], new { tag = "definitions" });
            publisher.Publish(batch, otherTag);
            ClientEvent toOne = new("inbox.changed", [1], null);
            publisher.Publish(batch, toOne);

            // One callback, and nothing sent before the commit.
            Assert.Single(fake.Callbacks);
            Assert.Empty(publisher.Delivered);

            fake.Commit();
            Assert.Equal([toBoth, tenant, otherTag, toOne], publisher.Delivered);
        }

        [Fact]
        public void Two_batches_hold_a_callback_each()
        {
            (IDataBatch first, CommitOnlyBatch firstFake) = CommitOnlyBatch.Create();
            (IDataBatch second, CommitOnlyBatch secondFake) = CommitOnlyBatch.Create();
            RecordingPublisher publisher = new();

            publisher.Publish(first, new ClientEvent("inbox.changed", [1], null));
            publisher.Publish(second, new ClientEvent("inbox.changed", [1], null));

            secondFake.Commit();
            Assert.Single(publisher.Delivered);
            firstFake.Commit();
            Assert.Equal(2, publisher.Delivered.Count);
        }

        [Fact]
        public void A_batch_that_does_not_commit_sends_nothing()
        {
            (IDataBatch batch, CommitOnlyBatch fake) = CommitOnlyBatch.Create();
            RecordingPublisher publisher = new();

            publisher.Publish(batch, new ClientEvent("inbox.changed", [1], null));
            fake.Fail();

            Assert.Empty(publisher.Delivered);
        }

        [Fact]
        public void Publishing_on_an_executed_batch_is_refused_whether_or_not_it_committed()
        {
            RecordingPublisher publisher = new();

            // A batch that failed before any publish on it.
            (IDataBatch failedFirst, CommitOnlyBatch failedFirstFake) = CommitOnlyBatch.Create();
            failedFirstFake.Fail();
            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => publisher.Publish(failedFirst, new ClientEvent("inbox.changed", [1], null)));
            Assert.StartsWith("The client event 'inbox.changed' was published on a batch that already executed", refused.Message, StringComparison.Ordinal);

            // A batch that failed after an event joined it: the next publish is refused too.
            (IDataBatch failed, CommitOnlyBatch failedFake) = CommitOnlyBatch.Create();
            publisher.Publish(failed, new ClientEvent("inbox.changed", [1], null));
            failedFake.Fail();
            _ = Assert.Throws<InvalidOperationException>(() => publisher.Publish(failed, new ClientEvent("inbox.changed", [2], null)));

            // A batch that committed.
            (IDataBatch committed, CommitOnlyBatch committedFake) = CommitOnlyBatch.Create();
            publisher.Publish(committed, new ClientEvent("inbox.changed", [1], null));
            committedFake.Commit();
            _ = Assert.Throws<InvalidOperationException>(() => publisher.Publish(committed, new ClientEvent("inbox.changed", [2], null)));

            // Only the committed batch's one event was sent.
            Assert.Single(publisher.Delivered);
        }

        [Fact]
        public void A_failed_delivery_from_the_commit_callback_is_logged_and_the_rest_are_sent()
        {
            (IDataBatch batch, CommitOnlyBatch fake) = CommitOnlyBatch.Create();
            FakeLogger logger = new();
            RecordingPublisher publisher = new(logger) { Fail = true };

            publisher.Publish(batch, new ClientEvent("inbox.changed", [1], null));
            publisher.Publish(batch, new ClientEvent("inbox.changed", [2], null));
            fake.Commit();

            Assert.Equal(2, publisher.Attempts);
            Assert.Equal(2, logger.Collector.Count);
        }

        // --- The composition's defaults ---

        [Fact]
        public async Task The_composition_registers_the_null_publisher_and_a_disabled_status()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            await using AsyncServiceScope scope = provider.CreateAsyncScope();

            Assert.IsType<NullClientEventPublisher>(scope.ServiceProvider.GetRequiredService<IClientEventPublisher>());
            Assert.False(provider.GetRequiredService<IRealtimeStatus>().Enabled);

            // The core events are registered, so the notifier's and the hub's publishes pass.
            await scope.ServiceProvider.GetRequiredService<IClientEventPublisher>().PublishAsync(new ClientEvent("inbox.changed", [1], null));
        }

        [Fact]
        public async Task A_host_registration_made_first_is_kept()
        {
            ServiceCollection services = new();
            services.AddScoped<IClientEventPublisher, RecordingPublisher>();
            services.AddSingleton<IRealtimeStatus, EnabledStatus>();

            await using ServiceProvider provider = TestComposition.Compose(static _ => { }, services: services).BuildServiceProvider(validateScopes: true);
            await using AsyncServiceScope scope = provider.CreateAsyncScope();

            Assert.IsType<RecordingPublisher>(scope.ServiceProvider.GetRequiredService<IClientEventPublisher>());
            Assert.True(provider.GetRequiredService<IRealtimeStatus>().Enabled);
        }

        /// <summary>A publisher that records what it delivers, or fails every delivery.</summary>
        /// <param name="logger">Where failed deliveries are logged.</param>
        public sealed class RecordingPublisher(ILogger logger) : ClientEventPublisher(Registry, logger)
        {
            /// <summary>Creates a publisher that logs nowhere.</summary>
            public RecordingPublisher()
                : this(NullLogger.Instance)
            {
            }

            /// <summary>The events delivered, in delivery order.</summary>
            public List<ClientEvent> Delivered { get; } = [];

            /// <summary>The deliveries attempted.</summary>
            public int Attempts { get; private set; }

            /// <summary>Whether every delivery fails: the first as a faulted task, the rest by throwing.</summary>
            public bool Fail { get; init; }

            /// <summary>Whether every event is refused as published from a scope without a tenant.</summary>
            public bool RefuseScope { get; init; }

            /// <inheritdoc />
            protected override void ValidateScope(ClientEvent clientEvent)
            {
                if (RefuseScope)
                {
                    throw new InvalidOperationException("No tenant.");
                }
            }

            /// <inheritdoc />
            protected override Task DeliverAsync(ClientEvent clientEvent)
            {
                // Completes before returning, so a test asserts right after a fire-and-forget send:
                // a faulted task for the first failure, a synchronous throw for the next.
                Attempts++;
                if (Fail)
                {
                    return Attempts == 1
                        ? Task.FromException(new TimeoutException("The transport timed out."))
                        : throw new TimeoutException("The transport timed out.");
                }

                Delivered.Add(clientEvent);
                return Task.CompletedTask;
            }
        }

        /// <summary>The status of a host that maps the hub.</summary>
        private sealed class EnabledStatus : IRealtimeStatus
        {
            /// <inheritdoc />
            public bool Enabled => true;
        }
    }
}
