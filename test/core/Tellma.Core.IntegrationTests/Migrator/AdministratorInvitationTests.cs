// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Migrator;
using Tellma.Testing.Support.Identity;

namespace Tellma.Core.IntegrationTests.Migrator
{
    /// <summary>
    ///     The first administrator's invitation after a tenant's provisioning steps, run by the
    ///     migrator through the users' invite action over the test distribution, with the identity
    ///     server answering from a script: the administrator invited once and linked, with the
    ///     membership hint and a pending welcome email; a refusal failing the provisioning after its
    ///     steps with a resume that names the administrator; a sandbox's administrator without a
    ///     usable account refused with the sandbox's code; and nobody invited who joined already or
    ///     whose subject the run names.
    /// </summary>
    /// <param name="sql">The shared SQL Server.</param>
    [Trait("Category", "Integration")]
    public sealed class AdministratorInvitationTests(SqlServerFixture sql)
    {
        /// <summary>The administrator the tests name, as an operator might type the address.</summary>
        private const string Typed = "Owner@Acme.Example";

        /// <summary>The administrator's address as the users' stack stores it.</summary>
        private const string Stored = "owner@acme.example";

        /// <summary>The administrator's row: its state, invitation status and subject, joined by bars.</summary>
        private const string AdministratorRow =
            $"SELECT CONCAT([State], '|', [InviteStatus], '|', [Subject] COLLATE DATABASE_DEFAULT) FROM [core].[Users] WHERE [Email] = N'{Stored}';";

        [Fact]
        public async Task A_provisioned_tenant_invites_its_administrator_once_links_the_account_and_queues_the_welcome()
        {
            (MigratorHarness migrator, ScriptedIdentityClient identity) = await DeploymentAsync("inv_new");

            MigratorResult provisioned = await migrator.RunConvergedAsync("provision", "--name", "Gadgets Inc", "--category", "Live", "--admin-email", Typed);

            // --- One invitation, for the stored address, that may create the account ---
            Assert.Equal("provision: registered tenant 1000", provisioned.Lines[0]);
            IdentityInvitation invitation = Assert.Single(Assert.Single(identity.Invites));
            Assert.Equal((Stored, false), (invitation.Email, invitation.ExistingOnly));

            // --- The administrator linked and invited, the hint recorded, the welcome pending, the tenant active ---
            string tenant = migrator.TenantConnectionString(1000);
            string subject = ScriptedIdentityClient.SubjectOf(Stored);
            Assert.Equal($"Invited|Invited|{subject}", await SqlServerFixture.ScalarAsync<string>(tenant, AdministratorRow));
            Assert.True(await SqlServerFixture.ScalarAsync<bool>(
                migrator.CatalogConnectionString, $"SELECT [IsActive] FROM [catalog].[TenantMemberships] WHERE [TenantId] = 1000 AND [Subject] = '{subject}';"));
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(tenant, $"""
                SELECT COUNT(*) FROM [core].[OutgoingEmails] AS E JOIN [core].[Users] AS U ON U.[Id] = E.[RecipientUserId]
                 WHERE U.[Email] = N'{Stored}' AND E.[RegardingEntity] = N'{CoreResources.User}' AND E.[State] = 'Pending';
                """));
            Assert.Equal("Active", await StateAsync(migrator, 1000));
        }

        [Fact]
        public async Task A_refused_invitation_fails_the_provisioning_after_its_steps_and_the_resume_naming_the_administrator_invites_again()
        {
            (MigratorHarness migrator, ScriptedIdentityClient identity) = await DeploymentAsync("inv_refused");
            identity.OnInvite = static (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
                [.. invitations.Select(static invitation => new IdentityInvitationResult(invitation.Email, null, null, "account_disabled"))]);

            // --- The refusal fails the tenant with the identity server's text; the steps are recorded, the tenant not active ---
            MigratorResult failed = await migrator.RunAsync("provision", "--name", "Gadgets Inc", "--category", "Live", "--admin-email", Typed);
            Assert.True(failed.ExitCode == MigratorExitCodes.PartialFailure, failed.ToString());
            Assert.Contains(failed.Lines, static line => line.StartsWith("tenant 1000: failed", StringComparison.Ordinal) && line.Contains("account_disabled", StringComparison.Ordinal));
            Assert.Contains($"provision: resume it with provision --tenant 1000 --admin-email {Typed}; rerunning this line registers another tenant", failed.Lines);
            string tenant = migrator.TenantConnectionString(1000);
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(tenant, "SELECT COUNT(*) FROM [dbo].[__TellmaProvisioning] WHERE [Step] = N'core.bootstrap-administrator';"));
            Assert.Equal("New||", await SqlServerFixture.ScalarAsync<string>(tenant, AdministratorRow));
            Assert.Equal("Provisioning", await StateAsync(migrator, 1000));

