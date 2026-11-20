// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Crud.Conformance;
using Tellma.Core.Testing.Crud;
using Tellma.Module.Gl;
using static System.FormattableString;

namespace Tellma.Core.IntegrationTests.Gl
{
    /// <summary>
    ///     The GL module's centers over a fixture tenant: a save resolves parents the payload holds in
    ///     two round trips, a center's parent must group, both when it is placed and when the parent's
    ///     type changes, a center with children is deleted only with them, the built-in actions recount
    ///     the active subtrees in one round trip, and the sample-centers step writes its tree in three
    ///     round trips and rewrites it in place.
    /// </summary>
    /// <param name="fixture">The tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class CenterTests(ConformanceDatabase fixture) : IClassFixture<ConformanceDatabase>
    {
        /// <summary>The codes of the default sample centers.</summary>
        private static readonly string[] SampleCodes = ["HQ", "BU-TRD", "OP-TRD", "SL-TRD", "BU-MFG", "OP-MFG", "SV-IT", "SV-HR"];

        /// <summary>The number of the next test's code prefix, shared by every case of the class.</summary>
        private static int _prefix;

        /// <summary>The running test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- Saves ---

        [Fact]
        public async Task A_save_whose_parents_are_in_the_payload_takes_two_round_trips_and_resolves_them()
        {
            string p = Prefix();
            StackConformanceCaller caller = await CallerAsync();

            // --- A root, a unit under it and an operation under the unit, all new ---
            Center[] centers =
            [
                New(-1, null, CenterType.Abstract, p + "HQ"),
                New(-2, -1, CenterType.BusinessUnit, p + "BU"),
                New(-3, -2, CenterType.Operation, p + "OP"),
            ];
            int trips = await CountedAsync(caller, service => service.SaveAsync(centers, new SaveOptions { ReturnEntities = false }, Token));

            // The natural key's uniqueness load rides the first round trip; the persist is the second.
            Assert.Equal(2, trips);
            Assert.Equal(p + "HQ", await ParentCodeAsync(p + "BU"));
            Assert.Equal(p + "BU", await ParentCodeAsync(p + "OP"));
            Assert.Equal(3, await CountOfAsync(p + "HQ", "SubtreeCount"));
        }

        [Fact]
        public async Task A_center_under_a_parent_that_does_not_group_is_refused_whether_the_parent_is_stored_or_in_the_payload()
        {
            string p = Prefix();
            StackConformanceCaller caller = await CallerAsync();
            int unit = await CreateAsync(caller, New(0, null, CenterType.BusinessUnit, p + "BU"));
            int operation = await CreateAsync(caller, New(0, unit, CenterType.Operation, p + "OP"));

            // --- A stored parent that does not group ---
            ValidationException stored = await Assert.ThrowsAsync<ValidationException>(
                () => SaveAsync(caller, [New(0, operation, CenterType.Sale, p + "S1")]));
            Assert.Equal(["[0].ParentId Gl.Centers.ParentMustBeGrouping"], Lines(stored));

            // --- A new parent of the payload that does not group ---
            ValidationException inPayload = await Assert.ThrowsAsync<ValidationException>(
                () => SaveAsync(caller, [New(-1, null, CenterType.Sale, p + "S2"), New(-2, -1, CenterType.Sale, p + "S3")]));
            Assert.Equal(["[1].ParentId Gl.Centers.ParentMustBeGrouping"], Lines(inPayload));

            // --- A grouping parent accepts the same center ---
            _ = await CreateAsync(caller, New(0, unit, CenterType.Sale, p + "S4"));
            Assert.Equal(p + "BU", await ParentCodeAsync(p + "S4"));
        }

        [Fact]
        public async Task A_parent_that_keeps_a_child_cannot_change_to_a_type_that_does_not_group_unless_its_children_move_away()
        {
            string p = Prefix();
            StackConformanceCaller caller = await CallerAsync();
            int unit = await CreateAsync(caller, New(0, null, CenterType.BusinessUnit, p + "BU"));
            int child = await CreateAsync(caller, New(0, unit, CenterType.Operation, p + "OP"));

            // --- The unit becomes a service while its child stays ---
            Center changed = await LoadAsync(caller, unit);
            changed.CenterType = CenterType.Service;
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SaveAsync(caller, [changed]));
            Assert.Equal(["[0].CenterType Gl.Centers.ParentMustBeGrouping"], Lines(refused));

            // --- The same change, with the child moved to the root in the same payload ---
            Center moving = await LoadAsync(caller, child);
            moving.ParentId = null;
            _ = await SaveAsync(caller, [changed, moving]);
            Assert.Equal("Service", await ScalarAsync<string>(Invariant($"SELECT [CenterType] FROM [gl].[Centers] WHERE [Id] = {unit}")));
            Assert.Null(await ParentCodeAsync(p + "OP"));
        }

