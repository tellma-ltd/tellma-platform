// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Analyzers.Tests.Infrastructure;

namespace Tellma.Core.Analyzers.Tests
{
    /// <summary>TELLMA0002: the tenant context is queried and saved only by the runtime and the migrators.</summary>
    public class TenantContextAnalyzerTests
    {
        /// <summary>A use of the context outside the runtime and the migrators.</summary>
        private const string Uses = """
            using System.Linq;
            using System.Threading.Tasks;
            using Microsoft.EntityFrameworkCore;
            using Tellma.Core.Abstractions.Jobs;
            using Tellma.Core.Data;

            class Reports
            {
                async Task Run(TellmaDbContext context)
                {
                    _ = {|TELLMA0002:context.Set<Job>()|}.Count();
                    _ = {|TELLMA0002:context.Jobs|}.Count();
                    _ = {|TELLMA0002:context.Find<Job>(1)|};
                    _ = {|TELLMA0002:context.SaveChanges()|};
                    _ = await {|TELLMA0002:context.SaveChangesAsync()|};
                    _ = await {|TELLMA0002:context.Database.ExecuteSqlRawAsync("DELETE FROM [core].[Jobs]")|};
                    _ = {|TELLMA0002:context.Database.SqlQueryRaw<int>("SELECT 1")|}.ToList();
                }
            }
            """;

        /// <summary>Queries, saves, entity sets and raw EF SQL over the context are refused in a distribution's code.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Queries_and_saves_over_the_context_are_refused()
        {
            return AnalyzerHarness.VerifyAsync<TenantContextAnalyzer>(Uses);
        }

        /// <summary>The runtime and a migrator own the context.</summary>
        /// <param name="assemblyName">The owning assembly's name.</param>
        /// <returns>A task that completes when the check has passed.</returns>
        [Theory]
        [InlineData("Tellma.Core")]
        [InlineData("Acme.Migrator")]
        public Task The_runtime_and_a_migrator_use_the_context(string assemblyName)
        {
            return AnalyzerHarness.VerifyAsync<TenantContextAnalyzer>(Without(Uses), assemblyName);
        }

        /// <summary>Building the model and querying another context are not uses of the tenant context.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Other_contexts_and_model_reads_pass()
        {
            return AnalyzerHarness.VerifyAsync<TenantContextAnalyzer>("""
                using System.Linq;
                using Microsoft.EntityFrameworkCore;
                using Tellma.Core.Abstractions.Jobs;
                using Tellma.Core.Data;

                class Catalog : DbContext
                {
                    public DbSet<Job> Jobs { get; set; } = null!;
                }

                class Uses
                {
                    void Run(Catalog catalog, TellmaDbContext context)
                    {
                        _ = catalog.Set<Job>().Count();
                        _ = catalog.Jobs.Count();
                        _ = catalog.SaveChanges();
                        _ = context.Model.GetEntityTypes().Count();
                        _ = nameof(TellmaDbContext.SaveChanges);
                    }
                }
                """);
        }

        /// <summary>Strips the markup from a source.</summary>
        /// <param name="source">The source.</param>
        /// <returns>The source without its expected diagnostics.</returns>
        private static string Without(string source)
        {
            return source.Replace("{|TELLMA0002:", string.Empty, StringComparison.Ordinal).Replace("|}", string.Empty, StringComparison.Ordinal);
        }
    }
}
