// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;
using Tellma.Identity.TestInProcHost;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Boots the distribution-shaped <c>Tellma.Identity.TestInProcHost</c> asset in-memory:
    ///     the identity engine mounted at <see cref="PathBase" /> inside a host that serves its own
    ///     routes, with a capturing email sink in place of the host's email pipeline.
    /// </summary>
    public class InProcFactory : WebApplicationFactory<InProcHostMarker>
    {
        /// <summary>The path base the engine is mounted at inside the host.</summary>
        public const string PathBase = "/id";

        /// <summary>Extra configuration applied on top of the test defaults.</summary>
        public Dictionary<string, string?> ConfigurationOverrides { get; } = [];

        /// <summary>The captured outbound email (codes, links).</summary>
        public CapturingEmailSender Emails { get; } = new CapturingEmailSender();

        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);

            // The same harness the standalone factory installs: mail is captured rather than
            // logged, so a test can read a sign-in code, and every request has a client address.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);

                services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(StandaloneFactory.RemoteIpAddress));
            });

            Dictionary<string, string?> settings = new()
            {
                ["TellmaIdentity:Mode"] = "InProc",
                ["TellmaIdentity:PathBase"] = PathBase,
                ["TellmaIdentity:Issuer"] = "http://localhost" + PathBase,
                ["TellmaIdentity:ConnectionString"] = "Server=unused;Database=unused;Encrypt=False",
                ["TellmaIdentity:Keys:Signing:Source"] = "DevelopmentSelfSigned",
                ["TellmaIdentity:Keys:Encryption:Source"] = "DevelopmentSelfSigned",
                ["TellmaIdentity:Development:AllowDevelopmentCertificates"] = "true",
                ["TellmaIdentity:Development:AllowInsecureHttp"] = "true",
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
