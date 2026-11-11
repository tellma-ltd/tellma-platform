// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Net;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Authentication
{
    /// <summary>
    ///     An endpoint's assurance bar: a session that reached it passes, one that did not gets the
    ///     step-up challenge, and a service account, which cannot step up, is refused outright.
    /// </summary>
    public sealed class StepUpTests(StepUpTests.Host fixture) : IClassFixture<StepUpTests.Host>
    {
        [Fact]
        public async Task A_session_that_reached_the_bar_recently_passes()
        {
            string cookie = fixture.Test.Cookie(acr: ProbeEndpoints.StepUpAcr, authTime: fixture.Test.Time.GetUtcNow().AddMinutes(-1));
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/stepup", cookie, "{}");
            using HttpResponseMessage response = await fixture.Test.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ProbeEndpoints.StepUpAcr, (await ProbeResult.ReadAsync(response)).Acr);
        }

        [Theory]
        [InlineData("urn:tellma:acr:pwd", 1)]
        [InlineData(null, 1)]
        [InlineData(ProbeEndpoints.StepUpAcr, 20)]
        public async Task A_session_below_the_bar_is_challenged_to_step_up(string? acr, int minutesAgo)
        {
            using MetricCollector<long> collector = new(
                fixture.Test.Services.GetRequiredService<IMeterFactory>(), HostTelemetryNames.MeterName, HostTelemetryNames.StepUpChallenges);
            string cookie = fixture.Test.Cookie(acr: acr, authTime: fixture.Test.Time.GetUtcNow().AddMinutes(-minutesAgo));
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/stepup", cookie, "{}");
            using HttpResponseMessage response = await fixture.Test.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(
                $"Bearer error=\"insufficient_user_authentication\", acr_values=\"{ProbeEndpoints.StepUpAcr}\", max_age=900",
                Assert.Single(response.Headers.WwwAuthenticate).ToString());
            Assert.Equal("step-up-required", (await Problem.ReadAsync(response)).Code);
            Assert.Equal(1, collector.GetMeasurementSnapshot().Sum(static measurement => measurement.Value));
        }

        [Fact]
        public async Task A_session_without_a_sign_in_time_cannot_prove_it_is_recent()
        {
            string cookie = fixture.Test.Cookie(acr: ProbeEndpoints.StepUpAcr);
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/stepup", cookie, "{}");
            using HttpResponseMessage response = await fixture.Test.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task A_service_account_is_refused_an_endpoint_with_a_bar()
        {
            using HttpRequestMessage request = TellmaTestHost.BearerRequest(HttpMethod.Post, "/1/api/v1/stepup", TellmaTestHost.ServiceToken());
            using HttpResponseMessage response = await fixture.Test.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Empty(response.Headers.WwwAuthenticate);
            Assert.Equal("individual-required", (await Problem.ReadAsync(response)).Code);
        }

        /// <summary>One host for the suite.</summary>
        public sealed class Host : IAsyncLifetime
        {
            private TellmaTestHost? _host;

            /// <summary>The started host.</summary>
            internal TellmaTestHost Test => _host ?? throw new InvalidOperationException("The host has not started.");

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                _host = await TellmaTestHost.StartAsync();
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (_host is not null)
                {
                    await _host.DisposeAsync();
                }
            }
        }
    }
}
