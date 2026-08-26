// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Inspection
{
    /// <summary>
    ///     The playground host: Kestrel on an ephemeral port serving one static page, plus the
    ///     endpoints that compile whatever the page sends.
    /// </summary>
    internal static class Program
    {
        /// <summary>Starts the playground and blocks until it is shut down.</summary>
        /// <param name="args">Command-line arguments; see the project README.</param>
        /// <returns>A task that completes when the host stops.</returns>
        internal static async Task Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            // Port 0 lets the OS choose: parallel worktrees have to be able to run this at the same
            // time, and nothing here may write a chosen port into a tracked file.
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");

            WebApplication app = builder.Build();
            app.UseDefaultFiles();
            app.UseStaticFiles();

            // Supplied on the command line rather than read from a file, so that a connection
            // string never ends up somewhere it could be committed. Without one the tool compiles
            // and shows, and opens no connection at all.
            string? connection = app.Configuration["connection"];

            if (!string.IsNullOrWhiteSpace(connection))
            {
                // Deployed once at startup rather than on demand, so that the first query somebody
                // runs answers from the same rows the conformance suites use.
                string? problem = await Fixture.DeployAsync(connection);
                Console.WriteLine(problem is null
                    ? "Fixture deployed; compiled queries will run against it."
                    : "Could not deploy the fixture: " + problem);
            }

            app.MapPost(
                "/api/inspect",
                async (InspectRequest request) => await Inspection.InspectAsync(request, connection));

            app.MapGet("/api/schema", () => LedgerFixture.Schema.Entities.Select(entity => new
            {
                entity.Name,
                entity.Source,
                Properties = entity.Properties.Select(property => new
                {
                    property.Name,
                    Type = property.Type.ToString(),
                    property.IsNotNull,
                    property.IsUnique,
                }),
                Navigations = entity.Navigations.Select(navigation => new
                {
                    navigation.Name,
                    Target = navigation.Target.Name,
                    ForeignKey = navigation.ForeignKey.Name,
                }),
            }));

            await app.RunAsync();
        }
    }
}