        // --- Deletes ---

        [Fact]
        public async Task A_center_with_children_is_deleted_only_with_them_and_a_delete_naming_its_whole_subtree_takes_two_round_trips()
        {
            string p = Prefix();
            StackConformanceCaller caller = await CallerAsync();
            int root = await CreateAsync(caller, New(0, null, CenterType.Abstract, p + "HQ"));
            int unit = await CreateAsync(caller, New(0, root, CenterType.BusinessUnit, p + "BU"));
            int sale = await CreateAsync(caller, New(0, unit, CenterType.Sale, p + "SL"));

            // --- The root, then the unit, each alone: refused, the subtree still there ---
            foreach (int id in (int[])[root, unit])
            {
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(
                    () => CountedAsync(caller, service => service.DeleteByIdsAsync([id], null, Token)));
                Assert.Equal(["Ids[0] Gl.Centers.HasChildren"], Lines(refused));
            }

            Assert.Equal(3, await CountOfAsync(p + "HQ", "SubtreeCount"));

            // --- The whole subtree by its ids, leaf first: accepted ---
            int trips = await CountedAsync(caller, async service => Assert.Equal(3, (await service.DeleteByIdsAsync([sale, unit, root], null, Token)).Count));
            Assert.Equal(2, trips);
            Assert.Equal(0, await ScalarAsync<int>(Invariant($"SELECT COUNT(*) FROM [gl].[Centers] WHERE [Id] IN ({root}, {unit}, {sale})")));
        }

        [Fact]
        public async Task A_delete_with_descendants_takes_the_subtree_in_two_round_trips()
        {
            string p = Prefix();
            StackConformanceCaller caller = await CallerAsync();
            int root = await CreateAsync(caller, New(0, null, CenterType.Abstract, p + "HQ"));
            int unit = await CreateAsync(caller, New(0, root, CenterType.BusinessUnit, p + "BU"));
            int sale = await CreateAsync(caller, New(0, unit, CenterType.Sale, p + "SL"));

            int trips = await CountedAsync(caller, async service => Assert.Equal(3, (await service.DeleteWithDescendantsAsync([root], Token)).Count));

            Assert.Equal(2, trips);
            Assert.Equal(0, await ScalarAsync<int>(Invariant($"SELECT COUNT(*) FROM [gl].[Centers] WHERE [Id] IN ({root}, {unit}, {sale})")));
        }

        // --- Activation ---

        [Fact]
        public async Task Deactivating_and_activating_a_center_recounts_its_ancestors_active_subtrees_in_one_round_trip_each()
        {
            string p = Prefix();
            StackConformanceCaller caller = await CallerAsync();
            int root = await CreateAsync(caller, New(0, null, CenterType.Abstract, p + "HQ"));
            int unit = await CreateAsync(caller, New(0, root, CenterType.BusinessUnit, p + "BU"));
            int sale = await CreateAsync(caller, New(0, unit, CenterType.Sale, p + "SL"));

            // --- The leaf deactivated: each ancestor counts one active center fewer, and as many centers ---
            Assert.Equal(1, await CountedAsync(caller, service => service.DeactivateAsync([sale], cancellationToken: Token)));
            Assert.Equal((2, 3), (await CountOfAsync(p + "HQ", "ActiveSubtreeCount"), await CountOfAsync(p + "HQ", "SubtreeCount")));
            Assert.Equal(1, await CountOfAsync(p + "BU", "ActiveSubtreeCount"));

            // --- The leaf activated: the counts come back ---
            Assert.Equal(1, await CountedAsync(caller, service => service.ActivateAsync([sale], cancellationToken: Token)));
            Assert.Equal(3, await CountOfAsync(p + "HQ", "ActiveSubtreeCount"));
            Assert.Equal(2, await CountOfAsync(p + "BU", "ActiveSubtreeCount"));
        }

