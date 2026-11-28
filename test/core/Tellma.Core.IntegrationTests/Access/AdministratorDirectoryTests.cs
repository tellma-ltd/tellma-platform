// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Access;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     The administrator directory against a real tenant database: the active individuals,
    ///     invited or joined, with a current membership of the administrator role, by id and at most
    ///     twenty; never an ended or a future membership, an inactive user, a service account or the
    ///     system user. The class has its own databases, so it alone grants the administrator role.
    /// </summary>
    /// <param name="fixture">The tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class AdministratorDirectoryTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task The_directory_lists_the_current_administrators_by_id_and_skips_an_ended_and_a_future_membership()
        {
            (_, string subject) = await fixture.SeedCallerAsync("Asking member");
            int administrators = WellKnownIds.AdministratorRoleId;

            // --- Listed: a joined, an invited and an ending administrator ---
            (int joined, _) = await fixture.SeedCallerAsync("Joined administrator");
            (int invited, _) = await fixture.SeedCallerAsync("Invited administrator", joined: false);
            (int ending, _) = await fixture.SeedCallerAsync("Ending administrator");
            await fixture.SeedMembershipAsync(joined, administrators);
            await fixture.SeedMembershipAsync(invited, administrators);
            await fixture.SeedMembershipAsync(ending, administrators, "DATEADD(day, -1, SYSDATETIMEOFFSET())", "DATEADD(day, 1, SYSDATETIMEOFFSET())");

            // --- Skipped: an ended and a future membership, an inactive user and a service account ---
            (int ended, _) = await fixture.SeedCallerAsync("Former administrator");
            (int future, _) = await fixture.SeedCallerAsync("Future administrator");
            (int inactive, _) = await fixture.SeedCallerAsync("Inactive administrator");
            int service = fixture.NextUserId();
            await fixture.SeedUserAsync(ConnectDatabase.TenantA, service, string.Create(CultureInfo.InvariantCulture, $"client-{service}"), "Service administrator", UserKind.Service);
            await fixture.SeedMembershipAsync(ended, administrators, "DATEADD(day, -10, SYSDATETIMEOFFSET())", "DATEADD(second, -1, SYSDATETIMEOFFSET())");
            await fixture.SeedMembershipAsync(future, administrators, "DATEADD(day, 1, SYSDATETIMEOFFSET())");
            await fixture.SeedMembershipAsync(inactive, administrators);
            await fixture.SeedMembershipAsync(service, administrators);
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {inactive};"));

            Assert.Equal([joined, invited, ending], await AdministratorsAsync(subject));

            // --- Twenty at most, the lowest ids first ---
            List<int> more = [];
            for (int index = 0; index < 20; index++)
            {
                (int administrator, _) = await fixture.SeedCallerAsync(string.Create(CultureInfo.InvariantCulture, $"Administrator {index}"));
                await fixture.SeedMembershipAsync(administrator, administrators);
                more.Add(administrator);
            }

            Assert.Equal(20, AdministratorDirectory.MaxAdministrators);
            Assert.Equal([joined, invited, ending, .. more[..17]], await AdministratorsAsync(subject));
        }

        /// <summary>The administrators as a member of the tenant reads them, in one scope.</summary>
        /// <param name="subject">The member's subject.</param>
        /// <returns>The administrators' ids.</returns>
        private async Task<IReadOnlyList<int>> AdministratorsAsync(string subject)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            return await scope.ServiceProvider.GetRequiredService<IAdministratorDirectory>().GetAdministratorIdsAsync(Token);
        }
    }
}
