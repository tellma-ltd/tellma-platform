// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Data.Ids;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The save pipeline's validation against a real tenant database, through the real connect
    ///     prologue, runner, evaluator and id allocator: a save whose validation passes takes its ids
    ///     from the reservation that rode its validation round trip with the before images and the
    ///     uniqueness reads, then commits on one more, a unique value is refused as the collation
    ///     meets it, a stale stamp names who changed the row, a refused save gives its reserved ids
    ///     back for the next save, and a tree's cycle is judged against the stored parents.
    /// </summary>
    /// <param name="fixture">The seeded tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class SavePipelineTests(DetailsPipelineDatabase fixture) : IClassFixture<DetailsPipelineDatabase>
    {
        /// <summary>The widgets' resource.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The nodes' resource, a tree.</summary>
        private const string Nodes = "fixture.Node";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_save_takes_its_ids_from_the_reservation_that_rode_its_validation_round_trip_and_commits_on_a_second()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));

            // The first scope warms the caller's connection and set, and reads the row to update.
            Widget stored;
            await using (AsyncServiceScope cold = await fixture.ScopeAsync(subject))
            {
                stored = Assert.Single((await WidgetService(cold).GetByIdAsync(3, new DetailsRequest(), Token)).Entities);
            }

            stored.Name = "Gamma 2";
            stored.Parts = [.. stored.Parts!, new WidgetPart { Ordinal = 2, PartNotes = [new WidgetPartNote { Text = "new" }] }];
            Widget created = new() { Code = "W9", Kind = WidgetKind.Plain, Name = "New", Subject = "s9", Parts = [new WidgetPart { Ordinal = 1 }] };

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            EntitiesResult<Widget> saved = await WidgetService(scope).SaveAsync([stored, created], new SaveOptions(), Token);

            // --- The validation round trip, then the persist; every new row has its id from the
            // sequences, its parent key from the nesting ---
            Assert.Equal(2, access.RoundTrips - before);
            Assert.True(created.Id >= 1000, $"The new widget's id {created.Id} is not from its sequence.");
            WidgetPart added = stored.Parts[^1];
            Assert.True(added.Id >= 1000, $"The new part's id {added.Id} is not from its sequence.");
            Assert.Equal((3, created.Id), (added.WidgetId, created.Parts[0].WidgetId));
            Assert.Equal(added.Id, Assert.Single(added.PartNotes!).WidgetPartId);

            // --- The rows read back are the committed ones, in payload order ---
            Assert.Equal([3L, created.Id], saved.Ids);
            Assert.Equal(["Gamma 2", "New"], saved.Entities.Select(static widget => widget.Name));
            Assert.Equal([1, 2], saved.Entities[0].Parts!.Select(static part => part.Ordinal).Order());
            Assert.Equal(
                "new",
                await fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"SELECT [Text] FROM [fixture].[WidgetPartNotes] WHERE [WidgetPartId] = {added.Id};")));
        }

        [Fact]
        public async Task A_unique_value_another_row_holds_is_refused_as_the_collation_meets_it_and_a_stale_stamp_names_its_modifier()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));

            // --- Widget 1's code and widget 2's subject, in another case ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                Widget taken = new() { Code = "w1", Kind = WidgetKind.Plain, Name = "Taken", Subject = "S2" };
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => WidgetService(scope).SaveAsync([taken], new SaveOptions(), Token));

                Assert.Equal(["[0].Code Unique", "[0].Subject Unique"], refused.Errors.Select(static error => $"{error.Path} {error.Code}").Order(StringComparer.Ordinal));
            }

            // --- A stamp the stored row moved past ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                Widget stale = Assert.Single((await WidgetService(scope).GetByIdAsync(2, new DetailsRequest(), Token)).Entities);
                DateTimeOffset stamp = stale.ModifiedAt;
                stale.ModifiedAt = stamp.AddSeconds(-1);
                ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => WidgetService(scope).SaveAsync([stale], new SaveOptions(), Token));

                ConcurrencyConflict only = Assert.Single(conflict.Conflicts);
                Assert.Equal((2L, WellKnownIds.SystemUserId, false), (only.Id, only.ModifiedById, only.IsMissing));
                Assert.Equal(stamp, DateTimeOffset.Parse(only.ModifiedAt!, CultureInfo.InvariantCulture));
                Assert.False(string.IsNullOrEmpty(only.ModifiedByName));
            }
        }

        [Fact]
        public async Task A_cold_save_that_validation_refuses_gives_back_the_ids_its_reservation_took_and_the_next_save_receives_them()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));
            IdAllocator allocator = fixture.Instance.GetRequiredService<IdAllocator>();

            // --- A cold buffer: the reservation rides round trip 1, and the code is widget 1's ---
            allocator.ForgetTenant(ConnectDatabase.TenantA);
            Widget refused = new() { Code = "w1", Kind = WidgetKind.Plain, Name = "Taken", Subject = "s-cold" };
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                ValidationException unique = await Assert.ThrowsAsync<ValidationException>(() => WidgetService(scope).SaveAsync([refused], new SaveOptions(), Token));
                Assert.Equal(["[0].Code Unique"], unique.Errors.Select(static error => $"{error.Path} {error.Code}"));
            }

            // --- The reserved id went back to the buffer, and the next save receives it at once ---
            Assert.True(refused.Id >= 1000, $"The refused widget's id {refused.Id} is not from its sequence.");
            Assert.True(allocator.Buffered(ConnectDatabase.TenantA, typeof(Widget)) >= 1, "The refused save kept its reserved id.");
            Widget next = new() { Code = "W-next", Kind = WidgetKind.Plain, Name = "Next", Subject = "s-next" };
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                _ = await WidgetService(scope).SaveAsync([next], new SaveOptions { ReturnEntities = false }, Token);
            }

            Assert.Equal(refused.Id, next.Id);
        }

        [Fact]
        public async Task A_parent_under_the_row_itself_closes_a_cycle_judged_against_the_stored_parents()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Nodes, AccessActions.Read, null), (Nodes, AccessActions.Save, null));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            EntityService<Node, int> nodes = scope.ServiceProvider.GetRequiredService<EntityService<Node, int>>();
            Node root = Assert.Single((await nodes.GetByIdAsync(1, new DetailsRequest(), Token)).Entities);
            root.ParentId = 3;

            // Node 3 is under node 2, which is under node 1: node 1 under node 3 closes the cycle.
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => nodes.SaveAsync([root], new SaveOptions(), Token));

            ValidationError cycle = Assert.Single(refused.Errors);
            Assert.Equal(("[0].ParentId", "Tree.Cycle"), (cycle.Path.ToString(), cycle.Code));
        }

        /// <summary>A scope's widget service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Widget, int> WidgetService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Widget, int>>();
        }
    }
}
