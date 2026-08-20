// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;
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

        /// <summary>Extra service registrations applied after the test defaults (fault injection).</summary>
        public IList<Action<IServiceCollection>> ServiceOverrides { get; } = [];

        /// <summary>
        ///     The client address every test request appears to come from. <c>TestServer</c> sets
        ///     none, so anything reading the connection address — per-IP rate limits, audit rows —
        ///     would otherwise be untestable and silently null.
        /// </summary>
        public static string RemoteIpAddress => "203.0.113.7";

        /// <inheritdoc />
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);

            // The host under test is the real entry point, which loads the developer's user secrets
            // because the environment is Development. Drop that source: a machine-local secret — a
            // seeded client, another issuer — would otherwise decide what the suite exercises, so a
            // green run on one machine would say nothing about any other.
            builder.ConfigureAppConfiguration(configuration =>
            {
                foreach (IConfigurationSource source in configuration.Sources.ToList())
                {
                    if (source is JsonConfigurationSource { Path: "secrets.json" })
                    {
                        configuration.Sources.Remove(source);
                    }
                }
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(Emails);

                // Route the back-channel logout client through the recording handler.
                services.AddHttpClient(Identity.Services.BackchannelLogout.BackchannelLogoutService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => BackchannelLogouts);

                services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(RemoteIpAddress));

                foreach (Action<IServiceCollection> configure in ServiceOverrides)
                {
                    configure(services);
                }
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
