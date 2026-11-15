// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Testing.Support.Http;

namespace Tellma.Identity.IntegrationTests
{
    /// <summary>
    ///     A credential an outbound request of the standalone host carries — in a header or in its
    ///     query string — reaches no Serilog event, rendered or as a property, even with every
    ///     category at Verbose and for a client the host knows nothing about.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class OutboundHttpLoggingTests(SqlServerFixture fixture)
    {
        /// <summary>A bearer token, distinctive enough that no other text contains it by accident.</summary>
        private const string BearerToken = "never-logged-bearer-1e7c3a9d5f2b";

        /// <summary>A key carried in the query string.</summary>
        private const string QueryKey = "never-logged-query-6a0f4d8b2c7e";

        [Fact]
        public async Task A_clients_credentials_reach_no_log_event_at_verbose()
        {
            CapturingLogSink logs = new();
            ScriptedHttpMessageHandler wire = new(static (_, _) => ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, "{}"));
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(
                fixture, "idhttplog", new Dictionary<string, string?> { ["Serilog:MinimumLevel:Default"] = "Verbose" });

            // A client registered after the host's composition, as a later feature would register
            // one: the host's default must reach it without its saying anything.
            factory.ServiceOverrides.Add(services =>
            {
                _ = services.AddSingleton<Serilog.Core.ILogEventSink>(logs);
                _ = services.AddHttpClient("third-party").ConfigurePrimaryHttpMessageHandler(() => wire);
            });

            using (factory.CreateClient())
            {
                // The sink sees the HTTP client's categories at Verbose, so silence below is a finding.
                const string Marker = "tellma-outbound-verbose-marker";
                factory.Services.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("System.Net.Http.HttpClient.third-party.ClientHandler")
                    .Log(LogLevel.Trace, default, Marker, null, static (state, _) => state);
                Assert.Contains(logs.Events, static logEvent => Texts(logEvent).Any(static text => text.Contains(Marker, StringComparison.Ordinal)));

                using HttpClient client = factory.Services.GetRequiredService<IHttpClientFactory>().CreateClient("third-party");
                using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"https://api.example.invalid/v1/things?api_key={QueryKey}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);
                using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

                // The credentials did travel, through the client the host composed.
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal($"Bearer {BearerToken}", Assert.Single(wire.Exchanges).Header("Authorization"));
            }

            foreach (string secret in new[] { BearerToken, QueryKey })
            {
                Assert.DoesNotContain(logs.Events, logEvent => Texts(logEvent).Any(text => text.Contains(secret, StringComparison.Ordinal)));
            }
        }

        /// <summary>Every text an event carries as a sink would write it: message, properties, exception.</summary>
        /// <param name="logEvent">The event.</param>
        /// <returns>The texts.</returns>
        private static IEnumerable<string> Texts(LogEvent logEvent)
        {
            yield return logEvent.RenderMessage(CultureInfo.InvariantCulture);
            foreach ((string name, LogEventPropertyValue value) in logEvent.Properties)
            {
                yield return name;

                // A property's rendering includes every element of a sequence and every member of
                // a structure, which is what a structured sink serialises.
                yield return value.ToString();
            }

            if (logEvent.Exception is not null)
            {
                yield return logEvent.Exception.ToString();
            }
        }
    }
}
