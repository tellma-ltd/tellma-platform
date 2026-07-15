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
using Tellma.Identity.Services.Email;
using Tellma.Identity.Web;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Boots the standalone <c>Tellma.Identity.Web</c> host in-memory with test-safe
    ///     configuration: development certificates, HTTP allowed, a capturing email sink, and no
    ///     startup seeding or migrations unless a test opts in (via
    ///     <see cref="ConfigurationOverrides" />).
    /// </summary>
    public class StandaloneFactory : WebApplicationFactory<WebHostMarker>
    {
        /// <summary>Extra configuration applied on top of the test defaults.</summary>
        public Dictionary<string, string?> ConfigurationOverrides { get; } = [];

        /// <summary>The captured outbound email (codes, links).</summary>
        public CapturingEmailSender Emails { get; } = new CapturingEmailSender();

        /// <summary>Captures back-channel logout tokens delivered to distributions.</summary>
        public RecordingBackchannelHandler BackchannelLogouts { get; } = new RecordingBackchannelHandler();

        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);

                // Route the back-channel logout client through the recording handler.
                services.AddHttpClient(Identity.Services.BackchannelLogout.BackchannelLogoutService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => BackchannelLogouts);
            });

            Dictionary<string, string?> settings = new()
            {
                ["TellmaIdentity:Mode"] = "Standalone",
                ["TellmaIdentity:Issuer"] = "http://localhost",
                ["TellmaIdentity:PathBase"] = "",
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
