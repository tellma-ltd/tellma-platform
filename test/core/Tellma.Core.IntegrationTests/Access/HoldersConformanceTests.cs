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
    ///     "Who holds this" against "can they", end to end over a real tenant database: for each
    ///     securable, a seeded user is listed as a holder exactly when the user's own decision allows it
    ///     through a role that is not public, with that decision's outcome and permissions. The seeds
    ///     reach the securables through every kind of matching stored row: another action's implied
    ///     read, the wildcard resource with an action the securable's resource registers, a former
    ///     name, and the owner of a child securable. A public role makes everyone hold its securable
    ///     and counts every active user. The class has its own databases, so it alone seeds their users.
    /// </summary>
    /// <param name="fixture">The tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class HoldersConformanceTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>
    {
        /// <summary>The roles' former name, which the test's own registration aliases to the roles.</summary>
        private const string FormerRoles = "core.Group";

        /// <summary>The user stamps' resource, a child securable owned by the users.</summary>
        private const string Stamps = UserStampConfiguration.QueryexName;

        /// <summary>
        ///     The fixture's lookups, whose delete the public role grants: no other seeded row reaches it,
        ///     and the read it implies is no securable the test decides, so no user's own grant meets it.
        /// </summary>
        private const string Lookups = "fixture.Lookup";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Each_user_is_a_holder_exactly_when_their_own_roles_allow_the_securable_through_any_matching_row()
        {
            // --- An instance whose registry also knows the roles' former name ---
            await using ServiceProvider instance = fixture.CreateInstance(static services => services.AddSingleton<ISecurableContributor>(new FormerNames()));
            ISecurableRegistry registry = instance.GetRequiredService<ISecurableRegistry>();
            Assert.Equal(CoreResources.Role, registry.FindResource(FormerRoles));
            Assert.NotNull(registry.Find(CoreResources.Role, AccessActions.Save));
            Assert.Equal(CoreResources.User, registry.Find(Stamps, AccessActions.Read)?.Owner?.Resource);
            Assert.NotNull(registry.Find(Lookups, AccessActions.Delete));

            // --- One role per kind of matching row, a public role and a deactivated one ---
            int readers = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null));
            int selfReaders = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, "Id = me()"));
            int savers = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Save, "Id > 0"));
            int anySavers = await fixture.SeedRoleAsync(("*", AccessActions.Save, null));
            int anyInviters = await fixture.SeedRoleAsync(("*", AccessActions.Invite, null));
            int formerReaders = await fixture.SeedRoleAsync((FormerRoles, AccessActions.Read, null));
            int everyone = await fixture.SeedRoleAsync((Lookups, AccessActions.Delete, null));
            int retired = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null));
            await fixture.ExecuteAsync(
                ConnectDatabase.TenantA,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Roles] SET [IsPublic] = 1 WHERE [Id] = {everyone}; UPDATE [core].[Roles] SET [IsActive] = 0 WHERE [Id] = {retired};"));

            // --- One member of each, a deactivated saver and a user of no role ---
            (int reader, string subject) = await fixture.SeedCallerAsync("Conformance reader");
            (int selfReader, _) = await fixture.SeedCallerAsync("Conformance self-reader");
            (int saver, _) = await fixture.SeedCallerAsync("Conformance saver");
            (int anySaver, _) = await fixture.SeedCallerAsync("Conformance saver of anything");
            (int anyInviter, _) = await fixture.SeedCallerAsync("Conformance inviter to anything");
            (int formerReader, _) = await fixture.SeedCallerAsync("Conformance reader by the former name");
            (int retiree, _) = await fixture.SeedCallerAsync("Conformance member of a deactivated role");
            (int deactivated, _) = await fixture.SeedCallerAsync("Conformance deactivated saver");
            (int nobody, _) = await fixture.SeedCallerAsync("Conformance member of no role");
            foreach ((int user, int role) in new[]
            {
                (reader, readers), (selfReader, selfReaders), (saver, savers), (anySaver, anySavers), (anyInviter, anyInviters),
                (formerReader, formerReaders), (retiree, retired), (deactivated, savers),
            })
            {
                await fixture.SeedMembershipAsync(user, role);
            }

            await fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {deactivated};"));
            int[] users = [reader, selfReader, saver, anySaver, anyInviter, formerReader, retiree, deactivated, nobody];

            // The tenant holds these users and the system user alone, so the public role's audience is known.
            int others = await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT COUNT(*) FROM [core].[Users] WHERE [Id] <> {WellKnownIds.SystemUserId};"));
            Assert.Equal(users.Length, others);

            // --- The reader reads every user, so every holder is listed ---
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services: instance);
            IAccessHoldersResolver resolver = scope.ServiceProvider.GetRequiredService<IAccessHoldersResolver>();
            IAccessEvaluator evaluator = scope.ServiceProvider.GetRequiredService<IAccessEvaluator>();
            Dictionary<(string Resource, string Action), AccessHolders> resolved = [];
            foreach ((string resource, string action) in new[]
            {
                (CoreResources.Role, AccessActions.Read), (CoreResources.Role, AccessActions.Save), (CoreResources.User, AccessActions.Read),
                (Stamps, AccessActions.Read), (Lookups, AccessActions.Delete),
            })
            {
                AccessHolders found = await resolver.ResolveAsync(resource, action, Token);
                resolved[(resource, action)] = found;
                var holders = found.Holders.ToDictionary(static holder => holder.UserId);
                Assert.Equal((holders.Count, false), (found.Total, found.Capped));

                // --- Each user: a holder exactly when their decision allows through a role that is not public ---
                foreach (int user in users)
                {
                    AccessDecision decision = await evaluator.EvaluateForAsync(user, resource, action, Token);
                    int[] own = [.. decision.Grants.OfType<RoleAccessGrant>().Where(static grant => !grant.IsPublic).Select(static grant => grant.PermissionId).Distinct().Order()];
                    if (!decision.IsAllowed || own.Length == 0)
                    {
                        Assert.False(holders.ContainsKey(user), $"User {user} is listed as a holder of {resource}:{action}, which no role of theirs that is not public allows.");
                        continue;
                    }

                    Assert.True(holders.TryGetValue(user, out AccessHolder? holder), $"User {user} is not listed as a holder of {resource}:{action}, which their own roles allow.");
                    Assert.Equal(decision.Outcome, holder.Outcome);
                    Assert.Equal(own, holder.PermissionIds);
                }

                // --- Everyone is what a user of no role is decided ---
                Assert.Equal((await evaluator.EvaluateForAsync(nobody, resource, action, Token)).Outcome, found.Everyone);
            }

            // --- The roles' read: implied by a save, by the wildcard resource's save, and stored under the former name ---
            AccessHolders roleReaders = resolved[(CoreResources.Role, AccessActions.Read)];
            Assert.Equal(
                [(saver, AccessOutcome.Filtered), (anySaver, AccessOutcome.Unrestricted), (formerReader, AccessOutcome.Unrestricted)],
                roleReaders.Holders.Select(static holder => (holder.UserId, holder.Outcome)).OrderBy(static pair => pair.UserId));
            Assert.Equal(1, roleReaders.MembersByRole[savers]);
            Assert.DoesNotContain(roleReaders.Grants, grant => grant.RoleId == retired);

            // --- The stamps' read: through the users' read, their owner, filtered or not ---
            AccessHolders stampReaders = resolved[(Stamps, AccessActions.Read)];
            Assert.Contains(stampReaders.Holders, holder => (holder.UserId, holder.Outcome) == (reader, AccessOutcome.Unrestricted));
            Assert.Contains(stampReaders.Holders, holder => (holder.UserId, holder.Outcome) == (selfReader, AccessOutcome.Filtered));

            // --- The public role: everyone holds the lookups' delete, no holder is listed, and it counts every active user ---
            AccessHolders deleters = resolved[(Lookups, AccessActions.Delete)];
            int audience = 0;
            foreach (int user in users)
            {
                audience += (await evaluator.EvaluateForAsync(user, Lookups, AccessActions.Delete, Token)).IsAllowed ? 1 : 0;
            }

            Assert.Equal(users.Length - 1, audience);
            Assert.Equal(AccessOutcome.Unrestricted, deleters.Everyone);
            Assert.Empty(deleters.Holders);
            Assert.Equal(audience, deleters.MembersByRole[everyone]);
        }

        /// <summary>The test's own registration: the roles' former name.</summary>
        private sealed class FormerNames : ISecurableContributor
        {
            /// <inheritdoc />
            public void Contribute(SecurableRegistryBuilder builder)
            {
                ArgumentNullException.ThrowIfNull(builder);
                _ = builder.Alias(FormerRoles, CoreResources.Role);
            }
        }
    }
}
