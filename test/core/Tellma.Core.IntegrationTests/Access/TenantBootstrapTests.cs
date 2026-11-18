// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     The administrator bootstrap against a real tenant database: once per address however
    ///     often it runs, an existing user granted the role, a development subject recorded as an
    ///     invitation, and a subject refused outside development; and the provisioning step that
    ///     calls it.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class TenantBootstrapTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_bootstrap_creates_the_administrator_once_however_often_it_runs()
        {
            // --- Twice, the second time with the address in another case ---
            int first = await BootstrapAsync(new TenantBootstrapRequest(" Boss@Example.com ", "Boss", "en", null));
            int second = await BootstrapAsync(new TenantBootstrapRequest("boss@example.com", "Boss", "en", null));

            // --- One user, new until invited, with one permanent administrator membership and a stamp row ---
            Assert.Equal(first, second);
            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM [core].[Users] WHERE [Email] = N'boss@example.com';"));
            Assert.Equal(nameof(UserState.New), await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, Where("SELECT [State] FROM [core].[Users]", first, "Id")));
            Assert.Equal(1, await CountAsync(Where("SELECT COUNT(*) FROM [core].[RoleMemberships]", first, "UserId", "AND [RoleId] = 1 AND [StartsAt] IS NULL AND [EndsAt] IS NULL")));
            Assert.Equal(1, await CountAsync(Where("SELECT COUNT(*) FROM [core].[UserStamps]", first, "UserId")));
        }

        [Fact]
        public async Task An_existing_user_keeps_their_roles_and_gains_a_permanent_administrator_membership_once()
        {
            (int ann, _) = await fixture.SeedCallerAsync("Ann");
            await fixture.SeedMembershipAsync(ann, WellKnownIds.AdministratorRoleId, endsAt: "DATEADD(day, 30, SYSDATETIMEOFFSET())");
            string email = await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, Where("SELECT [Email] FROM [core].[Users]", ann, "Id")) ?? throw new InvalidOperationException("The seeded user has no email address.");

            Assert.Equal(ann, await BootstrapAsync(new TenantBootstrapRequest(email, "Ann", "en", null)));
            Assert.Equal(ann, await BootstrapAsync(new TenantBootstrapRequest(email, "Ann", "en", null)));

            // The ending membership gave way to a permanent one; the seeded role stays.
            Assert.Equal(1, await CountAsync(Where("SELECT COUNT(*) FROM [core].[RoleMemberships]", ann, "UserId", "AND [RoleId] = 1 AND [EndsAt] IS NULL")));
            Assert.Equal(0, await CountAsync(Where("SELECT COUNT(*) FROM [core].[RoleMemberships]", ann, "UserId", "AND [RoleId] = 1 AND [EndsAt] IS NOT NULL")));
            Assert.Equal(1, await CountAsync(Where("SELECT COUNT(*) FROM [core].[RoleMemberships]", ann, "UserId", "AND [RoleId] <> 1")));
        }

        [Fact]
        public async Task A_development_subject_is_recorded_as_an_invitation_in_the_same_save()
        {
            int id = await BootstrapAsync(new TenantBootstrapRequest("dev@example.com", "Dev", "en", "dev-subject-1"));

            Assert.Equal("dev-subject-1", await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, Where("SELECT [Subject] FROM [core].[Users]", id, "Id")));
            Assert.Equal(nameof(UserState.Invited), await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, Where("SELECT [State] FROM [core].[Users]", id, "Id")));
            Assert.Equal(nameof(InviteStatus.Invited), await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, Where("SELECT [InviteStatus] FROM [core].[Users]", id, "Id")));
        }

        [Fact]
        public async Task A_subject_is_refused_outside_a_development_environment_and_nothing_is_written()
        {
            await using ServiceProvider deployed = fixture.CreateInstance(static services => services.Replace(ServiceDescriptor.Singleton(new TenancyEnvironment("fixture", IsDevelopment: false))));

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => BootstrapAsync(new TenantBootstrapRequest("prod@example.com", "Prod", "en", "prod-subject"), deployed));
            Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM [core].[Users] WHERE [Email] = N'prod@example.com';"));
        }

        [Fact]
        public async Task The_provisioning_step_bootstraps_the_run_s_administrator_by_the_address_s_local_part()
        {
            await using (AsyncServiceScope scope = await fixture.ScopeAsync("system", kind: PrincipalKind.System))
            {
                BootstrapAdministratorStep step = ActivatorUtilities.CreateInstance<BootstrapAdministratorStep>(scope.ServiceProvider);
                await step.RunAsync(new TenantProvisioningContext(ConnectDatabase.Descriptor(ConnectDatabase.TenantA), scope.ServiceProvider, IsNew: true, null, "Step.Owner@Example.com"));
                await step.RunAsync(new TenantProvisioningContext(ConnectDatabase.Descriptor(ConnectDatabase.TenantA), scope.ServiceProvider, IsNew: true, null, null));
            }

            Assert.Equal("Step.Owner", await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, "SELECT [Name] FROM [core].[Users] WHERE [Email] = N'step.owner@example.com';"));
        }

        // --- Fixtures ---

        /// <summary>A query over one row's key.</summary>
        /// <param name="select">The statement up to its filter.</param>
        /// <param name="id">The key.</param>
        /// <param name="column">The key's column.</param>
        /// <param name="and">More of the filter; empty for none.</param>
        /// <returns>The query.</returns>
        private static string Where(string select, int id, string column, string and = "")
        {
            return string.Create(CultureInfo.InvariantCulture, $"{select} WHERE [{column}] = {id} {and};");
        }

        /// <summary>Reads a count from the first tenant.</summary>
        /// <param name="query">The query.</param>
        /// <returns>The count.</returns>
        private Task<int> CountAsync(string query)
        {
            return fixture.ScalarAsync<int>(ConnectDatabase.TenantA, query);
        }

        /// <summary>Bootstraps in the platform's own scope of the first tenant.</summary>
        /// <param name="request">The request.</param>
        /// <param name="services">The instance; the fixture's own when null.</param>
        /// <returns>The administrator's id.</returns>
        private async Task<int> BootstrapAsync(TenantBootstrapRequest request, IServiceProvider? services = null)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync("system", kind: PrincipalKind.System, services: services);
            return await scope.ServiceProvider.GetRequiredService<ITenantBootstrapper>().BootstrapAdministratorAsync(request, Token);
        }
    }
}
