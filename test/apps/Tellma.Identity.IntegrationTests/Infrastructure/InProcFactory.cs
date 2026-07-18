// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Tellma.Identity.TestInProcHost;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Boots the distribution-shaped <c>Tellma.Identity.TestInProcHost</c> asset in-memory:
    ///     the identity engine mounted at <c>/id</c> inside a host that serves its own routes.
    /// </summary>
    public class InProcFactory : WebApplicationFactory<InProcHostMarker>
    {
        /// <summary>Extra configuration applied on top of the test defaults.</summary>
        public Dictionary<string, string?> ConfigurationOverrides { get; } = [];

        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);

            Dictionary<string, string?> settings = new()
            {
                ["TellmaIdentity:Mode"] = "InProc",
                ["TellmaIdentity:PathBase"] = "/id",
                ["TellmaIdentity:Issuer"] = "http://localhost/id",
                ["TellmaIdentity:ConnectionString"] = "Server=unused;Database=unused;Encrypt=False",
                ["TellmaIdentity:Keys:Signing:Source"] = "DevelopmentSelfSigned",
                ["TellmaIdentity:Keys:Encryption:Source"] = "DevelopmentSelfSigned",
                ["TellmaIdentity:Development:AllowDevelopmentCertificates"] = "true",
                ["TellmaIdentity:Development:AllowInsecureHttp"] = "true",
                ["TellmaIdentity:Development:UseEmailSink"] = "true",
                ["TellmaIdentity:Seed:ApplyMigrations"] = "false",
                ["TellmaIdentity:Seed:DevAdmin:Enabled"] = "false",
            };

            foreach ((string key, string? value) in ConfigurationOverrides)
            {
                settings[key] = value;
            }

            // UseSetting flows through host configuration, which beats the app's JSON files —
            // ConfigureAppConfiguration sources would be added before them and lose.
            foreach ((string key, string? value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