        // --- Sample centers ---

        [Fact]
        public async Task The_sample_centers_step_writes_its_tree_in_three_round_trips_and_a_rerun_rewrites_it_in_place()
        {
            // --- The first run: the read finds none, the save inserts all eight ---
            Assert.Equal(3, await RunSampleCentersAsync());
            string sampled = string.Join(", ", SampleCodes.Select(static code => $"N'{code}'"));
            string stored = $"SELECT COUNT(*) FROM [gl].[Centers] WHERE [Code] IN ({sampled})";
            Assert.Equal(SampleCodes.Length, await ScalarAsync<int>(stored));
            Assert.Equal("HQ", await ParentCodeAsync("BU-TRD"));
            Assert.Equal("BU-MFG", await ParentCodeAsync("OP-MFG"));
            Assert.Equal(SampleCodes.Length, await CountOfAsync("HQ", "SubtreeCount"));
            long ids = await ScalarAsync<long>($"SELECT SUM(CAST([Id] AS bigint)) FROM [gl].[Centers] WHERE [Code] IN ({sampled})");

            // --- The re-run: the read finds all eight, the save updates them under the same ids ---
            Assert.Equal(3, await RunSampleCentersAsync());
            Assert.Equal(SampleCodes.Length, await ScalarAsync<int>(stored));
            Assert.Equal(ids, await ScalarAsync<long>($"SELECT SUM(CAST([Id] AS bigint)) FROM [gl].[Centers] WHERE [Code] IN ({sampled})"));
        }

        /// <summary>A new center's values.</summary>
        /// <param name="id">Its id: zero, or a temporary id below zero that other rows of the payload name.</param>
        /// <param name="parentId">Its parent's id, stored or temporary; null for a root.</param>
        /// <param name="type">Its type.</param>
        /// <param name="code">Its code, which also names it.</param>
        /// <returns>The center.</returns>
        private static Center New(int id, int? parentId, CenterType type, string code)
        {
            return new Center { Id = id, ParentId = parentId, CenterType = type, Code = code, Name = code };
        }

        /// <summary>A code prefix no other case of the run uses, so the cases share the tenant's unique codes.</summary>
        /// <returns>The prefix.</returns>
        private static string Prefix()
        {
            return Invariant($"T{Interlocked.Increment(ref _prefix)}-");
        }

        /// <summary>Each error of a validation failure as its path and code.</summary>
        /// <param name="failure">The failure.</param>
        /// <returns>The lines, in ordinal order.</returns>
        private static List<string> Lines(ValidationException failure)
        {
            return [.. failure.Errors.Select(static error => $"{error.Path} {error.Code}").Order(StringComparer.Ordinal)];
        }

        /// <summary>The centers' service in a scope.</summary>
        /// <param name="scope">The scope's services.</param>
        /// <returns>The service.</returns>
        private static EntityService<Center, int> Service(IServiceProvider scope)
        {
            return scope.GetRequiredService<EntityService<Center, int>>();
        }

        /// <summary>
        ///     Seeds a caller holding every securable of the centers, and warms its caches with a read,
        ///     so the calls a case counts run on the warm path.
        /// </summary>
        /// <returns>The caller.</returns>
        private async Task<StackConformanceCaller> CallerAsync()
        {
            StackConformanceCaller caller = await fixture.SeedCallerAsync(
                [.. new[] { AccessActions.Read, AccessActions.Save, AccessActions.Delete, AccessActions.Activate }.Select(static action => new StackGrant(GlResources.Center, action))],
                Token);
            _ = await CountedAsync(caller, static service => service.QueryAsync(new QueryRequest { Select = "Id", Take = 1 }, Token));
            return caller;
        }

