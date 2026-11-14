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

        /// <summary>The name of the activity source each run starts its command under.</summary>
        private const string RunSourceName = "Tellma.Core.IntegrationTests.SqlQueryText";

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

        /// <summary>
        ///     Runs one command under the host's tracing, inside a parent activity of the run's own, and
        ///     collects the ended spans of that parent's children: the instrumentation hands every
        ///     provider the spans of every command in the process, other test classes' included.
        /// </summary>
        /// <param name="withoutEnrichment">Whether to remove the host's enrichment afterwards.</param>
        /// <returns>The spans of the run's own command.</returns>
        private async Task<List<Activity>> RunAsync(bool withoutEnrichment)
        {
            ServiceCollection services = [];
            ObservabilityComposition.Register(
                services,
                new ConfigurationBuilder().Build(),
                new DeploymentIdentity("acme", "Production"),
                new DeploymentVersions("1.0.0", "1.0.0"));

            // --- The host's tracing, plus the run's own source and a processor that keeps what ends ---
            List<Activity> spans = [];
            _ = services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(RunSourceName).AddProcessor(new Capture(spans)));
            if (withoutEnrichment)
            {
                _ = services.PostConfigure<SqlClientTraceInstrumentationOptions>(static options => options.EnrichWithSqlCommand = null);
            }

            // --- One command, whose span the run's activity parents ---
            using ActivitySource runs = new(RunSourceName);
            ActivitySpanId parent;
            await using (ServiceProvider provider = services.BuildServiceProvider())
            {
                _ = provider.GetRequiredService<TracerProvider>();
                using Activity? run = runs.StartActivity("run");
                Assert.NotNull(run);
                parent = run.SpanId;
                await using SqlConnection connection = new(sql.MasterConnectionString);
                await connection.OpenAsync(Token);
                await using SqlCommand command = new($"SELECT 1 AS [{Marker}];", connection);
                _ = await command.ExecuteScalarAsync(Token);
            }

            // --- Disposing the provider flushed the spans; only the run's children are its own ---
            // The instrumentation traces every command in the process while any provider is alive,
            // and merges its enrichment across providers: while the control run's provider lives
            // the merged enrichment is none, so another class's command started then carries its
            // text. Such a span can end while this run's provider listens, and is left out here.
            lock (spans)
            {
                return [.. spans.Where(span => span.ParentSpanId == parent)];
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
