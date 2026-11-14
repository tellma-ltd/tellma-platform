// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Testing.Support.Http;
using Tellma.Testing.Support.Logging;

namespace Tellma.Core.AspNetCore.Tests.Observability
{
    /// <summary>
    ///     A credential an outbound request carries — in a header or in its query string — reaches
    ///     no log of the host's, rendered or structured, even with every category logged at Trace
    ///     and for a client the host knows nothing about.
    /// </summary>
    public sealed class OutboundHttpLoggingTests
    {
        /// <summary>A bearer token, distinctive enough that no other text contains it by accident.</summary>
        private const string BearerToken = "never-logged-bearer-8d2f6a1c4e9b";

        /// <summary>A key carried in the query string.</summary>
        private const string QueryKey = "never-logged-query-3b7e0c5a9f1d";

        /// <summary>A basic credential's encoded user and password.</summary>
        private const string BasicCredential = "bmV2ZXItbG9nZ2VkOmJhc2ljLTZjMGE=";

        [Fact]
        public async Task A_clients_credentials_reach_no_log_entry_at_trace()
        {
            StateCapturingLoggerProvider logs = new();
            ScriptedHttpMessageHandler wire = new(static (_, _) => ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, "{}"));
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
            {
                // Every category at Trace, in Serilog and in the framework's own filters alike.
                options.Settings["Serilog:MinimumLevel:Default"] = "Verbose";
                options.Settings["Logging:LogLevel:Default"] = "Trace";

                // A client registered after the composition, as a connector or a later feature
                // would register one: the host's default must reach it without its saying anything.
                options.ConfigureServices = services =>
                {
                    _ = services.AddSingleton<ILoggerProvider>(logs);
                    _ = services.AddHttpClient("third-party").ConfigurePrimaryHttpMessageHandler(() => wire);
                };
            });

            // The capture sees the HTTP client's categories at Trace, so silence below is a finding.
            const string Marker = "tellma-outbound-trace-marker";
            host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("System.Net.Http.HttpClient.third-party.ClientHandler")
                .Log(LogLevel.Trace, default, Marker, null, static (state, _) => state);
            Assert.NotEmpty(logs.Mentioning(Marker));

            using HttpClient client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("third-party");
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"https://api.example.invalid/v1/things?api_key={QueryKey}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);
            request.Headers.TryAddWithoutValidation("Proxy-Authorization", $"Basic {BasicCredential}");
            using HttpResponseMessage response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            // The credentials did travel, through the client the host composed.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            RecordedHttpRequest sent = Assert.Single(wire.Exchanges);
            Assert.Equal($"Bearer {BearerToken}", sent.Header("Authorization"));
            Assert.Contains(QueryKey, sent.RequestUri!.Query, StringComparison.Ordinal);

            Assert.Empty(logs.Mentioning(BearerToken));
            Assert.Empty(logs.Mentioning(QueryKey));
            Assert.Empty(logs.Mentioning(BasicCredential));
        }
    }
}