            // --- The resume the report names invites again, and the tenant turns active ---
            identity.OnInvite = null;
            _ = await migrator.RunConvergedAsync("provision", "--tenant", "1000", "--admin-email", Typed);
            Assert.Equal(2, identity.Invites.Count);
            Assert.Equal($"Invited|Invited|{ScriptedIdentityClient.SubjectOf(Stored)}", await SqlServerFixture.ScalarAsync<string>(tenant, AdministratorRow));
            Assert.Equal("Active", await StateAsync(migrator, 1000));
        }

        [Fact]
        public async Task A_sandbox_administrator_without_a_usable_account_fails_with_the_sandbox_s_code_and_creates_nobody()
        {
            (MigratorHarness migrator, ScriptedIdentityClient identity) = await DeploymentAsync("inv_sandbox");
            _ = await migrator.RunConvergedAsync("provision", "--name", "Gadgets Inc", "--category", "Live");

            // The identity server knows no such address, and a lookup may not create one.
            identity.OnInvite = static (invitations, _) => Task.FromResult<IReadOnlyList<IdentityInvitationResult>>(
                [.. invitations.Select(static invitation => new IdentityInvitationResult(invitation.Email, null, null, invitation.ExistingOnly ? "unknown_email" : "unexpected"))]);

            MigratorResult failed = await migrator.RunAsync("provision", "--name", "Gadgets Sandbox", "--category", "Sandbox", "--live-tenant", "1000", "--admin-email", Typed);

            Assert.True(failed.ExitCode == MigratorExitCodes.PartialFailure, failed.ToString());
            Assert.True(Assert.Single(Assert.Single(identity.Invites)).ExistingOnly);
            Assert.Contains(failed.Lines, static line => line.StartsWith("tenant 1001: failed", StringComparison.Ordinal) && line.Contains(AccessCodes.SandboxRequiresExistingIdentity, StringComparison.Ordinal));
            Assert.Equal("New||", await SqlServerFixture.ScalarAsync<string>(migrator.TenantConnectionString(1001), AdministratorRow));
            Assert.Equal("Provisioning", await StateAsync(migrator, 1001));
        }

        [Fact]
        public async Task Nobody_is_invited_who_joined_already_or_whose_subject_the_run_names()
        {
            (MigratorHarness migrator, ScriptedIdentityClient identity) = await DeploymentAsync("inv_skip");
            _ = await migrator.RunConvergedAsync("provision", "--name", "Gadgets Inc", "--category", "Live", "--admin-email", Typed);
            Assert.Single(identity.Invites);

            // --- The administrator signs in, which joins them; naming them again invites nobody ---
            string tenant = migrator.TenantConnectionString(1000);
            await SqlServerFixture.ExecuteAsync(tenant, $"UPDATE [core].[Users] SET [JoinedAt] = SYSUTCDATETIME() WHERE [Email] = N'{Stored}';");
            Assert.Contains("tenant 1000: provisioned (already; 1 administrator step(s))", (await migrator.RunConvergedAsync("provision", "--tenant", "1000", "--admin-email", Typed)).Lines);
            Assert.Single(identity.Invites);

            // --- A Development administrator named with a subject is bound, not invited ---
            _ = await migrator.RunConvergedAsync("provision", "--name", "Gadgets Labs", "--category", "Live", "--admin-email", "lab@acme.example", "--admin-subject", "sub-lab");
            Assert.Single(identity.Invites);
            Assert.Equal("Invited|Invited|sub-lab", await SqlServerFixture.ScalarAsync<string>(
                migrator.TenantConnectionString(1001), "SELECT CONCAT([State], '|', [InviteStatus], '|', [Subject] COLLATE DATABASE_DEFAULT) FROM [core].[Users] WHERE [Email] = N'lab@acme.example';"));
        }

        /// <summary>A migrated deployment whose identity server answers from a script.</summary>
        /// <param name="prefix">The prefix identifying the test.</param>
        /// <returns>The deployment and its scripted identity server.</returns>
        private async Task<(MigratorHarness Migrator, ScriptedIdentityClient Identity)> DeploymentAsync(string prefix)
        {
            MigratorHarness migrator = new(sql, prefix);
            ScriptedIdentityClient identity = new();
            migrator.Services = services => services.Replace(ServiceDescriptor.Singleton<IIdentityServerClient>(identity));
            _ = await migrator.RunConvergedAsync("migrate");
            return (migrator, identity);
        }

        /// <summary>A tenant's state, as the catalog holds it.</summary>
        /// <param name="migrator">The harness.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The state's name, or null for no row.</returns>
        private static Task<string?> StateAsync(MigratorHarness migrator, int tenantId)
        {
            return SqlServerFixture.ScalarAsync<string>(
                migrator.CatalogConnectionString, string.Create(CultureInfo.InvariantCulture, $"SELECT [State] FROM [catalog].[Tenants] WHERE [Id] = {tenantId};"));
        }
    }
}
