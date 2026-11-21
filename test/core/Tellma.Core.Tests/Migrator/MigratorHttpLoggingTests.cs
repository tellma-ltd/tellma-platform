// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Migrator;
using Tellma.Core.Tests.Infrastructure;
using Tellma.Testing.Support.Http;
using Tellma.Testing.Support.Logging;

namespace Tellma.Core.Tests.Migrator
{
    /// <summary>
    ///     A credential an outbound request of the migrator carries, in a header or in its query
    ///     string, reaches no log entry at Trace: in a command's host and in the client-asset emit's
    ///     composition alike, for a client the distribution's composition registers.
    /// </summary>
    /// <remarks>
    ///     The request goes out from a startup check, which both runs execute before anything else,
    ///     through the host's own client factory; the check then refuses the start, so no command
    ///     reaches for a database.
    /// </remarks>
    public sealed class MigratorHttpLoggingTests
    {
        /// <summary>A bearer token, distinctive enough that no other text contains it by accident.</summary>
        private const string BearerToken = "never-logged-migrator-bearer-2c9e7a";

        /// <summary>A key carried in the query string.</summary>
        private const string QueryKey = "never-logged-migrator-query-5d1b8f";

        /// <summary>The named client the composition registers.</summary>
        private const string ClientName = "third-party";

        /// <summary>A Trace event on the HTTP client's category, proving the capture sees that level.</summary>
        private const string Marker = "tellma-migrator-trace-marker";

        [Theory]
        [InlineData("status")]
        [InlineData("emit-client-assets")]
        public async Task A_clients_credentials_reach_no_log_entry_at_trace(string command)
        {
            StateCapturingLoggerProvider logs = new();
            ScriptedHttpMessageHandler wire = new(static (_, _) => ScriptedHttpMessageHandler.Respond(HttpStatusCode.OK, "{}"));
            string folder = Path.Combine(Path.GetTempPath(), "tellma-emit-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
            string[] args = command == "status" ? ["status"] : ["emit-client-assets", folder];

            // The distribution's composition registers the client, as a connector does, and the
            // capture: the emit composes from it alone.
            void Compose(TellmaBuilder tellma)
            {
                _ = tellma.Services.AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
                _ = tellma.Services.AddHttpClient(ClientName).ConfigurePrimaryHttpMessageHandler(() => wire);
                _ = tellma.AddFeature(new TestFeature<Tag.A>("probe", contribute: static c => c.Singleton<IStartupCheck, OutboundProbe>()));
            }

            Dictionary<string, string?> configuration = new(TestComposition.SoundConfiguration)
            {
                ["Tellma:DisplayName"] = "Acme ERP",
                ["Logging:LogLevel:Default"] = "Trace",
            };

            try
            {
                using StringWriter output = new(CultureInfo.InvariantCulture);
                using StringWriter error = new(CultureInfo.InvariantCulture);
                int code = await TellmaMigrator.RunAsync(
                    args,
                    TestComposition.Slug,
                    Compose,
                    migrateIdentityStore: null,
                    new MigratorRunSettings
                    {
                        MigrationsAssembly = typeof(MigratorHttpLoggingTests).Assembly,
                        Out = output,
                        Error = error,
                        EnvironmentName = "Production",
                        ContentRootPath = AppContext.BaseDirectory,
                        ConfigureConfiguration = builder => builder.AddInMemoryCollection(configuration),

                        // A command's host, configured after the composition: the host's default
                        // providers go, and the capture the composition added comes back with them.
                        ConfigureServices = collection => collection.AddLogging(logging => logging.ClearProviders().AddProvider(logs)),
                    },
                    TestContext.Current.CancellationToken);

                // The probe ran, sent, and stopped the run before the command.
                Assert.Equal(MigratorExitCodes.CompositionProblems, code);
                Assert.Contains("[test.outbound]", error.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }

            // The credentials did travel, through the client the host composed, and Trace reached
            // the capture, so silence below is a finding.
            RecordedHttpRequest sent = Assert.Single(wire.Exchanges);
            Assert.Equal($"Bearer {BearerToken}", sent.Header("Authorization"));
            Assert.NotEmpty(logs.Mentioning(Marker));

            Assert.Empty(logs.Mentioning(BearerToken));
            Assert.Empty(logs.Mentioning(QueryKey));
        }

        /// <summary>
        ///     A composition-only startup check that logs the Trace marker, sends one request carrying
        ///     the credentials through the host's client factory, and refuses the start.
        /// </summary>
        /// <param name="clients">The host's client factory.</param>
        /// <param name="loggers">The host's logger factory.</param>
        private sealed class OutboundProbe(IHttpClientFactory clients, ILoggerFactory loggers) : IStartupCheck
        {
            /// <inheritdoc />
            public string Name => "test.outbound";

            /// <inheritdoc />
            public bool RequiresInfrastructure => false;

            /// <inheritdoc />
            public async Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
            {
                loggers.CreateLogger($"System.Net.Http.HttpClient.{ClientName}.ClientHandler")
                    .Log(LogLevel.Trace, default, Marker, null, static (state, _) => state);

                using HttpClient client = clients.CreateClient(ClientName);
                using HttpRequestMessage request = new(HttpMethod.Get, new Uri($"https://api.example.invalid/v1/things?api_key={QueryKey}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);
                using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

                return [new CompositionProblem(Name, "The outbound probe sent its request.", null)];
            }
        }
    }
}
