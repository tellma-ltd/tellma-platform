// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.SqlClient;
using OpenTelemetry.Trace;
using System.Diagnostics;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.AspNetCore.Observability;

namespace Tellma.Core.IntegrationTests.Observability
{
    /// <summary>
    ///     The web host's tracing against a real SQL Server: a command's span, as the SqlClient
    ///     instrumentation exports it, carries neither the command's text nor its summary.
    /// </summary>
    [Trait("Category", "Integration")]
    public sealed class SqlQueryTextTests(SqlServerFixture sql)
    {
        /// <summary>
        ///     A column name the command carries, which no exported tag may contain. An identifier,
        ///     not a literal: the instrumentation already replaces literals with placeholders.
        /// </summary>
        private const string Marker = "TellmaQueryTextMarker";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_commands_span_carries_no_query_text()
        {
            List<Activity> spans = await RunAsync(withoutEnrichment: false);

            // The span exists, so its tags were set, and none of them carries the command.
            Assert.Contains(spans, static span => span.GetTagItem("db.system.name") is not null);
            Assert.All(spans, static span =>
            {
                Assert.Null(span.GetTagItem("db.query.text"));
                Assert.Null(span.GetTagItem("db.query.summary"));
                Assert.Null(span.GetTagItem("db.statement"));
                Assert.DoesNotContain(span.TagObjects, static tag => tag.Value?.ToString()?.Contains(Marker, StringComparison.Ordinal) == true);
            });
        }

        [Fact]
        public async Task Without_the_host_enrichment_the_span_would_carry_the_command()
        {
            // The control: what the first test proves is removed is there to remove.
            List<Activity> spans = await RunAsync(withoutEnrichment: true);
            Assert.Contains(spans, static span => span.GetTagItem("db.query.text")?.ToString()?.Contains(Marker, StringComparison.Ordinal) == true);
        }

        /// <summary>Runs one command under the host's tracing and collects the ended spans.</summary>
        /// <param name="withoutEnrichment">Whether to remove the host's enrichment afterwards.</param>
        /// <returns>The spans.</returns>
        private async Task<List<Activity>> RunAsync(bool withoutEnrichment)
        {
            ServiceCollection services = [];
            ObservabilityComposition.Register(
                services,
                new ConfigurationBuilder().Build(),
                new DeploymentIdentity("acme", "Production"),
                new DeploymentVersions("1.0.0", "1.0.0"));

            List<Activity> spans = [];
            _ = services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(new Capture(spans)));
            if (withoutEnrichment)
            {
                _ = services.PostConfigure<SqlClientTraceInstrumentationOptions>(static options => options.EnrichWithSqlCommand = null);
            }

            await using (ServiceProvider provider = services.BuildServiceProvider())
            {
                _ = provider.GetRequiredService<TracerProvider>();
                await using SqlConnection connection = new(sql.MasterConnectionString);
                await connection.OpenAsync(Token);
                await using SqlCommand command = new($"SELECT 1 AS [{Marker}];", connection);
                _ = await command.ExecuteScalarAsync(Token);
            }

            // Disposing the provider flushed the spans.
            lock (spans)
            {
                return [.. spans];
            }
        }

        /// <summary>A processor that keeps every span that ends.</summary>
        /// <param name="spans">Where the spans go.</param>
        private sealed class Capture(List<Activity> spans) : BaseProcessor<Activity>
        {
            /// <inheritdoc />
            public override void OnEnd(Activity data)
            {
                lock (spans)
                {
                    spans.Add(data);
                }
            }
        }
    }
}