        /// <summary>Runs a call on the centers' service in a scope of a caller, and counts its round trips.</summary>
        /// <param name="caller">The caller.</param>
        /// <param name="call">The call.</param>
        /// <returns>The round trips it took, which the driver's count agrees with.</returns>
        private async Task<int> CountedAsync(StackConformanceCaller caller, Func<EntityService<Center, int>, Task> call)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(caller, Token);
            return await CountedInAsync(scope.ServiceProvider, () => call(Service(scope.ServiceProvider)));
        }

        /// <summary>Runs a call in a scope, and counts its round trips.</summary>
        /// <param name="scope">The scope's services.</param>
        /// <param name="call">The call.</param>
        /// <returns>The round trips it took, which the driver's count agrees with.</returns>
        private async Task<int> CountedInAsync(IServiceProvider scope, Func<Task> call)
        {
            DataAccessScope access = scope.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            long? serverBefore = fixture.ServerRoundTrips(scope);
            await call();
            int trips = access.RoundTrips - before;
            Assert.Equal(trips, fixture.ServerRoundTrips(scope) - serverBefore);
            return trips;
        }

        /// <summary>Saves centers as a caller.</summary>
        /// <param name="caller">The caller.</param>
        /// <param name="centers">The centers.</param>
        /// <returns>The round trips the save took.</returns>
        private Task<int> SaveAsync(StackConformanceCaller caller, Center[] centers)
        {
            return CountedAsync(caller, service => service.SaveAsync(centers, new SaveOptions { ReturnEntities = false }, Token));
        }

        /// <summary>Stores one center as a caller.</summary>
        /// <param name="caller">The caller.</param>
        /// <param name="center">The center.</param>
        /// <returns>Its id.</returns>
        private async Task<int> CreateAsync(StackConformanceCaller caller, Center center)
        {
            _ = await SaveAsync(caller, [center]);
            return await ScalarAsync<int>($"SELECT [Id] FROM [gl].[Centers] WHERE [Code] = N'{center.Code}'");
        }

        /// <summary>Loads a stored center as a caller, its stamp included.</summary>
        /// <param name="caller">The caller.</param>
        /// <param name="id">The center's id.</param>
        /// <returns>The center.</returns>
        private async Task<Center> LoadAsync(StackConformanceCaller caller, int id)
        {
            Center? loaded = null;
            _ = await CountedAsync(caller, async service => loaded = Assert.Single((await service.GetByIdAsync(id, DetailsRequest.None, Token)).Entities));
            return loaded!;
        }

        /// <summary>Runs the sample-centers step as provisioning runs it: in a system scope of the first tenant.</summary>
        /// <returns>The round trips it took.</returns>
        private async Task<int> RunSampleCentersAsync()
        {
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(
                "system", WellKnownIds.SystemUserId, PrincipalKind.System, background: true, services: fixture.Services);
            IServiceProvider services = scope.ServiceProvider;
            GlSampleCentersStep<Center> step = services.GetRequiredService<GlSampleCentersStep<Center>>();
            TenantProvisioningContext context = new(ConnectDatabase.Descriptor(ConnectDatabase.TenantA), services, IsNew: true, AdminSubject: null, AdminEmail: null);
            return await CountedInAsync(services, () => step.RunAsync(context));
        }

        /// <summary>The code of a stored center's parent.</summary>
        /// <param name="code">The center's code.</param>
        /// <returns>The parent's code; null for a root.</returns>
        private Task<string?> ParentCodeAsync(string code)
        {
            return ScalarAsync<string?>(
                $"SELECT p.[Code] FROM [gl].[Centers] AS c LEFT JOIN [gl].[Centers] AS p ON p.[Id] = c.[ParentId] WHERE c.[Code] = N'{code}'");
        }

        /// <summary>One of a stored center's subtree counts.</summary>
        /// <param name="code">The center's code.</param>
        /// <param name="column">The count's column: <c>SubtreeCount</c> or <c>ActiveSubtreeCount</c>.</param>
        /// <returns>The count.</returns>
        private Task<int> CountOfAsync(string code, string column)
        {
            return ScalarAsync<int>($"SELECT [{column}] FROM [gl].[Centers] WHERE [Code] = N'{code}'");
        }

        /// <summary>Reads one value from the first tenant's database.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="query">The query.</param>
        /// <returns>The value.</returns>
        private async Task<T> ScalarAsync<T>(string query)
        {
            return (await fixture.Connect.ScalarAsync<T>(ConnectDatabase.TenantA, query))!;
        }
    }
}
