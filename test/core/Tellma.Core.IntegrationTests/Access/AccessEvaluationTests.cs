// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Caching;
using Tellma.Core.Localization;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     The evaluator and the holders resolver against a real tenant database: another user's set
    ///     loaded once per scope and never reported to the response, a deactivated, unknown or
    ///     system target denied, a stale denial re-checked in one prologue round trip that validates
    ///     the set or brings a permission granted since, under the premises of a scope connected by
    ///     subject or by user id and without reloading an aged set, an invalidation that makes the
    ///     user's next connection load afresh, the member witness of a connected caller, and the
    ///     holders of a securable read in one round trip, skipping an ended and a future membership,
    ///     listing only the users the caller may read, by their names in the reader's content
    ///     language, up to the cap.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class AccessEvaluationTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>, IDisposable
    {
        /// <summary>The prologue round trips of the fixture's instance, one measurement each, tagged by variant.</summary>
        private readonly MetricCollector<double> _prologues = Prologues(fixture.Services);

        /// <summary>The connect results of the fixture's instance, tagged by result.</summary>
        private readonly MetricCollector<long> _results = new(
            fixture.Services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.ConnectResults);

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public void Dispose()
        {
            _prologues.Dispose();
            _results.Dispose();
        }

        // --- EvaluateForAsync: another user's set ---

        [Fact]
        public async Task Another_user_is_loaded_once_per_scope_and_never_reported_to_the_response()
        {
            (int caller, string subject) = await fixture.SeedCallerAsync("Evaluating caller");
            (int target, _) = await fixture.SeedCallerAsync("Evaluated target");
            int role = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null));
            await fixture.SeedMembershipAsync(target, role);
            IUserVersionTagSnapshots stamps = fixture.Services.GetRequiredService<IUserVersionTagSnapshots>();

            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                UserVersionTagSnapshot? callerStamps = stamps.Current(ConnectDatabase.TenantA, caller);
                int before = _prologues.GetMeasurementSnapshot().Count;
                IAccessEvaluator evaluator = scope.ServiceProvider.GetRequiredService<IAccessEvaluator>();

                // --- Concurrent decisions on the target share one load; the caller's own id loads nothing ---
                AccessDecision[] reads = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => evaluator.EvaluateForAsync(target, CoreResources.Role, AccessActions.Read, Token)));
                AccessDecision save = await evaluator.EvaluateForAsync(target, CoreResources.Role, AccessActions.Save, Token);
                AccessDecision own = await evaluator.EvaluateForAsync(caller, CoreResources.Role, AccessActions.Read, Token);

                Assert.Equal([AccessTelemetryNames.VariantUser], Variants(_prologues).Skip(before));
                Assert.All(reads, decision =>
                {
                    Assert.Equal(AccessOutcome.Unrestricted, decision.Outcome);
                    Assert.Equal(role, Assert.Single(decision.Grants.OfType<RoleAccessGrant>()).RoleId);
                });
                Assert.Equal(AccessOutcome.Denied, save.Outcome);
                Assert.Equal(AccessOutcome.Denied, own.Outcome);

                // --- Nothing of the target's reaches the response's tags, the caches or the witness ---
                Assert.Null(stamps.Current(ConnectDatabase.TenantA, target));
                Assert.Equal(callerStamps, stamps.Current(ConnectDatabase.TenantA, caller));
                Assert.Null(fixture.Services.GetRequiredService<PermissionsCache>().Peek(new TenantUserCacheKey(ConnectDatabase.TenantA, target), static _ => true));
                Assert.Empty(scope.ServiceProvider.GetRequiredService<AccessWitness>().Evaluated);
            }

            // --- The next scope loads the target again ---
            await using AsyncServiceScope next = await fixture.ScopeAsync(subject);
            int warm = _prologues.GetMeasurementSnapshot().Count;
            AccessDecision again = await next.ServiceProvider.GetRequiredService<IAccessEvaluator>().EvaluateForAsync(target, CoreResources.Role, AccessActions.Read, Token);

            Assert.Equal(AccessOutcome.Unrestricted, again.Outcome);
            Assert.Equal([AccessTelemetryNames.VariantUser], Variants(_prologues).Skip(warm));
        }

        [Fact]
        public async Task A_deactivated_unknown_or_system_target_is_denied_with_no_set()
        {
            (_, string subject) = await fixture.SeedCallerAsync("Asking caller");
            (int target, _) = await fixture.SeedCallerAsync("Deactivated target");
            int role = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null));
            await fixture.SeedMembershipAsync(target, role);
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {target};"));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            int before = _results.GetMeasurementSnapshot().Count;
            IAccessEvaluator evaluator = scope.ServiceProvider.GetRequiredService<IAccessEvaluator>();

            AccessDecision[] decisions =
            [
                await evaluator.EvaluateForAsync(target, CoreResources.Role, AccessActions.Read, Token),
                await evaluator.EvaluateForAsync(987_654, CoreResources.Role, AccessActions.Read, Token),
                await evaluator.EvaluateForAsync(WellKnownIds.SystemUserId, CoreResources.Role, AccessActions.Read, Token),
            ];

            Assert.All(decisions, static decision =>
            {
                Assert.Equal(AccessOutcome.Denied, decision.Outcome);
                Assert.Empty(decision.Grants);
            });
            Assert.Equal(
                [AccessTelemetryNames.ResultDeactivated, AccessTelemetryNames.ResultUnknownSubject, AccessTelemetryNames.ResultUnknownSubject],
                _results.GetMeasurementSnapshot().Skip(before).Select(static measurement => (string)measurement.Tags[AccessTelemetryNames.ResultTag]!));
        }

        // --- The deny re-check ---

        [Fact]
        public async Task A_stale_denial_is_re_checked_once_per_scope_in_one_round_trip_whose_pass_advances_the_validation()
        {
            await using ServiceProvider instance = StaleInstance();
            using MetricCollector<double> prologues = Prologues(instance);
            (int caller, string subject) = await fixture.SeedCallerAsync("Re-checked caller");

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, services: instance);
            ConnectedUser connected = await scope.ServiceProvider.GetRequiredService<IScopeConnection>().CurrentAsync(Token);
            DateTimeOffset validated = connected.Access.ValidatedAt;
            int before = prologues.GetMeasurementSnapshot().Count;
            IAccessEvaluator evaluator = scope.ServiceProvider.GetRequiredService<IAccessEvaluator>();

            AccessDecision first = await evaluator.EvaluateAsync(CoreResources.Role, AccessActions.Read, Token);

            // --- One prologue-only round trip under the warm premises, which passed: the same set, validated now ---
            Assert.Equal(AccessOutcome.Denied, first.Outcome);
            Assert.Equal([AccessTelemetryNames.VariantSubject], Variants(prologues).Skip(before));
            UserAccess cached = instance.GetRequiredService<PermissionsCache>().Peek(new TenantUserCacheKey(ConnectDatabase.TenantA, caller), static _ => true)!;
            Assert.Same(connected.Access, cached);
            Assert.True(cached.ValidatedAt > validated);

            // --- No second re-check in the scope, and none from the summary ---
            AccessDecision second = await evaluator.EvaluateAsync(CoreResources.Role, AccessActions.Save, Token);
            IReadOnlyList<AccessDecision> summary = await evaluator.EvaluateAllAsync(Token);

            Assert.Equal(AccessOutcome.Denied, second.Outcome);
            Assert.All(summary, static decision => Assert.Equal(AccessOutcome.Denied, decision.Outcome));
            _ = Assert.Single(Variants(prologues).Skip(before));
        }

        [Fact]
        public async Task A_re_check_that_finds_a_moved_tag_admits_a_permission_granted_since_the_set_was_cached()
        {
            await using ServiceProvider instance = StaleInstance();
            using MetricCollector<double> prologues = Prologues(instance);
            (int caller, string subject) = await fixture.SeedCallerAsync("Newly granted caller");

            // --- A cold connect caches the caller's set, which holds nothing yet ---
            AsyncServiceScope cold = await fixture.ScopeAsync(subject, services: instance);
            Assert.Empty((await cold.ServiceProvider.GetRequiredService<IAccessEvaluator>().GetAccessAsync(Token)).Grants);
            await cold.DisposeAsync();

            // --- A save grants the caller the role's read and moves the tags ---
            int role = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null));
            await fixture.SeedMembershipAsync(caller, role);
            await fixture.BumpPermissionsAsync(caller);

            // --- The warm scope's cached set denies; the re-check brings the fresh rows ---
            await using AsyncServiceScope warm = await fixture.ScopeAsync(subject, services: instance);
            int before = prologues.GetMeasurementSnapshot().Count;
            IAccessEvaluator evaluator = warm.ServiceProvider.GetRequiredService<IAccessEvaluator>();

            AccessDecision decision = await evaluator.EvaluateAsync(CoreResources.Role, AccessActions.Read, Token);
            AccessDecision again = await evaluator.EvaluateAsync(CoreResources.Role, AccessActions.Read, Token);

            Assert.Equal(AccessOutcome.Unrestricted, decision.Outcome);
            Assert.Equal(role, Assert.Single(decision.Grants.OfType<RoleAccessGrant>()).RoleId);
            Assert.Equal(AccessOutcome.Unrestricted, again.Outcome);
            Assert.Equal([AccessTelemetryNames.VariantSubject], Variants(prologues).Skip(before));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task A_re_check_confirms_the_set_under_the_scopes_own_premises_and_never_reloads_an_aged_one(bool byUserId, bool aged)
        {
            await using ServiceProvider instance = aged ? AgedInstance() : StaleInstance();
            using MetricCollector<double> prologues = Prologues(instance);
            (int caller, string subject) = await fixture.SeedCallerAsync(byUserId ? "Re-checked run-as user" : "Re-checked caller of an aged set");

            // --- A scope connected by the user's id, as a job's is, or by subject ---
            await using AsyncServiceScope scope = byUserId
                ? await fixture.ScopeAsync(null, userId: caller, services: instance)
                : await fixture.ScopeAsync(subject, services: instance);
            ConnectedUser connected = await scope.ServiceProvider.GetRequiredService<IScopeConnection>().CurrentAsync(Token);
            DateTimeOffset validated = connected.Access.ValidatedAt;
            int before = prologues.GetMeasurementSnapshot().Count;

            AccessDecision decision = await scope.ServiceProvider.GetRequiredService<IAccessEvaluator>().EvaluateAsync(CoreResources.Role, AccessActions.Read, Token);

            // --- One prologue of the scope's own variant confirmed the set: the same one, cached and validated now ---
            Assert.Equal(AccessOutcome.Denied, decision.Outcome);
            Assert.Equal([byUserId ? AccessTelemetryNames.VariantUser : AccessTelemetryNames.VariantSubject], Variants(prologues).Skip(before));
            UserAccess cached = instance.GetRequiredService<PermissionsCache>().Peek(new TenantUserCacheKey(ConnectDatabase.TenantA, caller), static _ => true)!;
            Assert.Same(connected.Access, cached);
            Assert.True(cached.ValidatedAt > validated);
        }

        // --- Invalidate ---

        [Fact]
        public async Task Invalidating_a_user_makes_their_next_connection_load_afresh_and_no_scope_connected_before_caches_the_old_one_again()
        {
            await using ServiceProvider instance = StaleInstance();
            using MetricCollector<double> prologues = Prologues(instance);
            (int caller, string subject) = await fixture.SeedCallerAsync("Invalidated caller");
            TenantUserCacheKey key = new(ConnectDatabase.TenantA, caller);
            ConnectCache connections = instance.GetRequiredService<ConnectCache>();
            ProfileCache profiles = instance.GetRequiredService<ProfileCache>();

            // --- A scope connected by subject: the instance caches the set, the profile and the connection ---
            await using AsyncServiceScope connected = await fixture.ScopeAsync(subject, services: instance);
            ConnectedUser old = await connected.ServiceProvider.GetRequiredService<IScopeConnection>().CurrentAsync(Token);
            VersionTag preferences = new(old.PreferencesTag);
            Assert.Same(old.Access, connections.Find(ConnectDatabase.TenantA, subject)?.Access);
            Assert.Same(old.Profile, profiles.Peek(key, preferences));

            // --- The invalidation drops all three, though no tag moved ---
            connected.ServiceProvider.GetRequiredService<IAccessEvaluator>().Invalidate(ConnectDatabase.TenantA, caller);

            Assert.Null(connections.Find(ConnectDatabase.TenantA, subject));
            Assert.Null(instance.GetRequiredService<PermissionsCache>().Peek(key, static _ => true));
            Assert.Null(profiles.Peek(key, preferences));

            // --- A round trip of the scope connected before it confirms that scope's set, and caches no connection ---
            _ = await connected.ServiceProvider.GetRequiredService<IAccessHoldersResolver>().ResolveAsync(CoreResources.Role, AccessActions.Read, Token);
            Assert.Null(connections.Find(ConnectDatabase.TenantA, subject));

            // --- The next scope connects afresh: a new set and profile, and its connection cached ---
            ConnectedUser fresh;
            await using (AsyncServiceScope next = await fixture.ScopeAsync(subject, services: instance))
            {
                fresh = await next.ServiceProvider.GetRequiredService<IScopeConnection>().CurrentAsync(Token);
            }

            Assert.NotSame(old.Access, fresh.Access);
            Assert.NotSame(old.Profile, fresh.Profile);
            Assert.Same(fresh.Access, connections.Find(ConnectDatabase.TenantA, subject)?.Access);

            // --- The older scope's re-check confirms its own set again, and leaves the fresh connection cached ---
            int before = prologues.GetMeasurementSnapshot().Count;
            AccessDecision decision = await connected.ServiceProvider.GetRequiredService<IAccessEvaluator>().EvaluateAsync(CoreResources.Role, AccessActions.Read, Token);

            Assert.Equal(AccessOutcome.Denied, decision.Outcome);
            Assert.Equal([AccessTelemetryNames.VariantSubject], Variants(prologues).Skip(before));
            Assert.Same(fresh.Access, connections.Find(ConnectDatabase.TenantA, subject)?.Access);
        }

        // --- The witness ---

        [Fact]
        public async Task A_connected_caller_is_witnessed_as_a_member_and_each_evaluation_as_its_pair()
        {
            (_, string subject) = await fixture.SeedCallerAsync("Witnessed caller");

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            AccessWitness witness = scope.ServiceProvider.GetRequiredService<AccessWitness>();
            Assert.True(witness.IsMemberWitnessed);
            Assert.Empty(witness.Evaluated);

            _ = await scope.ServiceProvider.GetRequiredService<IAccessEvaluator>().EvaluateAsync("CORE.ROLE", "read", Token);

            Assert.Equal([new SecurableRef(CoreResources.Role, AccessActions.Read)], witness.Evaluated);
            Assert.True(witness.Confirm(new SecurableEndpointMetadata(CoreResources.Role, AccessActions.Read), "GET roles"));
        }

        // --- The holders ---

        [Fact]
        public async Task The_holders_skip_an_ended_and_a_future_membership_and_list_only_the_users_the_caller_may_read()
        {
            // --- The holders' role and its members: current, ended, future, ending later, inactive ---
            int holders = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Delete, null));
            (int current, _) = await fixture.SeedCallerAsync("Hld Amal");
            (int ended, _) = await fixture.SeedCallerAsync("Hld Dalia");
            (int future, _) = await fixture.SeedCallerAsync("Hld Eman");
            (int ending, _) = await fixture.SeedCallerAsync("Hld Basma");
            (int inactive, _) = await fixture.SeedCallerAsync("Hld Fatma");
            await fixture.SeedMembershipAsync(current, holders);
            await fixture.SeedMembershipAsync(ended, holders, "DATEADD(day, -10, SYSDATETIMEOFFSET())", "DATEADD(day, -1, SYSDATETIMEOFFSET())");
            await fixture.SeedMembershipAsync(future, holders, "DATEADD(day, 1, SYSDATETIMEOFFSET())");
            await fixture.SeedMembershipAsync(ending, holders, null, "DATEADD(day, 30, SYSDATETIMEOFFSET())");
            await fixture.SeedMembershipAsync(inactive, holders);
            await fixture.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {inactive};"));

            // --- Three readers: every user, only themselves (and a holder too), and no user ---
            (int reader, string readerSubject) = await fixture.SeedCallerAsync("Hld Reader");
            (int self, string selfSubject) = await fixture.SeedCallerAsync("Hld Carmen");
            (int blindReader, string blindSubject) = await fixture.SeedCallerAsync("Hld Blind");
            int readers = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, null), (CoreResources.Role, AccessActions.Read, null));
            int selfReaders = await fixture.SeedRoleAsync((CoreResources.User, AccessActions.Read, "Id = me()"), (CoreResources.Role, AccessActions.Read, null));
            await fixture.SeedMembershipAsync(reader, readers);
            await fixture.SeedMembershipAsync(self, selfReaders);
            await fixture.SeedMembershipAsync(self, holders);
            int blind = await fixture.SeedRoleAsync((CoreResources.Role, AccessActions.Read, null));
            await fixture.SeedMembershipAsync(blindReader, blind);

            int permission = await fixture.ScalarAsync<int>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT [Id] FROM [core].[Permissions] WHERE [RoleId] = {holders};"));
            DateTimeOffset endsAt = await fixture.ScalarAsync<DateTimeOffset>(
                ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT [EndsAt] FROM [core].[RoleMemberships] WHERE [UserId] = {ending} AND [RoleId] = {holders};"));
            string userEntity = fixture.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(User)).Name;
            string roleEntity = fixture.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(Role)).Name;

            // --- A reader of every user: one round trip, the current members by name, the ended and the future skipped ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(readerSubject))
            {
                int before = _prologues.GetMeasurementSnapshot().Count;
                AccessHolders found = await scope.ServiceProvider.GetRequiredService<IAccessHoldersResolver>().ResolveAsync(CoreResources.Role, AccessActions.Delete, Token);

                _ = Assert.Single(Variants(_prologues).Skip(before));
                Assert.Equal((CoreResources.Role, AccessActions.Delete, AccessOutcome.Denied, 3, false), (found.Resource, found.Action, found.Everyone, found.Total, found.Capped));
                Assert.Equal(
                    [(current, null), (ending, endsAt), (self, null)],
                    found.Holders.Select(static holder => (holder.UserId, holder.EndsAt)));
                Assert.All(found.Holders, holder =>
                {
                    Assert.Equal(AccessOutcome.Unrestricted, holder.Outcome);
                    Assert.Equal([permission], holder.PermissionIds);
                });
                Assert.Equal(3, found.MembersByRole[holders]);
                Assert.Contains(found.Grants, grant => grant.RoleId == holders && grant.PermissionId == permission);
                Assert.Empty(found.Problems);
                Assert.Equal([current, ending, self], found.Related.Sets[userEntity].Entities.Cast<User>().Select(static user => user.Id));
                Assert.Contains(holders, found.Related.Sets[roleEntity].Entities.Cast<Role>().Select(static role => role.Id));
            }

            // --- A reader of only themselves sees only themselves, and still counts every holder ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(selfSubject))
            {
                AccessHolders found = await scope.ServiceProvider.GetRequiredService<IAccessHoldersResolver>().ResolveAsync(CoreResources.Role, AccessActions.Delete, Token);

                Assert.Equal([self], found.Holders.Select(static holder => holder.UserId));
                Assert.Equal(3, found.Total);
            }

            // --- A caller who may read no user lists none, and the count stands ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(blindSubject))
            {
                AccessHolders found = await scope.ServiceProvider.GetRequiredService<IAccessHoldersResolver>().ResolveAsync(CoreResources.Role, AccessActions.Delete, Token);

                Assert.Empty(found.Holders);
                Assert.Equal(3, found.Total);
                Assert.False(found.Related.Sets.ContainsKey(userEntity));
            }
        }

        [Fact]
        public async Task The_holders_listed_are_the_first_by_the_readers_names_up_to_the_cap_and_only_they_are_related()
        {
            // --- The second tenant, which no other case seeds: English content first, then Arabic ---
            await fixture.ExecuteAsync(
                ConnectDatabase.TenantB,
                $"UPDATE [core].[Settings] SET [SecondaryLanguage] = 'ar'; UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'{VersionTagNames.Settings}';");

            // --- Three holders seeded so that their ids, their names and their Arabic names order them three ways ---
            int savers = await fixture.SeedRoleAsync(ConnectDatabase.TenantB, (CoreResources.Role, AccessActions.Save, null));
            (int zed, _) = await SeedSecondTenantUserAsync("Cap Zed");
            (int amal, _) = await SeedSecondTenantUserAsync("Cap Amal", name2: "Cap Gamma");
            (int mona, _) = await SeedSecondTenantUserAsync("Cap Mona", name2: "Cap Beta");
            foreach (int holder in new[] { zed, amal, mona })
            {
                await fixture.SeedMembershipAsync(holder, savers, tenantId: ConnectDatabase.TenantB);
            }

            // --- Two readers of every user, one reading English and one Arabic, and a reader of no user ---
            int readers = await fixture.SeedRoleAsync(ConnectDatabase.TenantB, (CoreResources.User, AccessActions.Read, null));
            (int english, string englishSubject) = await SeedSecondTenantUserAsync("Cap English reader");
            (int arabic, string arabicSubject) = await SeedSecondTenantUserAsync("Cap Arabic reader", preferredLanguage: "ar");
            (_, string blindSubject) = await SeedSecondTenantUserAsync("Cap Blind reader");
            await fixture.SeedMembershipAsync(english, readers, tenantId: ConnectDatabase.TenantB);
            await fixture.SeedMembershipAsync(arabic, readers, tenantId: ConnectDatabase.TenantB);

            // --- Capped at two: the first two by name, or by Arabic name falling back to the name, and only they related ---
            await using (ServiceProvider capped = HoldersInstance(2))
            {
                (AccessHolders byName, int englishContent) = await HoldersAsync(capped, englishSubject);
                (AccessHolders byArabicName, int arabicContent) = await HoldersAsync(capped, arabicSubject);
                (AccessHolders blindly, _) = await HoldersAsync(capped, blindSubject);

                Assert.Equal((1, 2), (englishContent, arabicContent));
                Assert.Equal([amal, mona], byName.Holders.Select(static holder => holder.UserId));
                Assert.Equal((3, true), (byName.Total, byName.Capped));
                Assert.Equal([amal, mona], RelatedUserIds(byName));
                Assert.Equal([mona, amal], byArabicName.Holders.Select(static holder => holder.UserId));
                Assert.Equal((3, true), (byArabicName.Total, byArabicName.Capped));
                Assert.Equal([amal, mona], RelatedUserIds(byArabicName));

                // A reader of no user sees none of the three, so none was cut.
                Assert.Empty(blindly.Holders);
                Assert.Equal((3, false), (blindly.Total, blindly.Capped));
            }

            // --- Capped at three: every holder listed by name, and nothing cut ---
            await using ServiceProvider roomy = HoldersInstance(3);
            (AccessHolders all, _) = await HoldersAsync(roomy, englishSubject);

            Assert.Equal([amal, mona, zed], all.Holders.Select(static holder => holder.UserId));
            Assert.Equal((3, false), (all.Total, all.Capped));
            Assert.Equal([zed, amal, mona], RelatedUserIds(all));
        }

        // --- Helpers ---

        /// <summary>A collector of an instance's prologue round trips.</summary>
        /// <param name="services">The instance.</param>
        /// <returns>The collector.</returns>
        private static MetricCollector<double> Prologues(IServiceProvider services)
        {
            return new MetricCollector<double>(services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.ConnectPrologueDuration);
        }

        /// <summary>The variant of every prologue round trip a collector measured, in order.</summary>
        /// <param name="prologues">The collector.</param>
        /// <returns>The variants.</returns>
        private static List<string> Variants(MetricCollector<double> prologues)
        {
            return [.. prologues.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[AccessTelemetryNames.VariantTag]!)];
        }

        /// <summary>The ids of the users related to the holders, ascending.</summary>
        /// <param name="holders">The holders.</param>
        /// <returns>The ids.</returns>
        private List<int> RelatedUserIds(AccessHolders holders)
        {
            string userEntity = fixture.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(User)).Name;
            return [.. holders.Related.Sets[userEntity].Entities.Cast<User>().Select(static user => user.Id).Order()];
        }

        /// <summary>
        ///     Seeds an active user of the second tenant, with a name in its second content language
        ///     and a preferred language when given.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="name2">The name in the second content language; null for none.</param>
        /// <param name="preferredLanguage">The language the user prefers; null for none.</param>
        /// <returns>The id and the subject.</returns>
        private async Task<(int UserId, string Subject)> SeedSecondTenantUserAsync(string name, string? name2 = null, string? preferredLanguage = null)
        {
            int userId = fixture.NextUserId();
            string subject = string.Create(CultureInfo.InvariantCulture, $"sub-{userId}");
            await fixture.SeedUserAsync(ConnectDatabase.TenantB, userId, subject, name);
            await fixture.ExecuteAsync(
                ConnectDatabase.TenantB,
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Users] SET [Name2] = {Literal(name2)}, [PreferredLanguage] = {Literal(preferredLanguage)} WHERE [Id] = {userId};"));
            return (userId, subject);
        }

        /// <summary>A text as a SQL literal.</summary>
        /// <param name="text">The text; null for none.</param>
        /// <returns>The literal.</returns>
        private static string Literal(string? text)
        {
            return text is null ? "NULL" : $"N'{text.Replace("'", "''", StringComparison.Ordinal)}'";
        }

        /// <summary>
        ///     The holders of the roles' save in the second tenant, read by a caller, and the
        ///     content-language position the caller's scope negotiated.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <param name="subject">The caller's subject.</param>
        /// <returns>The holders and the content-language position.</returns>
        private async Task<(AccessHolders Holders, int ContentLanguage)> HoldersAsync(IServiceProvider instance, string subject)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject, tenantId: ConnectDatabase.TenantB, services: instance);
            AccessHolders holders = await scope.ServiceProvider.GetRequiredService<IAccessHoldersResolver>().ResolveAsync(CoreResources.Role, AccessActions.Save, Token);
            return (holders, scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current.ContentLanguageIndex);
        }

        /// <summary>
        ///     An instance over the same databases that lists at most a given number of holders and
        ///     offers English and Arabic, so a reader may read in either.
        /// </summary>
        /// <param name="maxHolders">The most holders listed.</param>
        /// <returns>The instance; the caller disposes it.</returns>
        private ServiceProvider HoldersInstance(int maxHolders)
        {
            return fixture.CreateInstance(services =>
            {
                _ = services.Replace(ServiceDescriptor.Singleton(new LanguageDeclaration(["en", "ar"], [])));
                _ = services.PostConfigure<AccessOptions>(options => options.MaxHolders = maxHolders);
            });
        }

        /// <summary>
        ///     An instance over the same databases whose fast-deny window and maximum age are one tick,
        ///     so every denial is stale and every cached set is aged.
        /// </summary>
        /// <returns>The instance; the caller disposes it.</returns>
        private ServiceProvider AgedInstance()
        {
            return fixture.CreateInstance(static services => services.PostConfigure<AccessOptions>(static options =>
            {
                options.FastDenyWindow = TimeSpan.FromTicks(1);
                options.PermissionsMaxAge = TimeSpan.FromTicks(1);
            }));
        }

        /// <summary>
        ///     An instance over the same databases whose fast-deny window is one tick, so every denial
        ///     of a set loaded before the evaluation is stale.
        /// </summary>
        /// <returns>The instance; the caller disposes it.</returns>
        private ServiceProvider StaleInstance()
        {
            return fixture.CreateInstance(static services => services.PostConfigure<AccessOptions>(static options => options.FastDenyWindow = TimeSpan.FromTicks(1)));
        }
    }
}
