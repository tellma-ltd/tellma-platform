// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Blobs;
using Tellma.Core.Excel;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The <c>excel</c> and <c>excel.deployment</c> startup checks: a warning, never a problem,
    ///     for each server-owned property excluded from Excel, and a scratch directory that does not
    ///     exist a problem with its fix.
    /// </summary>
    public sealed class ExcelCheckTests
    {
        [Fact]
        public async Task The_model_check_needs_no_infrastructure_and_the_deployment_check_does()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);

            Assert.Equal(
                [("excel", false), ("excel.deployment", true)],
                provider.GetServices<IStartupCheck>().OfType<ExcelCheck>().Select(static check => (check.Name, check.RequiresInfrastructure)));
        }

        [Fact]
        public async Task A_server_owned_property_excluded_from_excel_is_a_warning_and_an_editable_one_is_not()
        {
            IServiceCollection services = TestComposition.Compose(static tellma => tellma.AddFeature(new TestFeature<Tag.D>("xc", contribute: static contribution => contribution.Entity<XcLedger>())));
            _ = services.AddFakeLogging();
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            Assert.Empty(await CheckAsync(provider, deployment: false));

            FakeLogRecord warning = Assert.Single(provider.GetFakeLogCollector().GetSnapshot(), static record => record.Id.Name == "RedundantExcelExclusion");
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Equal(
                "The property xc.XcLedger.Digest is ServerOwned, so it is already left out of the editable export and its exclusion from Excel is redundant.",
                warning.Message);
        }

        [Fact]
        public async Task A_scratch_directory_that_takes_a_file_passes()
        {
            string directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tellma-excel-tests", Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                await using ServiceProvider provider = WithScratch(directory);

                Assert.Empty(await CheckAsync(provider, deployment: true));
                Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task A_scratch_directory_that_does_not_exist_is_a_problem_with_its_fix()
        {
            string missing = Path.Combine(Path.GetTempPath(), "tellma-excel-tests", Guid.NewGuid().ToString("N"));
            await using ServiceProvider provider = WithScratch(missing);

            CompositionProblem problem = Assert.Single(await CheckAsync(provider, deployment: true));

            Assert.Equal(("excel.deployment", $"The scratch directory '{missing}' does not exist."), (problem.Source, problem.Problem));
            Assert.Contains(ScratchDirectory.ConfigurationKey, problem.Fix, StringComparison.Ordinal);
            Assert.False(Directory.Exists(missing));
        }

        /// <summary>A composition whose scratch directory is the given one.</summary>
        /// <param name="directory">The directory.</param>
        /// <returns>The services.</returns>
        private static ServiceProvider WithScratch(string directory)
        {
            IServiceCollection services = TestComposition.Compose(static _ => { });
            _ = services.Replace(ServiceDescriptor.Singleton(new ScratchDirectory(directory)));
            return services.BuildServiceProvider(validateScopes: true);
        }

        /// <summary>Runs one of the two Excel checks.</summary>
        /// <param name="provider">The composition's services.</param>
        /// <param name="deployment">True for the deployment's check.</param>
        /// <returns>Its problems.</returns>
        private static Task<IReadOnlyList<CompositionProblem>> CheckAsync(ServiceProvider provider, bool deployment)
        {
            ExcelCheck check = provider.GetServices<IStartupCheck>().OfType<ExcelCheck>().Single(check => check.RequiresInfrastructure == deployment);
            return check.CheckAsync(provider, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>A ledger of the check's model: one server-owned and one editable property, each excluded from Excel.</summary>
    [Table("Ledgers", Schema = "xc")]
    [TableType]
    public class XcLedger : TopLevelEntity
    {
        /// <summary>The ledger's name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>A digest the server computes, whose exclusion is redundant.</summary>
        [MaxLength(64)]
        [ServerOwned]
        [ExcludeFromExcel]
        public string? Digest { get; set; }

        /// <summary>A note the client writes and the workbooks leave out.</summary>
        [MaxLength(255)]
        [ExcludeFromExcel]
        public string? Note { get; set; }
    }
}
