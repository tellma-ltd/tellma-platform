// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Tests.Observability
{
    /// <summary>
    ///     The <c>Tellma.Identity</c> meter records the counters §15 requires, so the alerts and
    ///     dashboards built on them have data. Guards against the instruments being defined but
    ///     never incremented.
    /// </summary>
    public sealed class IdentityMetricsTests
    {
        [Fact]
        public void Login_attempts_and_token_issuance_record_measurements()
        {
            using ServiceProvider provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
            IMeterFactory meterFactory = provider.GetRequiredService<IMeterFactory>();

            // Attach the collectors to the same factory before the instruments are created so they
            // observe every measurement the meter publishes.
            using MetricCollector<long> logins =
                new(meterFactory, IdentityMetrics.MeterName, "tellma.identity.login.attempts");
            using MetricCollector<long> tokens =
                new(meterFactory, IdentityMetrics.MeterName, "tellma.identity.tokens.issued");
            using MetricCollector<long> reuse =
                new(meterFactory, IdentityMetrics.MeterName, "tellma.identity.refresh.reuse_detected");

            IdentityMetrics metrics = new(meterFactory);
            metrics.LoginAttempt("passkey", "success", "primary");
            metrics.TokenIssued("authorization_code");
            metrics.RefreshReuseDetected();

            Assert.Equal(1, Assert.Single(logins.GetMeasurementSnapshot()).Value);
            Assert.Equal("passkey", Assert.Single(logins.GetMeasurementSnapshot()).Tags["method"]);
            Assert.Equal(1, Assert.Single(tokens.GetMeasurementSnapshot()).Value);
            Assert.Equal(1, Assert.Single(reuse.GetMeasurementSnapshot()).Value);
        }
    }
}
