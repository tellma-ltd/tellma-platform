// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Excel;
using Tellma.Core.Tests.Localization;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The manifest an editable export writes, read back from the workbook: the header block
    ///     naming the deployment, the source tenant and the tenant's languages, and the column table
    ///     recording each column's path, role, language, referenced entity and key kind; and the
    ///     same-source gate over a manifest's deployment and source tenant.
    /// </summary>
    /// <param name="composition">The fixture model with its labels, composed once for the class.</param>
    public sealed class ExportManifestTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>, IDisposable
    {
        /// <summary>The scratch directory child sheets spool to, one per test.</summary>
        private readonly string _scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tellma-excel-tests", Guid.NewGuid().ToString("N"))).FullName;

        [Fact]
        public async Task The_template_records_the_source_and_every_columns_path_role_language_and_key_kind()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            ExportPlanner planner = new(composition.Services.GetRequiredService<IValueSetRegistry>(), "1.2.3");
            ExportForImportRequest request = new()
            {
                Ids = [],
                Columns = ["Name"],
                Children = [new ExportCollection(nameof(User.RoleMemberships), Columns: null, new Dictionary<string, string> { ["RoleId"] = "Name2" }, Children: null)],
            };
            ExcelWorkbookPlan plan = planner.PlanEditable(scope.Metadata.Get(typeof(User)), request, cap: 10, scope.Context);

            using WrittenWorkbook workbook = new(await Write(plan, scope.Context));
            IReadOnlyList<IReadOnlyDictionary<string, ExcelRawCell>> rows = workbook.Rows("_tellma");

            // --- The header block: the source deployment and tenant, and the tenant's languages ---
            var header = rows.Take(13).ToDictionary(static row => row["A"].Text, static row => row["B"].Text, StringComparer.Ordinal);
            Assert.Equal(("ForImport", "core.User", "deployment-a", "7", "en, ar"), (header["Shape"], header["Entity"], header["Deployment"], header["SourceTenantId"], header["Languages"]));
            Assert.Equal("1.2.3", header["Platform"]);

            // --- The column table: the parent's twins by language, the memberships' reference in its key ---
            IReadOnlyDictionary<string, ExcelRawCell>[] columns = [.. rows.Skip(15)];
            Assert.Equal(
                [("Id", "Id", ""), ("Stamp", "Stamp", ""), ("Email", "Property", ""), ("Name", "Property", "en"), ("Name", "Property", "ar")],
                columns.Where(static row => !row.ContainsKey("B")).Select(static row => (row["E"].Text, row["G"].Text, Text(row, "H"))));
            IReadOnlyDictionary<string, ExcelRawCell> role = columns.Single(static row => Text(row, "B") == "RoleMemberships" && row["G"].Text == "Reference");
            Assert.Equal(("Role.Name", "ar", "core.Role", "Natural"), (role["E"].Text, role["H"].Text, role["I"].Text, role["J"].Text));
            IReadOnlyDictionary<string, ExcelRawCell> owner = columns.Single(static row => Text(row, "B") == "RoleMemberships" && row["G"].Text == "ParentReference");
            Assert.Equal(("User.Id", "core.User"), (owner["E"].Text, owner["I"].Text));
        }

        [Fact]
        public void A_file_is_same_source_when_it_names_this_deployment_and_the_target_tenant()
        {
            Dictionary<string, string> manifest = new(StringComparer.Ordinal)
            {
                [ExcelManifest.DeploymentKey] = "deployment-a",
                [ExcelManifest.SourceTenantIdKey] = "7",
            };

            Assert.True(ExcelManifest.IsSameSource(manifest, "deployment-a", 7));
            Assert.False(ExcelManifest.IsSameSource(manifest, "deployment-a", 8));
            Assert.False(ExcelManifest.IsSameSource(manifest, "Deployment-A", 7));
            Assert.False(ExcelManifest.IsSameSource(manifest, "deployment-b", 7));
        }

        [Theory]
        [InlineData(null, "7")]
        [InlineData("deployment-a", null)]
        [InlineData("deployment-a", " 7")]
        [InlineData("deployment-a", "+7")]
        [InlineData("deployment-a", "7.0")]
        [InlineData("deployment-a", "seven")]
        public void A_manifest_missing_either_key_or_naming_the_tenant_loosely_is_foreign(string? deployment, string? tenant)
        {
            Dictionary<string, string> manifest = new(StringComparer.Ordinal);
            if (deployment is not null)
            {
                manifest[ExcelManifest.DeploymentKey] = deployment;
            }

            if (tenant is not null)
            {
                manifest[ExcelManifest.SourceTenantIdKey] = tenant;
            }

            Assert.False(ExcelManifest.IsSameSource(manifest, "deployment-a", 7));
            Assert.False(ExcelManifest.IsSameSource(null, "deployment-a", 7));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Directory.Delete(_scratch, recursive: true);
        }

        /// <summary>A cell's text; empty when the row has no such cell.</summary>
        /// <param name="row">The row.</param>
        /// <param name="column">The column letter.</param>
        /// <returns>The text.</returns>
        private static string Text(IReadOnlyDictionary<string, ExcelRawCell> row, string column)
        {
            return row.TryGetValue(column, out ExcelRawCell cell) ? cell.Text : string.Empty;
        }

        /// <summary>Writes a plan whose queries answer one page without rows, as a template's do.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="context">The context.</param>
        /// <returns>The workbook's bytes.</returns>
        private async Task<byte[]> Write(ExcelWorkbookPlan plan, ExcelContext context)
        {
            using MemoryStream destination = new();
            _ = await new WorkbookWriter(new ExcelOptions(), _scratch).WriteAsync(plan, new EmptySource(), context, () => destination, TestContext.Current.CancellationToken);
            return destination.ToArray();
        }

        /// <summary>A row source whose every query answers one page without rows.</summary>
        private sealed class EmptySource : IExcelRowSource
        {
            /// <inheritdoc />
            public async IAsyncEnumerable<ExcelPage> Pages(ExcelQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                yield return Empty(query) with { Count = query.CountCap is null ? null : 0 };
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>>> ReadAsync(IReadOnlyList<ExcelQuery> queries, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            /// <summary>A page without rows, its dependents parallel to the query's.</summary>
            /// <param name="query">The query.</param>
            /// <returns>The page.</returns>
            private static ExcelPage Empty(ExcelQuery query)
            {
                return new ExcelPage([], [.. (query.Dependents ?? []).Select(Empty)], Count: null);
            }
        }
    }
}
