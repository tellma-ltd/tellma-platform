// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;
using Tellma.Core.Migrator;
using Tellma.Distro.Acme.TestSupport;

namespace Tellma.Core.AspNetCore.IntegrationTests.Security
{
    /// <summary>
    ///     The verdict matrix: the deployment's third tenant driven by the migrator, as an operator
    ///     drives it, from <c>Provisioning</c> through <c>Active</c>, <c>ReadOnly</c>, <c>Frozen</c>,
    ///     <c>Suspended</c> and <c>Retired</c>, and in each state the administrator's read, save, and
    ///     locale change (an action a soft lock allows) sent through the composed host.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class TenantStateTests(AcmeDeployment deployment)
    {
        [Fact]
        public async Task Each_tenant_state_serves_or_refuses_a_read_a_save_and_an_action_allowed_when_read_only()
        {
            int tenant = deployment.StateTenant;
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);

            // --- Provisioning: no tenant to anyone ---
            await AssertAsync(admin, tenant, read: TenantProblemCodes.NotFound, save: TenantProblemCodes.NotFound, locale: TenantProblemCodes.NotFound);

            // --- The provisioning resumed, its invitation answered this time: Active serves everything ---
            MigratorResult resumed = await deployment.Acme.RunAsync(
                "provision", "--tenant", tenant.ToString(CultureInfo.InvariantCulture), "--admin-email", AcmeSignIn.Administrator);
            Assert.True(resumed.ExitCode == MigratorExitCodes.Converged, resumed.ToString());
            Assert.Equal(nameof(TenantState.Active), await deployment.Acme.StateAsync(tenant));
            await deployment.RefreshCatalogAsync();
            await AssertAsync(admin, tenant, read: null, save: null, locale: null);

            // --- ReadOnly: reads and the allowed action served, the save refused ---
            await MoveAsync(tenant, TenantState.ReadOnly);
            await AssertAsync(admin, tenant, read: null, save: TenantProblemCodes.ReadOnly, locale: null);

            // --- Frozen: reads served, every mutation refused ---
            await MoveAsync(tenant, TenantState.Frozen);
            await AssertAsync(admin, tenant, read: null, save: TenantProblemCodes.Frozen, locale: TenantProblemCodes.Frozen);

            // --- Suspended: everything refused ---
            await MoveAsync(tenant, TenantState.Suspended);
            await AssertAsync(admin, tenant, read: TenantProblemCodes.Suspended, save: TenantProblemCodes.Suspended, locale: TenantProblemCodes.Suspended);

            // --- Retired: no tenant to anyone ---
            await MoveAsync(tenant, TenantState.Retired);
            await AssertAsync(admin, tenant, read: TenantProblemCodes.NotFound, save: TenantProblemCodes.NotFound, locale: TenantProblemCodes.NotFound);
        }

        /// <summary>Moves the tenant with the migrator's <c>set-state</c>, then refreshes the host's snapshot.</summary>
        /// <param name="tenant">The tenant.</param>
        /// <param name="state">The new state.</param>
        /// <returns>A task that completes when the host holds the state.</returns>
        private async Task MoveAsync(int tenant, TenantState state)
        {
            await deployment.Acme.SetStateAsync(tenant, state, "verdict matrix");
            await deployment.RefreshCatalogAsync();
        }

        /// <summary>Sends the three requests and asserts each was served or refused with its code.</summary>
        /// <param name="admin">The administrator.</param>
        /// <param name="tenant">The tenant.</param>
        /// <param name="read">The read's refusal; null when it is served.</param>
        /// <param name="save">The save's refusal; null when it is served.</param>
        /// <param name="locale">The locale change's refusal; null when it is served.</param>
        /// <returns>A task that completes when the assertions ran.</returns>
        private static async Task AssertAsync(SpaClient admin, int tenant, string? read, string? save, string? locale)
        {
            string code = "TS-" + Guid.NewGuid().ToString("N")[..8];
            Assert.Multiple(
                await VerdictAsync(admin.PostAsync(tenant, "centers/query", new JsonObject { ["select"] = "Code", ["take"] = 1 }), read, "read"),
                await VerdictAsync(admin.PostAsync(tenant, "centers/save", Wire.Save(new JsonObject { ["id"] = 0, ["code"] = code, ["name"] = code, ["centerType"] = "Sale" })), save, "save"),
                await VerdictAsync(admin.PostAsync(tenant, "users/me/locale", new JsonObject { ["timeZone"] = "Africa/Addis_Ababa" }), locale, "locale"));
        }

        /// <summary>The assertion of one request's verdict.</summary>
        /// <param name="request">The request.</param>
        /// <param name="refusal">Its refusal's code; null when it is served.</param>
        /// <param name="name">The request's name, for a failure's message.</param>
        /// <returns>The assertion.</returns>
        private static async Task<Action> VerdictAsync(Task<ApiResponse> request, string? refusal, string name)
        {
            ApiResponse response = await request;
            HttpStatusCode status = refusal switch
            {
                null => HttpStatusCode.OK,
                TenantProblemCodes.NotFound => HttpStatusCode.NotFound,
                _ => HttpStatusCode.Forbidden,
            };
            return () => Assert.True(
                response.Status == status && (refusal is null || response.Code == refusal),
                $"The {name} expected {(int)status} {refusal}, answered {response}");
        }
    }
}
