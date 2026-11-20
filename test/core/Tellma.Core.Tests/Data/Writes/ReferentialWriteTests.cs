// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Saves;
using Tellma.Core.Data.TableTypes;
using Tellma.Core.Data.Writes;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Data.Writes
{
    /// <summary>
    ///     The tables the database writes by the delete actions of foreign keys: read from the model,
    ///     followed through cascades, declared by a delete and by a save that deletes child rows, and
    ///     never part of the caller's authority, for the statement or for anything after it.
    /// </summary>
    public sealed class ReferentialWriteTests : IDisposable
    {
        /// <summary>The hubs' table: the deleted root.</summary>
        private static readonly TableName Hubs = new("rf", "Hubs");

        /// <summary>The bays' table: the root's child collection.</summary>
        private static readonly TableName Bays = new("rf", "Bays");

        /// <summary>The spokes' table, whose key to a hub cascades.</summary>
        private static readonly TableName Spokes = new("rf", "Spokes");

        /// <summary>The rims' table, whose keys to a spoke and to a bay set null.</summary>
        private static readonly TableName Rims = new("rf", "Rims");

        /// <summary>The treads' table, whose key to a rim cascades.</summary>
        private static readonly TableName Treads = new("rf", "Treads");

        /// <summary>The seats' table, whose key to a spoke cascades and whose rows name users.</summary>
        private static readonly TableName Seats = new("rf", "Seats");

        /// <summary>The composition over the referential model.</summary>
        private readonly BatchHarness _harness = new(compose: ReferentialModel.Compose);

        // --- The keys, from the model ---

        [Fact]
        public void The_keys_that_act_on_a_delete_are_read_from_the_model_by_the_table_they_reference()
        {
            ForeignKeyActions keys = _harness.Services.GetRequiredService<ForeignKeyActions>();

            ForeignKeyAction spoke = Assert.Single(keys.Referencing(Hubs));
            Assert.Equal((Spokes, Hubs, ReferentialAction.Cascade), (spoke.Dependent, spoke.Principal, spoke.OnDelete));
            Assert.False(string.IsNullOrEmpty(spoke.Constraint));

            // Both of a rim's keys set null; the child key of a bay has no action and is absent.
            Assert.Equal(["HubId"], spoke.Columns);
            Assert.Equal(["Id"], spoke.PrincipalColumns);
            Assert.Equal([(Rims, ReferentialAction.SetNull), (Seats, ReferentialAction.Cascade)], keys.Referencing(Spokes).Select(static key => (key.Dependent, key.OnDelete)));
            Assert.Equal([(Rims, ReferentialAction.SetNull)], keys.Referencing(Bays).Select(static key => (key.Dependent, key.OnDelete)));
            Assert.Equal([(Treads, ReferentialAction.Cascade)], keys.Referencing(Rims).Select(static key => (key.Dependent, key.OnDelete)));
            Assert.Empty(keys.Referencing(Treads));

            // The same keys, by the table that holds them.
            Assert.Equal([Bays, Spokes], keys.HeldBy(Rims).Select(static key => key.Principal));
            Assert.Empty(keys.HeldBy(Hubs));
        }

        [Fact]
        public void A_cascade_is_followed_to_the_keys_of_the_rows_it_deletes_but_a_set_null_goes_no_further()
        {
            ForeignKeyActions keys = _harness.Services.GetRequiredService<ForeignKeyActions>();

            // The hubs cascade into the spokes, whose deletion nulls the rims; the rims keep their rows,
            // so the treads' cascade never fires.
            Assert.Equal([Rims, Seats, Spokes], keys.WrittenByDeleting([Hubs]).OrderBy(static table => table.Name, StringComparer.Ordinal));
            Assert.Equal([Hubs, Seats, Spokes], keys.RemovedByDeleting([Hubs]).OrderBy(static table => table.Name, StringComparer.Ordinal));
            Assert.Equal([Rims], keys.WrittenByDeleting([Bays]));

            // A deleted rim does cascade into its treads.
            Assert.Equal([Treads], keys.WrittenByDeleting([Rims]));
            Assert.Empty(keys.WrittenByDeleting([Treads]));
        }

        // --- A delete ---

        [Fact]
        public void A_delete_declares_the_tables_its_referencing_keys_cascade_into_or_set_null_transitively()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Delete(new DeleteSpec<Hub>.ByIds([1], null, null));

            // The bays and the hubs leave by the delete's own statements; the database removes the
            // spokes and their seats and nulls the rims' keys, so no statement writes any of them.
            BatchStatement delete = batch.Statements[^1];
            Assert.Equal([Bays, Hubs, Rims, Seats, Spokes], delete.Writes.OrderBy(static table => table.Name, StringComparer.Ordinal));
            Assert.Contains("[rf].[Bays]", delete.Text, StringComparison.Ordinal);
            Assert.Equal(1, delete.Text.Split("[rf].[Spokes]").Length - 1);
            Assert.DoesNotContain("[rf].[Rims]", delete.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(Treads, batch.WrittenTables);
        }

        [Fact]
        public void A_delete_captures_the_users_named_by_the_rows_a_cascade_reaches_before_the_rows_leave()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Delete(new DeleteSpec<Hub>.ByIds([1], null, null));

            // The seats leave by the spokes' cascade, which reports no rows, so the delete reads their
            // riders first: the seats whose spoke belongs to a deleted hub.
            string text = batch.Statements[^1].Text;
            string capture = ReservedSqlNames.Statement(batch.Statements[^1].Ordinal, UserIdCapture.ConsequenceWord(Seats));
            int insert = text.IndexOf($"INSERT INTO {capture} ([Old_RiderId], [New_RiderId])", StringComparison.Ordinal);
            Assert.True(insert >= 0, text);
            Assert.Contains($"DECLARE {capture} TABLE ([Old_RiderId] int NULL, [New_RiderId] int NULL);", text, StringComparison.Ordinal);
            Assert.Contains(
                "SELECT d.[RiderId], NULL FROM [rf].[Seats] AS d WHERE (EXISTS (SELECT 1 FROM [rf].[Spokes] AS r0 WHERE r0.[Id] = d.[SpokeId] AND ((EXISTS (SELECT 1 FROM [rf].[Hubs] AS r1 WHERE r1.[Id] = r0.[HubId] AND (r1.[Id] IN (SELECT [Id] FROM ",
                text,
                StringComparison.Ordinal);
            Assert.True(insert < text.IndexOf("DELETE", StringComparison.Ordinal), text);

            // The epilogue reads the riders, before and after, from the capture.
            UserIdSource source = Assert.Single(batch.UserIdSources, static source => source.Tables.Contains(Seats));
            Assert.Equal(("RiderId", capture), (source.Column, source.Identifier.Name));
            Assert.Equal(["Old_RiderId", "New_RiderId"], source.ReadColumns);
        }

        [Fact]
        public void A_delete_captures_the_users_of_only_the_reached_tables_that_name_them()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);

            _ = batch.Delete(new DeleteSpec<Spoke>.ByIds([1], null, null));
            _ = batch.Delete(new DeleteSpec<Rim>.ByIds([1], null, null));

            // A spoke's delete reaches its seats; a rim's reaches only its treads, which name no user.
            Assert.Contains(batch.UserIdSources, static source => source.Tables.Contains(Seats));
            Assert.DoesNotContain("uidk_", batch.Statements[^1].Text, StringComparison.Ordinal);
            Assert.Equal([Rims, Treads], batch.Statements[^1].Writes.OrderBy(static table => table.Name, StringComparer.Ordinal));
        }

        [Fact]
        public void A_referencing_table_needs_no_authority_for_the_delete_and_the_delete_grants_none_over_it()
        {
            using BatchHarness owned = new(new MapOwners(new() { [Spokes] = "spokes", [Rims] = "rims", [Hubs] = "hubs", [Bays] = "hubs" }), compose: ReferentialModel.Compose);
            DataBatch batch = owned.Batch(BatchPurpose.Persist);
            batch.SetCallerAuthority(new HashSet<TableName> { Hubs, Bays });

            // The keys' actions are the database's consequence of a delete the caller may make.
            _ = batch.Delete(new DeleteSpec<Hub>.ByIds([1], null, null));
            Assert.Contains(Spokes, batch.WrittenTables);
            Assert.Contains(Rims, batch.WrittenTables);
            Assert.Equal([Bays, Hubs], batch.CallerAuthority.OrderBy(static table => table.Name, StringComparer.Ordinal));

            // The caller's own statements over those tables are still refused, after the delete as before it.
            int statements = batch.Statements.Count;
            InvalidOperationException rims = Assert.Throws<InvalidOperationException>(
                () => batch.Sql($"UPDATE [rf].[Rims] SET [SpokeId] = NULL;", new SqlOptions(writes: new HashSet<TableName> { Rims })));
            InvalidOperationException spokes = Assert.Throws<InvalidOperationException>(
                () => batch.Sql($"DELETE FROM [rf].[Spokes];", new SqlOptions(writes: new HashSet<TableName> { Spokes })));
            Assert.Contains("rf.Rims", rims.Message, StringComparison.Ordinal);
            Assert.Contains("'rims'", rims.Message, StringComparison.Ordinal);
            Assert.Contains("rf.Spokes", spokes.Message, StringComparison.Ordinal);
            Assert.Equal(statements, batch.Statements.Count);

            // A delete of the referencing entity itself is the caller's write, and needs its authority.
            InvalidOperationException spoke = Assert.Throws<InvalidOperationException>(() => batch.Delete(new DeleteSpec<Spoke>.ByIds([2], null, null)));
            Assert.Contains("rf.Spokes", spoke.Message, StringComparison.Ordinal);
        }

        // --- A save ---

        [Fact]
        public void A_save_whose_deleted_child_rows_reach_a_table_naming_users_is_refused_when_planned()
        {
            EntityMetadata hub = _harness.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(Hub));
            TableTypeBinder binder = _harness.Services.GetRequiredService<TableTypeBinder>();
            ForeignKeyActions keys = _harness.Services.GetRequiredService<ForeignKeyActions>();

            // Were the rims to name users, a save could not tell which ones its removed bays reach.
            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                () => new SavePlan(hub, binder, keys, table => table == Rims ? ["SpokeId"] : []));
            Assert.Contains("rf.Rims", refused.Message, StringComparison.Ordinal);
            Assert.Contains("user-level version tag", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_save_declares_what_the_keys_of_the_child_rows_it_deletes_write_but_never_the_roots()
        {
            using BatchHarness owned = new(new MapOwners(new() { [Spokes] = "spokes", [Rims] = "rims" }), compose: ReferentialModel.Compose);
            DataBatch batch = owned.Batch(BatchPurpose.Persist);

            _ = batch.Save([new Hub { Id = 5, Bays = [] }]);

            // A save removes the bays its collection no longer carries, whose rims' key sets null; it
            // never deletes a hub, so the spokes' cascade is not its consequence.
            BatchStatement save = Assert.Single(batch.Statements);
            Assert.Equal([Bays, Hubs, Rims], save.Writes.OrderBy(static table => table.Name, StringComparer.Ordinal));
            Assert.DoesNotContain("[rf].[Rims]", save.Text, StringComparison.Ordinal);
        }

        // --- The core model ---

        [Fact]
        public void A_schedule_delete_declares_its_states_cascade_and_its_jobs_schedule_set_null()
        {
            using BatchHarness core = new();
            DataBatch batch = core.Batch(BatchPurpose.Persist);

            _ = batch.Delete(new DeleteSpec<Schedule>.ByIds([1000], null, null));

            Assert.Equal(
                [new TableName("core", "Jobs"), new TableName("core", "ScheduleStates"), new TableName("core", "Schedules")],
                batch.Statements[^1].Writes.OrderBy(static table => table.Name, StringComparer.Ordinal));
        }

        [Fact]
        public void A_user_delete_declares_the_stamp_and_preference_rows_that_cascade_with_the_user()
        {
            using BatchHarness core = new();
            DataBatch batch = core.Batch(BatchPurpose.Persist);

            _ = batch.Delete(new DeleteSpec<User>.ByIds([1000], null, null));

            IReadOnlySet<TableName> writes = batch.Statements[^1].Writes;
            Assert.Contains(new TableName("core", "Users"), writes);
            Assert.Contains(new TableName("core", "UserStamps"), writes);
            Assert.Contains(new TableName("core", "UserPreferences"), writes);
            Assert.Contains(new TableName("core", "NotificationPreferences"), writes);

            // Both preference tables name the user whose preferences tag they bump, so the delete
            // captures their users before the user's rows cascade away.
            Assert.Contains(batch.UserIdSources, static source => source.Tables.Contains(new TableName("core", "UserPreferences")));
            Assert.Contains(batch.UserIdSources, static source => source.Tables.Contains(new TableName("core", "NotificationPreferences")));
            Assert.Contains("INSERT INTO @tb", batch.Statements[^1].Text, StringComparison.Ordinal);
            Assert.Contains("uidk_core_UserPreferences", batch.Statements[^1].Text, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }
    }
}
