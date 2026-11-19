// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Provisioning;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     The permission drift scan against a real tenant database: a filter over a column the
    ///     tenant's model no longer has, and a filter over a language the tenant does not hold, each
    ///     reported with its role and the engine's diagnostic; a sound permission never.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class PermissionDriftScanTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_filter_over_a_language_the_tenant_does_not_hold_drifts_and_one_over_its_primary_does_not()
        {
            // The fixture's tenants hold their primary language alone, so the second name is not in their schema.
            int role = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, "Name2 = 'x'"), (CoreResources.Role, AccessActions.Save, "Name = 'x'"));

            IReadOnlyList<PermissionDriftItem> drifted = await ScanAsync(role);

            PermissionDriftItem item = Assert.Single(drifted);
            Assert.Equal((role, AccessProblemCode.FilterInvalid), (item.RoleId, item.Code));
            Assert.Contains(item.Diagnostics, static diagnostic => diagnostic.Arguments.Any(static argument => argument.Value == "Name2"));
        }

        [Fact]
        [Trait("Category", "Nightly")]
        public async Task A_filter_over_a_renamed_column_drifts_and_the_migrator_s_report_names_it()
        {
            // A stored filter written before its column was renamed: it names what the model no longer has.
            int role = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, "FormerName = 'x'"));
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Roles] SET [Code] = N'renamed' WHERE [Id] = {role};"));

            PermissionDriftItem item = Assert.Single(await ScanAsync(role));
            Assert.Equal(AccessProblemCode.FilterInvalid, item.Code);
            Assert.Equal("renamed", item.RoleCode);
            Assert.Contains(item.Diagnostics, static diagnostic => diagnostic.Arguments.Any(static argument => argument.Value == "FormerName"));

            // --- The line the migrator prints for the tenant ---
            await using AsyncServiceScope scope = await fixture.ScopeAsync("system", kind: PrincipalKind.System);
            IReadOnlyList<string> report = await scope.ServiceProvider.GetRequiredService<ITenantDriftReport>().ScanAsync(Token);
            string line = Assert.Single(report, static candidate => candidate.Contains("(renamed)", StringComparison.Ordinal));
            Assert.StartsWith(string.Create(CultureInfo.InvariantCulture, $"Permission {item.PermissionId} of role {role} (renamed) grants nothing: FilterInvalid; "), line, StringComparison.Ordinal);
            Assert.Contains("FormerName", line, StringComparison.Ordinal);
        }

        /// <summary>Scans the first tenant in the platform's own scope and keeps one role's items.</summary>
        /// <param name="roleId">The role.</param>
        /// <returns>The role's drifted permissions.</returns>
        private async Task<IReadOnlyList<PermissionDriftItem>> ScanAsync(int roleId)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync("system", kind: PrincipalKind.System);
            IReadOnlyList<PermissionDriftItem> drifted = await scope.ServiceProvider.GetRequiredService<IPermissionDriftScanner>().ScanAsync(Token);
            return [.. drifted.Where(item => item.RoleId == roleId)];
        }
    }
}
