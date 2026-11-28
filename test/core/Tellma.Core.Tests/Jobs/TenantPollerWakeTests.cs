// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Tellma.Core.Jobs;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The seam that wakes a tenant's pollers after an enqueue commits: every subscriber of the
    ///     tenant is woken and no other, a failing subscriber is logged and never thrown and the rest
    ///     are still woken, and a disposed subscription is woken no more.
    /// </summary>
    public sealed class TenantPollerWakeTests : IDisposable
    {
        /// <summary>The job machinery's log category.</summary>
        private const string Category = "Tellma.Core.Jobs";

        /// <summary>Collects the seam's logs.</summary>
        private readonly FakeLoggerProvider _logs = new();

        /// <summary>The factory the seam creates its logger from.</summary>
        private readonly LoggerFactory _loggers;

        /// <summary>The seam under test.</summary>
        private readonly TenantPollerWake _wake;

        /// <summary>Creates the seam over a collecting logger.</summary>
        public TenantPollerWakeTests()
        {
            _loggers = new LoggerFactory([_logs]);
            _wake = new TenantPollerWake(_loggers);
        }

        [Fact]
        public void Every_subscriber_of_the_tenant_is_woken_and_no_other()
        {
            int first = 0, second = 0, other = 0;
            using IDisposable a = _wake.Subscribe(1, () => first++);
            using IDisposable b = _wake.Subscribe(1, () => second++);
            using IDisposable c = _wake.Subscribe(2, () => other++);

            _wake.Wake(1);
            _wake.Wake(3);

            Assert.Equal((1, 1, 0), (first, second, other));
            Assert.Empty(Logs());
        }

        [Fact]
        public void A_failing_subscriber_is_logged_never_thrown_and_the_rest_are_still_woken()
        {
            int before = 0, after = 0;
            using IDisposable a = _wake.Subscribe(1, () => before++);
            using IDisposable failing = _wake.Subscribe(1, static () => throw new InvalidOperationException("The poller is gone."));
            using IDisposable b = _wake.Subscribe(1, () => after++);

            _wake.Wake(1);

            Assert.Equal((1, 1), (before, after));
            FakeLogRecord log = Assert.Single(Logs());
            Assert.Equal((LogLevel.Warning, "JobNudgeFailed"), (log.Level, log.Id.Name));
            Assert.Equal("The poller is gone.", Assert.IsType<InvalidOperationException>(log.Exception).Message);
        }

        [Fact]
        public void A_disposed_subscription_is_woken_no_more_and_disposing_it_again_does_nothing()
        {
            int kept = 0, dropped = 0;
            using IDisposable keep = _wake.Subscribe(1, () => kept++);
            IDisposable drop = _wake.Subscribe(1, () => dropped++);

            drop.Dispose();
            drop.Dispose();
            _wake.Wake(1);

            Assert.Equal((1, 0), (kept, dropped));
        }

        [Fact]
        public void A_tenant_whose_last_subscription_ended_can_be_subscribed_again()
        {
            int first = 0, again = 0;
            IDisposable only = _wake.Subscribe(1, () => first++);
            only.Dispose();
            _wake.Wake(1);

            using IDisposable renewed = _wake.Subscribe(1, () => again++);
            _wake.Wake(1);

            Assert.Equal((0, 1), (first, again));
        }

        [Fact]
        public void A_wake_in_flight_keeps_the_subscribers_it_read()
        {
            // A subscriber that ends another's subscription mid-wake does not change the wake in flight.
            int second = 0;
            IDisposable? later = null;
            using IDisposable first = _wake.Subscribe(1, () => later!.Dispose());
            later = _wake.Subscribe(1, () => second++);

            _wake.Wake(1);
            _wake.Wake(1);

            Assert.Equal(1, second);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _loggers.Dispose();
            _logs.Dispose();
        }

        /// <summary>The seam's log records.</summary>
        /// <returns>The records of the job machinery's category.</returns>
        private List<FakeLogRecord> Logs()
        {
            return [.. _logs.Collector.GetSnapshot().Where(static record => record.Category == Category)];
        }
    }
}
