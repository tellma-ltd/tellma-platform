// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Caching;
using Tellma.Core.Data.Batches;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Access
{
    /// <summary>
    ///     The guarded runner's persists and re-runs against real tenant databases, through the real
    ///     connect prologue of each variant: the re-checks inside the transaction, a move committed
    ///     between the guard and the re-checks played by a transaction-head statement that the
    ///     rollback undoes, the re-run policies of a real cached list, and the round trips a save costs.
    /// </summary>
    /// <param name="fixture">The two tenant databases and their composition.</param>
    [Trait("Category", "Integration")]
    public sealed class GuardedPersistTests(ConnectDatabase fixture) : IClassFixture<ConnectDatabase>, IDisposable
    {
        /// <summary>Moves the tenant's permissions tag, as a role save does.</summary>
        private const string MovePermissions = "UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'permissions';";

        /// <summary>Deactivates the user the prologue resolved.</summary>
        private const string DeactivateCaller = "UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = @tm_UserId;";

        /// <summary>The table the persists write, which the jobs' stack owns.</summary>
        private static readonly HashSet<TableName> Jobs = [new("core", "Jobs")];

        /// <summary>The tables a widget save writes, which the widgets' stack owns.</summary>
        private static readonly HashSet<TableName> WidgetTables = [new("fixture", "Widgets"), new("fixture", "WidgetParts"), new("fixture", "WidgetPartNotes")];

        /// <summary>The prologue round trips, one measurement each, tagged by variant.</summary>
        private readonly MetricCollector<double> _prologues = new(
            fixture.Services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.ConnectPrologueDuration);

        /// <summary>The connect results, tagged by result.</summary>
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

        // --- The re-checks inside the transaction ---

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_permissions_tag_moved_after_the_guard_is_caught_by_the_re_check_and_costs_one_cold_reconnect_and_one_recompose(bool background)
        {
            (int userId, string subject) = await fixture.SeedCallerAsync("Ann");
            await using AsyncServiceScope scope = await ScopeAsync(userId, subject, background);
            int before = Variants().Count;

            int attempts = await PersistAsync(scope, userId, static attempt => attempt == 0 ? MovePermissions : null);

            // The failed attempt, the cold re-connect and the recomposed persist, each counted; only the last commits.
            Assert.Equal(2, attempts);
            Assert.Equal(3, Variants().Count - before);
            Assert.Equal([AccessTelemetryNames.ResultWarm, AccessTelemetryNames.ResultCold, AccessTelemetryNames.ResultWarm], Results().Skip(1));
            Assert.Equal(Variants().Count, Results().Count);
            Assert.Equal(1, await JobsAsync(userId));
        }

        [Fact]
        public async Task A_permissions_tag_moved_after_the_guard_on_every_attempt_is_refused_as_stale_after_one_recompose()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync("Abe");
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);

            _ = await Assert.ThrowsAsync<StaleContextException>(() => PersistAsync(scope, userId, static _ => MovePermissions));

            Assert.Equal(AccessTelemetryNames.ResultGuardThrash, Results()[^1]);
            Assert.Equal(0, await JobsAsync(userId));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_caller_deactivated_after_the_guard_is_refused_by_the_re_check_counted_and_dropped(bool background)
        {
            (int userId, string subject) = await fixture.SeedCallerAsync("Ben");
            await using AsyncServiceScope scope = await ScopeAsync(userId, subject, background);

            _ = await Assert.ThrowsAsync<TenantNotFoundException>(() => PersistAsync(scope, userId, static attempt => attempt == 0 ? DeactivateCaller : null));

            // --- Counted as every refusal of a deactivated user is, and nothing of the caller is kept ---
            Assert.Equal(AccessTelemetryNames.ResultDeactivated, Results()[^1]);
            TenantUserCacheKey key = new(ConnectDatabase.TenantA, userId);
            Assert.Null(fixture.Services.GetRequiredService<ConnectCache>().Find(ConnectDatabase.TenantA, subject));
            Assert.Null(fixture.Services.GetRequiredService<PermissionsCache>().Peek(key, static _ => true));
            Assert.Null(fixture.Services.GetRequiredService<ProfileCache>().Peek(key, static _ => true));
            Assert.Equal(0, await JobsAsync(userId));
        }

        [Fact]
        public async Task A_system_persist_expects_no_permissions_tag_and_re_checks_no_caller()
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync("system", kind: PrincipalKind.System);
            int jobId = fixture.NextUserId();
            Guid tag = await TagAsync("permissions");
            int before = Variants().Count;

            // --- The system user's row deactivated and the permissions moved, both inside the window;
            // the body re-activates the row, so the database is left as it was found ---
            int attempts = await PersistAsync(
                scope,
                jobId,
                static attempt => attempt == 0 ? MovePermissions + " UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = 1;" : null,
                body: "UPDATE [core].[Users] SET [IsActive] = 1 WHERE [Id] = 1;");

            Assert.Equal(1, attempts);
            Assert.Equal(1, Variants().Count - before);
            Assert.NotEqual(tag, await TagAsync("permissions"));
            Assert.Equal(1, await JobsAsync(jobId));
            Assert.True(await fixture.ScalarAsync<bool>(ConnectDatabase.TenantA, "SELECT [IsActive] FROM [core].[Users] WHERE [Id] = 1;"));
        }

        // --- The re-run policies of a cached input ---

        [Fact]
        public async Task A_validation_composed_from_a_cached_list_whose_tag_moved_reruns_once_and_reads_the_reloaded_list()
        {
            (_, string subject) = await fixture.SeedCallerAsync("Cal");
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            _ = await InsertLookupAsync(moveTag: false);
            _ = await ListAsync(scope, BatchPurpose.Read);

            // --- A save of the list commits after the cache was warmed: a new row, and the list's tag moved ---
            string saved = await InsertLookupAsync(moveTag: true);
            (int prologues, int results) = (Variants().Count, Results().Count);

            (List<string> codes, int attempts) = await ListAsync(scope, BatchPurpose.Validate);

            // The cached list was refused once; the recomposed batch reloaded it, and both round trips counted.
            Assert.Equal(2, attempts);
            Assert.Contains(saved, codes);
            Assert.Equal(2, Variants().Count - prologues);
            Assert.Equal(2, Results().Count - results);
        }

        [Fact]
        public async Task A_read_composed_from_a_cached_list_whose_tag_moved_is_served_in_one_round_trip()
        {
            (_, string subject) = await fixture.SeedCallerAsync("Cam");
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            _ = await InsertLookupAsync(moveTag: false);
            _ = await ListAsync(scope, BatchPurpose.Read);
            string saved = await InsertLookupAsync(moveTag: true);
            int before = Variants().Count;

            (List<string> codes, int attempts) = await ListAsync(scope, BatchPurpose.Read);

            Assert.Equal(1, attempts);
            Assert.DoesNotContain(saved, codes);
            Assert.Equal(1, Variants().Count - before);
        }

        [Fact]
        public async Task A_cached_list_whose_tag_moved_again_after_the_rerun_is_refused_as_stale()
        {
            (_, string subject) = await fixture.SeedCallerAsync("Cyd");
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);

            _ = await Assert.ThrowsAsync<StaleContextException>(() => ReadAsync(scope, VersionTagMismatchPolicy.Rerun, static _ => true));

            Assert.Equal(AccessTelemetryNames.ResultGuardThrash, Results()[^1]);
        }

        // --- The round trips a save costs ---

        [Fact]
        public async Task A_create_on_a_warm_id_buffer_costs_one_round_trip_and_a_save_of_an_existing_row_two()
        {
            (_, string subject) = await fixture.SeedCallerAsync("Dot");
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            IIdAllocator allocator = scope.ServiceProvider.GetRequiredService<IIdAllocator>();
            IGuardedBatchRunner runner = scope.ServiceProvider.GetRequiredService<IGuardedBatchRunner>();

            // --- Warm the id buffer with a first create, whose ids ride its validation ---
            Widget first = NewWidget();
            IdReservation? warming = null;
            _ = await runner.RunAsync(
                BatchPurpose.Validate,
                (user, batch) =>
                {
                    warming?.Dispose();
                    warming = allocator.Reserve(batch, typeof(Widget), [first]);
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome.RoundTrips,
                Token);
            using (warming)
            {
                warming!.Assign([first]);
                _ = await SaveAsync(runner, first);
            }

            // --- A create: the ids come from the buffer, so the persist is the only round trip ---
            Widget widget = NewWidget();
            int before = Variants().Count;
            using (IdReservation reservation = allocator.Reserve(scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Validate), typeof(Widget), [widget]))
            {
                Assert.True(reservation.IsImmediate);
                reservation.Assign([widget]);
                Assert.Equal(1, await SaveAsync(runner, widget));
            }

            Assert.Equal(1, Variants().Count - before);

            // --- A save of the existing row: one round trip validates, one persists ---
            widget.Name = "renamed";
            before = Variants().Count;
            _ = await runner.RunAsync(
                BatchPurpose.Validate,
                static (user, batch) =>
                {
                    _ = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome.RoundTrips,
                Token);
            Assert.Equal(1, await SaveAsync(runner, widget));

            Assert.Equal(2, Variants().Count - before);
            Assert.Equal("renamed", await fixture.ScalarAsync<string>(ConnectDatabase.TenantA, Sql($"SELECT [Name] FROM [fixture].[Widgets] WHERE [Id] = {widget.Id};")));
        }

        // --- Helpers ---

        /// <summary>Formats a statement in the invariant culture.</summary>
        /// <param name="sql">The statement.</param>
        /// <returns>The text.</returns>
        private static string Sql(FormattableString sql)
        {
            return sql.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A new widget under a fresh subject.</summary>
        /// <returns>The widget.</returns>
        private static Widget NewWidget()
        {
            string subject = "w-" + Guid.NewGuid().ToString("N");
            return new Widget { Subject = subject, Name = subject, Parts = [] };
        }

        /// <summary>Saves a widget in a guarded persist.</summary>
        /// <param name="runner">The scope's runner.</param>
        /// <param name="widget">The widget.</param>
        /// <returns>The persist's round trips.</returns>
        private static Task<int> SaveAsync(IGuardedBatchRunner runner, Widget widget)
        {
            return runner.RunAsync(
                BatchPurpose.Persist,
                (user, batch) =>
                {
                    // The save is appended as the widgets' own stack appends it, holding its tables.
                    ((DataBatch)batch).SetCallerAuthority(WidgetTables);
                    _ = batch.Save([widget]);
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome.RoundTrips,
                Token);
        }

        /// <summary>
        ///     Runs a guarded persist that inserts one job, with a transaction-head statement on the
        ///     attempts that ask for one: it runs after <c>BEGIN TRANSACTION</c> and before the
        ///     re-checks, as a save committed by another caller in that window would have.
        /// </summary>
        /// <param name="scope">The scope.</param>
        /// <param name="jobId">The job's id, which no other test's job takes.</param>
        /// <param name="head">The transaction-head statement of an attempt, by its number from zero; null for none.</param>
        /// <param name="body">A statement after the insert, or null for none.</param>
        /// <returns>The attempts composed.</returns>
        private static async Task<int> PersistAsync(AsyncServiceScope scope, int jobId, Func<int, string?> head, string? body = null)
        {
            int attempts = 0;
            _ = await scope.ServiceProvider.GetRequiredService<IGuardedBatchRunner>().RunAsync(
                BatchPurpose.Persist,
                (user, batch) =>
                {
                    var data = (DataBatch)batch;
                    if (head(attempts++) is string statement)
                    {
                        data.Append(data.Begin(BatchSection.TransactionHead), statement);
                    }

                    // The insert is appended as the jobs' own stack would append it, holding its table.
                    data.SetCallerAuthority(Jobs);
                    _ = batch.Sql(
                        $"INSERT INTO [core].[Jobs] ([Id], [HandlerKey], [Status], [DueAt], [RunAsUserId], [CreatedAt]) VALUES ({jobId}, N'fixture.probe', 'Pending', SYSUTCDATETIME(), 1, SYSUTCDATETIME());",
                        new SqlOptions(writes: Jobs));
                    if (body is not null)
                    {
                        data.Append(data.Begin(), body);
                    }

                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome.RoundTrips,
                Token);
            return attempts;
        }

        /// <summary>Runs a guarded batch that reads the cached lookup list.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="purpose">The batch's purpose: a read is served a moved list, a validation re-runs.</param>
        /// <returns>The codes of the list read, and the attempts composed.</returns>
        private static async Task<(List<string> Codes, int Attempts)> ListAsync(AsyncServiceScope scope, BatchPurpose purpose)
        {
            int attempts = 0;
            BatchResult<CacheResult<CachedEntitySet<Lookup>>>? list = null;
            List<string> codes = await scope.ServiceProvider.GetRequiredService<IGuardedBatchRunner>().RunAsync<List<string>>(
                purpose,
                (user, batch) =>
                {
                    attempts++;
                    list = batch.FromCache<Lookup>();
                    return ValueTask.CompletedTask;
                },
                (_, _) => [.. list!.Value.Value!.Items.Select(static lookup => lookup.Code)],
                Token);
            return (codes, attempts);
        }

        /// <summary>Runs a guarded read that depends on the cached lookup list, under a policy.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="policy">What a moved tag does.</param>
        /// <param name="moved">Whether an attempt, by its number from zero, composed from a list whose tag has since moved.</param>
        /// <returns>The attempts composed.</returns>
        private static async Task<int> ReadAsync(AsyncServiceScope scope, VersionTagMismatchPolicy policy, Func<int, bool> moved)
        {
            string name = VersionTagNames.ForEntity("fixture.Lookup");
            int attempts = 0;
            _ = await scope.ServiceProvider.GetRequiredService<IGuardedBatchRunner>().RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    VersionTag expected = moved(attempts++) ? VersionTag.New() : new VersionTag(user.TenantTags[name]);
                    batch.DependsOn([new VersionTagDependency(name, expected, policy)]);
                    _ = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome.RoundTrips,
                Token);
            return attempts;
        }

        /// <summary>A scope of the first tenant: a request by subject, or a background scope running as the user.</summary>
        /// <param name="userId">The user.</param>
        /// <param name="subject">The user's subject.</param>
        /// <param name="background">Whether the scope runs as the user by id.</param>
        /// <returns>The scope.</returns>
        private Task<AsyncServiceScope> ScopeAsync(int userId, string subject, bool background)
        {
            return background ? fixture.ScopeAsync(subject: null, userId: userId) : fixture.ScopeAsync(subject);
        }

        /// <summary>Inserts a lookup under a fresh id, moving the list's tag as a save of the list does, or not.</summary>
        /// <param name="moveTag">Whether the list's tag moves.</param>
        /// <returns>The lookup's code.</returns>
        private async Task<string> InsertLookupAsync(bool moveTag)
        {
            int id = fixture.NextUserId();
            string move = moveTag ? $"UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'{VersionTagNames.ForEntity("fixture.Lookup")}';" : string.Empty;
            await fixture.ExecuteAsync(
                ConnectDatabase.TenantA,
                Sql($"INSERT INTO [fixture].[Lookups] ([Id], [Code], [Name], [IsActive], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById]) VALUES ({id}, 'L{id}', N'Lookup {id}', 1, SYSDATETIMEOFFSET(), 1, SYSDATETIMEOFFSET(), 1); {move}"));
            return string.Create(CultureInfo.InvariantCulture, $"L{id}");
        }

        /// <summary>The jobs of an id that committed.</summary>
        /// <param name="jobId">The id.</param>
        /// <returns>The count.</returns>
        private Task<int> JobsAsync(int jobId)
        {
            return fixture.ScalarAsync<int>(ConnectDatabase.TenantA, Sql($"SELECT COUNT(*) FROM [core].[Jobs] WHERE [Id] = {jobId};"));
        }

        /// <summary>A tenant-level tag of the first tenant as stored.</summary>
        /// <param name="name">The tag's name.</param>
        /// <returns>The tag.</returns>
        private async Task<Guid> TagAsync(string name)
        {
            return await fixture.ScalarAsync<Guid>(ConnectDatabase.TenantA, $"SELECT [Tag] FROM [core].[VersionTags] WHERE [Name] = N'{name}';");
        }

        /// <summary>The variant of every prologue round trip measured so far, in order.</summary>
        /// <returns>The variants.</returns>
        private List<string> Variants()
        {
            return [.. _prologues.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[AccessTelemetryNames.VariantTag]!)];
        }

        /// <summary>Every connect result counted so far, in order.</summary>
        /// <returns>The results.</returns>
        private List<string> Results()
        {
            return [.. _results.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[AccessTelemetryNames.ResultTag]!)];
        }
    }
}
