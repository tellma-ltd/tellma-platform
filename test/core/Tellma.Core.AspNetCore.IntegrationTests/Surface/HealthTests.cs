// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;

namespace Tellma.Core.AspNetCore.IntegrationTests.Surface
{
    /// <summary>
    ///     The health probes of a started instance, as its orchestrator calls them: no session, no
    ///     tenant, both healthy once the instance resolves tenants, each traced like any answer and
    ///     carrying no version tags.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class HealthTests(AcmeDeployment deployment)
    {
        [Theory]
        [InlineData("/health/live")]
        [InlineData("/health/ready")]
        public async Task A_started_instance_s_probe_answers_healthy_without_a_session(string path)
        {
            using SpaClient anonymous = deployment.Anonymous();

            ApiResponse probe = await anonymous.GetAsync(path);

            Assert.True(probe.Status == HttpStatusCode.OK, probe.ToString());
            Assert.Equal("Healthy", probe.Text);
            Assert.False(string.IsNullOrEmpty(probe.Header(TellmaHeaders.TraceId)));
            Assert.False(string.IsNullOrEmpty(probe.Header(TellmaHeaders.Build)));
            Assert.Null(probe.Header(TellmaHeaders.VersionTags));
        }
    }
}
