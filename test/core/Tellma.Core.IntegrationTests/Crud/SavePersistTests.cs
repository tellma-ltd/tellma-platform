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
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The save pipeline's persist against a real tenant database: a create commits on one round
    ///     trip and an update on two, each returning the rows it committed; an update keeps what the
    ///     server owns; a row another connection changed, deleted, took the unique value of or whose
    ///     reference it deleted between the validation round trip and the persist is refused as the
    ///     caller can act on it, a tree move closed into a cycle among them; a move re-paths and
    ///     recounts the rows outside the payload; the caller's statements commit with the save, and a
    ///     distribution's guard rolls both back; a moved permissions tag recomposes the persist once,
    ///     re-running the validators in memory, and a second move or a load the first run never made
    ///     is stale; a read-only tenant refuses it; and a persist once sent commits whatever the
    ///     caller's token says.
    /// </summary>
    /// <param name="fixture">The seeded tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class SavePersistTests(DetailsPipelineDatabase fixture) : IClassFixture<DetailsPipelineDatabase>
    {
        /// <summary>The widgets' resource.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The nodes' resource, a tree.</summary>
        private const string Nodes = "fixture.Node";

        /// <summary>The exports' resource: no unique value and no reference, so a warm create validates without a round trip.</summary>
        private const string Exports = "fixture.Export";

        /// <summary>The table the caller's statements write: no stack owns it.</summary>
        private static readonly HashSet<TableName> Jobs = [new("core", "Jobs")];

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_warm_create_commits_on_one_round_trip_and_an_update_on_two_each_returning_the_rows_it_committed()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Exports, AccessActions.Read, null), (Exports, AccessActions.Save, null));
            await WarmAsync(subject);

            // --- A create: its persist and read-back ride one round trip ---
            Export created;
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
                int before = access.RoundTrips;
                EntitiesResult<Export> saved = await ExportService(scope).SaveAsync([new Export { Name = "first" }], new SaveOptions(), Token);

                Assert.Equal(1, access.RoundTrips - before);
                created = Assert.Single(saved.Entities);
                Assert.Equal((Assert.Single(saved.Ids), "first"), (created.Id, created.Name));
                Assert.NotEqual(default, created.CreatedAt);
            }

            Assert.Equal("first", await NameAsync(created.Id));

            // --- An update: the before image on round trip 1, the persist and its read-back on round trip 2 ---
            created.Name = "second";
            DateTimeOffset stamp = created.ModifiedAt;
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
                int before = access.RoundTrips;
                EntitiesResult<Export> saved = await ExportService(scope).SaveAsync([created], new SaveOptions(), Token);

                Assert.Equal(2, access.RoundTrips - before);
                Export updated = Assert.Single(saved.Entities);
                Assert.Equal(("second", created.CreatedAt), (updated.Name, updated.CreatedAt));
                Assert.True(updated.ModifiedAt > stamp, $"The update did not move the row's stamp from {stamp:O}: {updated.ModifiedAt:O}.");
            }

            Assert.Equal("second", await NameAsync(created.Id));
        }

        [Fact]
        public async Task An_update_keeps_the_stored_active_flag_and_what_the_server_owns_whatever_the_payload_sends()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            Widget stored = Assert.Single((await WidgetService(scope).GetByIdAsync(4, new DetailsRequest(), Token)).Entities);
            DateTimeOffset createdAt = stored.CreatedAt;
            (stored.Name, stored.IsActive, stored.CreatedAt, stored.CreatedById) = ("Delta 2", false, createdAt.AddDays(-1), 999);

            Widget committed = Assert.Single((await WidgetService(scope).SaveAsync([stored], new SaveOptions(), Token)).Entities);

            Assert.Equal(("Delta 2", true, createdAt, WellKnownIds.SystemUserId), (committed.Name, committed.IsActive, committed.CreatedAt, committed.CreatedById));
            Assert.True(await fixture.Connect.ScalarAsync<bool>(ConnectDatabase.TenantA, "SELECT [IsActive] FROM [fixture].[Widgets] WHERE [Id] = 4;"));
        }

        [Fact]
        public async Task A_row_another_caller_changes_or_deletes_before_the_commit_is_a_conflict_naming_its_modifier_or_not_found()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Exports, AccessActions.Read, null), (Exports, AccessActions.Save, null));
            int id = await CreateExportAsync(subject, "conflicted");

            // --- Changed on another connection between the two round trips: the stamp the payload echoes moved ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                Export stale = await ExportAsync(scope, id);
                stale.Name = "mine";
                SaveOptions options = new() { OnPersist = Meanwhile(Invariant($"UPDATE [fixture].[Exports] SET [Name] = N'theirs', [ModifiedAt] = DATEADD(SECOND, 1, [ModifiedAt]) WHERE [Id] = {id};")) };

                ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => ExportService(scope).SaveAsync([stale], options, Token));

                ConcurrencyConflict only = Assert.Single(conflict.Conflicts);
                Assert.Equal((id, "Caller", false), (only.Id, only.ModifiedByName, only.IsMissing));
            }

            // --- Deleted on another connection in the window: no concurrency conflict, the row is not found ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                Export stale = await ExportAsync(scope, id);
                stale.Name = "mine";
                SaveOptions options = new() { OnPersist = Meanwhile(Invariant($"DELETE FROM [fixture].[Exports] WHERE [Id] = {id};")) };

                NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => ExportService(scope).SaveAsync([stale], options, Token));

                Assert.Equal((Exports, id.ToString(CultureInfo.InvariantCulture)), (missing.Resource, Assert.Single(missing.Ids)));
                Assert.Null(await NameAsync(id));
            }
        }

        [Fact]
        public async Task A_unique_value_taken_or_a_reference_deleted_before_the_commit_is_refused_at_the_row_that_carries_it()
        {
            (_, string subject) = await fixture.SeedCallerAsync(
                (Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null), (Nodes, AccessActions.Read, null), (Nodes, AccessActions.Save, null));

            // --- Another connection commits a widget with the code the validators found free: one 422, never a 500 ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                Widget racing = new() { Code = "W-race", Kind = WidgetKind.Plain, Name = "Mine", Subject = "s-race-mine" };
                SaveOptions options = new() { OnPersist = Meanwhile(DataQueryDatabase.Stamped("[fixture].[Widgets]", "[Id], [Code], [Kind], [Name], [Subject]", "(900, 'W-race', 'Plain', N'Theirs', N's-race-theirs')")) };

                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => WidgetService(scope).SaveAsync([racing], options, Token));

                Assert.Equal(["[0].Code Unique"], Lines(refused));
            }

            // --- Another connection deletes the node a new link points at ---
            await fixture.Connect.ExecuteAsync(ConnectDatabase.TenantA, DataQueryDatabase.Stamped("fixture.Nodes", "[Id], [Code], [Name], [Node], [ParentId]", "(60, 'N-target', N'Target', '/60/', NULL)"));
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                Node linked = new() { Code = "N-linked", Name = "Linked", Links = [new Link { OtherNodeId = 60 }] };
                SaveOptions options = new() { OnPersist = Meanwhile("DELETE FROM [fixture].[Nodes] WHERE [Id] = 60;") };

                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => NodeService(scope).SaveAsync([linked], options, Token));

                Assert.Equal(["[0].Links[0].OtherNodeId Fk.NotFound"], Lines(refused));
                Assert.Equal((Nodes, 60L), (refused.Errors[0].Arguments["entity"], refused.Errors[0].Arguments["id"]));
            }

            // --- Only the other connection's writes stand: each refused save rolled back its own ---
            Assert.Equal("900", await fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, "SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), N',') FROM [fixture].[Widgets] WHERE [Code] = 'W-race';"));
            Assert.Equal(0, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM [fixture].[Nodes] WHERE [Code] = 'N-linked';"));
        }

        [Fact]
        public async Task A_move_another_connection_closes_into_a_cycle_before_the_commit_is_refused_at_the_root_and_rolled_back()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Nodes, AccessActions.Read, null), (Nodes, AccessActions.Save, null));
            await fixture.Connect.ExecuteAsync(
                ConnectDatabase.TenantA,
                DataQueryDatabase.Stamped("fixture.Nodes", "[Id], [Code], [Name], [Node], [ParentId]", "(70, 'N-cycle-a', N'A', '/70/', NULL)", "(71, 'N-cycle-b', N'B', '/71/', NULL)"));

            // --- The save moves A under B; meanwhile another connection moves B under A ---
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            Node moving = Assert.Single((await NodeService(scope).GetByIdAsync(70, new DetailsRequest(), Token)).Entities);
            moving.ParentId = 71;
            SaveOptions options = new() { ReturnEntities = false, OnPersist = Meanwhile("UPDATE [fixture].[Nodes] SET [ParentId] = 70, [Node] = '/70/71/' WHERE [Id] = 71;") };

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => NodeService(scope).SaveAsync([moving], options, Token));

            // --- One 422 at the root, never a 500; only the other connection's move stands ---
            Assert.Equal([$" {ValidationCodes.TreeCycle}"], Lines(refused));
            Assert.Equal("70:/70/::1:1,71:/70/71/:70:1:1", await TreeAsync(70, 71));
        }

        [Fact]
        public async Task A_move_re_paths_the_descendants_and_recounts_the_ancestors_outside_the_payload_without_stamping_them()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Nodes, AccessActions.Read, null), (Nodes, AccessActions.Save, null));
            await fixture.Connect.ExecuteAsync(
                ConnectDatabase.TenantA,
                DataQueryDatabase.Stamped(
                    "fixture.Nodes",
                    "[Id], [Code], [Name], [Node], [ParentId], [SubtreeCount], [ActiveSubtreeCount]",
                    "(72, 'N-move-r', N'R', '/72/', NULL, 3, 3)",
                    "(73, 'N-move-c', N'C', '/72/73/', 72, 2, 2)",
                    "(74, 'N-move-g', N'G', '/72/73/74/', 73, 1, 1)",
                    "(75, 'N-move-t', N'T', '/75/', NULL, 1, 1)"));
            const string stamps = "SELECT STRING_AGG(CONVERT(varchar(40), [ModifiedAt], 127), ',') WITHIN GROUP (ORDER BY [Id]) FROM [fixture].[Nodes] WHERE [Id] IN (72, 74, 75);";
            string? before = await fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, stamps);

            // --- The payload holds C alone, moved from under R to under T ---
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            Node moving = Assert.Single((await NodeService(scope).GetByIdAsync(73, new DetailsRequest(), Token)).Entities);
            moving.ParentId = 75;
            _ = await NodeService(scope).SaveAsync([moving], new SaveOptions { ReturnEntities = false }, Token);

            // --- G re-pathed under C's new node, R and T recounted, and none of the three stamped ---
            Assert.Equal("72:/72/::1:1,73:/75/73/:75:2:2,74:/75/73/74/:73:1:1,75:/75/::3:3", await TreeAsync(72, 75));
            Assert.Equal(before, await fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, stamps));
        }

        [Fact]
        public async Task The_callers_statements_commit_with_the_save_and_a_distributions_guard_rolls_both_back_as_a_validation_failure()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Exports, AccessActions.Read, null), (Exports, AccessActions.Save, null));

            // --- Committed together ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                SaveOptions options = new() { ReturnEntities = false, OnPersist = batch => InsertJob(batch, 7401) };
                long id = Assert.Single((await ExportService(scope).SaveAsync([new Export { Name = "with-job" }], options, Token)).Ids);

                Assert.Equal("with-job", await NameAsync((int)id));
                Assert.Equal(1, await JobsAsync(7401));
            }

            // --- A distribution's THROW in its band: the save and the caller's statement roll back ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                SaveOptions options = new()
                {
                    ReturnEntities = false,
                    OnPersist = batch =>
                    {
                        InsertJob(batch, 7402);
                        _ = batch.Sql($"THROW 50650, N'Acme.PeriodClosed', 1;", SqlOptions.None);
                    },
                };

                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => ExportService(scope).SaveAsync([new Export { Name = "rolled-back" }], options, Token));

                Assert.Equal([" Acme.PeriodClosed"], Lines(refused));
            }

            Assert.Equal(0, await JobsAsync(7402));
            Assert.Equal(0, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM [fixture].[Exports] WHERE [Name] = N'rolled-back';"));
        }

        [Fact]
        public async Task A_persist_whose_permissions_moved_since_the_scope_cached_its_caller_is_recomposed_once_and_commits_once()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync((Exports, AccessActions.Read, null), (Exports, AccessActions.Save, null));
            await WarmAsync(subject);
            await fixture.Connect.BumpPermissionsAsync(userId);

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            int called = 0;
            SaveOptions options = new()
            {
                ReturnEntities = false,
                OnPersist = batch =>
                {
                    called++;
                    InsertJob(batch, 7403);
                },
            };

            long id = Assert.Single((await ExportService(scope).SaveAsync([new Export { Name = "recomposed" }], options, Token)).Ids);

            // --- The failed guard's round trip ran nothing; the recomposed batch replayed the caller's statement ---
            Assert.Equal((1, 2), (called, access.RoundTrips - before));
            Assert.Equal("recomposed", await NameAsync((int)id));
            Assert.Equal(1, await JobsAsync(7403));
        }

        [Fact]
        public async Task A_persist_whose_permissions_move_after_its_validation_round_re_runs_the_validators_in_memory_and_commits_its_effect_once()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));
            await WarmWidgetsAsync(subject);
            WidgetSaveArm arm = fixture.Probe.Arm("W-stale-1", new WidgetSaveArm { Loads = static _ => [1], JobId = 7404 });

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            SaveOptions options = new() { ReturnEntities = false, OnPersist = Meanwhile(() => fixture.Connect.BumpPermissionsAsync(userId)) };

            long id = Assert.Single((await WidgetService(scope).SaveAsync([StaleWidget(1)], options, Token)).Ids);

            // --- The validation round, the persist its guard refused and the recomposed one: the
            // validator ran again in memory, the effect's statement was replayed and committed once ---
            Assert.Equal(3, access.RoundTrips - before);
            Assert.Equal((2, 1, 1), (arm.Validations, arm.Contributions, arm.Commits));
            Assert.Equal(1, await JobsAsync(7404));
            Assert.Equal(id, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT [Id] FROM [fixture].[Widgets] WHERE [Code] = 'W-stale-1';"));
        }

        [Theory]
        [InlineData(2, false)]
        [InlineData(3, true)]
        public async Task A_second_move_or_a_load_the_first_run_never_made_during_the_in_memory_re_run_is_stale_and_commits_nothing(int number, bool newLoad)
        {
            (int userId, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));
            await WarmWidgetsAsync(subject);
            int jobId = 7403 + number;
            WidgetSaveArm arm = fixture.Probe.Arm(
                $"W-stale-{number}",
                new WidgetSaveArm
                {
                    // The re-run either asks for a widget the round never loaded, or moves the tag again
                    // while the recomposed persist is composed.
                    Loads = run => run == 1 || !newLoad ? [1] : [2],
                    Validated = run => run == 2 && !newLoad ? fixture.Connect.BumpPermissionsAsync(userId) : Task.CompletedTask,
                    JobId = jobId,
                });

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            SaveOptions options = new() { ReturnEntities = false, OnPersist = Meanwhile(() => fixture.Connect.BumpPermissionsAsync(userId)) };

            _ = await Assert.ThrowsAsync<StaleContextException>(() => WidgetService(scope).SaveAsync([StaleWidget(number)], options, Token));

            // --- The validator re-ran, the effect contributed once, and nothing committed ---
            Assert.Equal((2, 1, 0), (arm.Validations, arm.Contributions, arm.Commits));
            Assert.Equal(0, await JobsAsync(jobId));
            Assert.Equal(0, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, Invariant($"SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Code] = 'W-stale-{number}';")));
        }

        [Fact]
        public async Task A_read_only_tenant_refuses_the_persist_of_a_save()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Exports, AccessActions.Read, null), (Exports, AccessActions.Save, null));

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject, state: TenantState.ReadOnly, services: fixture.Instance);
            TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(
                () => ExportService(scope).SaveAsync([new Export { Name = "read-only" }], new SaveOptions(), Token));

            Assert.Equal(TenantProblemCodes.ReadOnly, refused.Code);
            Assert.Equal(0, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM [fixture].[Exports] WHERE [Name] = N'read-only';"));
        }

        [Fact]
        public async Task A_sent_persist_commits_and_returns_its_rows_though_the_caller_aborts_while_it_runs()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Exports, AccessActions.Read, null), (Exports, AccessActions.Save, null));
            using var abort = CancellationTokenSource.CreateLinkedTokenSource(Token);
            SaveOptions options = new()
            {
                // The caller's statement holds the sent batch for three seconds; the abort lands in the
                // middle, a second after the persist was composed.
                OnPersist = batch =>
                {
                    _ = batch.Sql($"WAITFOR DELAY '00:00:03';", SqlOptions.None);
                    abort.CancelAfter(TimeSpan.FromSeconds(1));
                },
            };

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            EntitiesResult<Export> saved = await ExportService(scope).SaveAsync([new Export { Name = "aborted" }], options, abort.Token);

            Assert.True(abort.IsCancellationRequested, "The caller's abort did not land while the persist ran.");
            Export committed = Assert.Single(saved.Entities);
            Assert.Equal("aborted", committed.Name);
            Assert.Equal("aborted", await NameAsync(committed.Id));
        }

        /// <summary>
        ///     Another caller's commit in the window between a save's validation round trip and its
        ///     persist: the statement commits on a connection of its own while the persist is composed.
        /// </summary>
        /// <param name="statement">The other caller's statement.</param>
        /// <returns>The save's caller statements, which append nothing.</returns>
        private Action<IDataBatch> Meanwhile(string statement)
        {
            return Meanwhile(() => fixture.Connect.ExecuteAsync(ConnectDatabase.TenantA, statement));
        }

        /// <summary>
        ///     Another caller's commit in the window between a save's validation round trip and its
        ///     persist: the write commits on a connection of its own while the persist is composed.
        /// </summary>
        /// <param name="write">The other caller's write.</param>
        /// <returns>The save's caller statements, which append nothing.</returns>
        private static Action<IDataBatch> Meanwhile(Func<Task> write)
        {
            // The caller's statements are composed synchronously, after the validation round trip
            // completed and before the persist is sent; the other connection's commit is awaited off
            // the composing thread.
            return _ => Task.Run(write).GetAwaiter().GetResult();
        }

        /// <summary>Appends a caller's insert of a job no other test's job takes.</summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="jobId">The job's id.</param>
        private static void InsertJob(IDataBatch batch, int jobId)
        {
            // The job row stands for a caller's own statement: the test writes the platform's jobs table
            // from a class that owns no stack, deliberately, as a caller's OnPersist would.
#pragma warning disable TELLMA0006
            _ = batch.Sql(
                $"INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [RunAsUserId], [CreatedAt]) VALUES ({jobId}, N'fixture.probe', 'Pending', SYSUTCDATETIME(), 1, SYSUTCDATETIME());",
                new SqlOptions(writes: Jobs));
#pragma warning restore TELLMA0006
        }

        /// <summary>Saves one export, which warms the caller's connection and set and the exports' id buffer.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <returns>A task that completes when the export committed.</returns>
        private async Task WarmAsync(string subject)
        {
            _ = await CreateExportAsync(subject, "warm");
        }

        /// <summary>Saves one widget, which warms the caller's connection and set and the widgets' id buffer.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <returns>A task that completes when the widget committed.</returns>
        private async Task WarmWidgetsAsync(string subject)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            string warm = Guid.NewGuid().ToString("N");
            _ = await WidgetService(scope).SaveAsync([new Widget { Code = $"W-{warm[..12]}", Kind = WidgetKind.Plain, Name = "Warm", Subject = $"s-{warm}" }], new SaveOptions { ReturnEntities = false }, Token);
        }

        /// <summary>A new widget whose code the probe may be armed for.</summary>
        /// <param name="number">The widget's number, which names its code and subject.</param>
        /// <returns>The widget.</returns>
        private static Widget StaleWidget(int number)
        {
            return new Widget { Code = Invariant($"W-stale-{number}"), Kind = WidgetKind.Plain, Name = Invariant($"Stale {number}"), Subject = Invariant($"s-stale-{number}") };
        }

        /// <summary>A range of nodes, each as its id, node, parent, subtree count and active subtree count, ordered by id.</summary>
        /// <param name="from">The first id.</param>
        /// <param name="to">The last id.</param>
        /// <returns>The nodes, comma-separated.</returns>
        private Task<string?> TreeAsync(int from, int to)
        {
            return fixture.Connect.ScalarAsync<string>(
                ConnectDatabase.TenantA,
                Invariant($"SELECT STRING_AGG(CONCAT([Id], ':', [Node].ToString(), ':', [ParentId], ':', [SubtreeCount], ':', [ActiveSubtreeCount]), ',') WITHIN GROUP (ORDER BY [Id]) FROM [fixture].[Nodes] WHERE [Id] BETWEEN {from} AND {to};"));
        }

        /// <summary>Creates an export.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="name">The export's name.</param>
        /// <returns>The export's id.</returns>
        private async Task<int> CreateExportAsync(string subject, string name)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            return (int)Assert.Single((await ExportService(scope).SaveAsync([new Export { Name = name }], new SaveOptions { ReturnEntities = false }, Token)).Ids);
        }

        /// <summary>Reads an export in details shape.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="id">The export's id.</param>
        /// <returns>The export.</returns>
        private static async Task<Export> ExportAsync(AsyncServiceScope scope, int id)
        {
            return Assert.Single((await ExportService(scope).GetByIdAsync(id, new DetailsRequest(), Token)).Entities);
        }

        /// <summary>An export's stored name.</summary>
        /// <param name="id">The export's id.</param>
        /// <returns>The name; null when no row holds the id.</returns>
        private Task<string?> NameAsync(int id)
        {
            return fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, Invariant($"SELECT [Name] FROM [fixture].[Exports] WHERE [Id] = {id};"));
        }

        /// <summary>How many jobs hold an id.</summary>
        /// <param name="jobId">The id.</param>
        /// <returns>The count.</returns>
        private Task<int> JobsAsync(int jobId)
        {
            return fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, Invariant($"SELECT COUNT(*) FROM [core].[Jobs] WHERE [Id] = {jobId};"));
        }

        /// <summary>A refusal's errors, each as its path and code, ordered.</summary>
        /// <param name="refusal">The refusal.</param>
        /// <returns>The lines.</returns>
        private static string[] Lines(ValidationException refusal)
        {
            return [.. refusal.Errors.Select(static error => $"{error.Path} {error.Code}").Order(StringComparer.Ordinal)];
        }

        /// <summary>Formats text in the invariant culture.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The formatted text.</returns>
        private static string Invariant(FormattableString text)
        {
            return FormattableString.Invariant(text);
        }

        /// <summary>A scope's export service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Export, int> ExportService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Export, int>>();
        }

        /// <summary>A scope's widget service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Widget, int> WidgetService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Widget, int>>();
        }

        /// <summary>A scope's node service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Node, int> NodeService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Node, int>>();
        }
    }
}
