// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;

namespace Tellma.Core.Testing.Tests.Email
{
    /// <summary>
    ///     A fixture builder that reached for the wall clock would make every assertion touching
    ///     ordering or lag flaky, so its determinism is part of its contract.
    /// </summary>
    public class DeliveryEventsTests
    {
        [Fact]
        public void Builds_a_batch_in_the_order_it_was_written()
        {
            IReadOnlyList<EmailDeliveryEvent> events = DeliveryEvents
                .For(new EmailCorrelation("outbox", "42", 3))
                .Delivered()
                .Opened()
                .Clicked()
                .Build();

            Assert.Equal(
                [EmailDeliveryEventType.Delivered, EmailDeliveryEventType.Opened, EmailDeliveryEventType.Clicked],
                events.Select(static e => e.Type));
        }

        [Fact]
        public void Gives_every_event_a_distinct_id_and_an_advancing_timestamp()
        {
            IReadOnlyList<EmailDeliveryEvent> events = DeliveryEvents
                .For(new EmailCorrelation("outbox", "42"))
                .Deferred()
                .Delivered()
                .Build();

            Assert.Equal(2, events.Select(static e => e.ProviderEventId).Distinct().Count());
            Assert.True(events[1].Timestamp > events[0].Timestamp);
        }

        [Fact]
        public void Produces_the_same_batch_every_run()
        {
            EmailCorrelation correlation = new("outbox", "42");

            IReadOnlyList<EmailDeliveryEvent> first = DeliveryEvents.For(correlation).Bounced().Build();
            IReadOnlyList<EmailDeliveryEvent> second = DeliveryEvents.For(correlation).Bounced().Build();

            Assert.Equal(first, second);
        }

        [Fact]
        public void Repeats_an_event_verbatim_for_a_deduplication_test()
        {
            IReadOnlyList<EmailDeliveryEvent> events = DeliveryEvents
                .For(new EmailCorrelation("outbox", "42"))
                .Delivered()
                .Redeliver()
                .Build();

            // A provider redelivery is the same event again, which is exactly what a handler must
            // deduplicate on.
            Assert.Equal(2, events.Count);
            Assert.Equal(events[0], events[1]);
        }

        [Fact]
        public void Builds_the_uncorrelated_events_a_provider_also_delivers()
        {
            IReadOnlyList<EmailDeliveryEvent> events = DeliveryEvents.For(null).SpamReported().Build();

            Assert.Null(Assert.Single(events).Correlation);
        }

        [Fact]
        public void Refuses_to_redeliver_nothing()
        {
            Assert.Throws<InvalidOperationException>(
                () => DeliveryEvents.For(null).Redeliver());
        }
    }
}
