// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Tests.Data.Batches
{
    /// <summary>
    ///     The persist frame with no database: the transaction head, the re-checks and the body in
    ///     order with the commit last; the expected tags the guard compares; who gets the re-checks;
    ///     and the batch's probe and tag declarations.
    /// </summary>
    public sealed class PersistFrameTests : IDisposable
    {
        private static readonly TableName Widgets = new("fixture", "Widgets");

        private static readonly TableName Profiles = new("fixture", "Profiles");

        private static readonly TableName Currencies = new("fixture", "Currencies");

        private static readonly VersionTag Settings = Tag(1);

        private static readonly VersionTag Permissions = Tag(2);

        /// <summary>The prologue stand-in: declares the guard variable.</summary>
        private const string Prologue = "DECLARE @tm_Guard bit = 1;";

        /// <summary>The transaction-head stand-in: an application lock.</summary>
        private const string Lock = "EXEC sp_getapplock @Resource = N'fx', @LockMode = 'Exclusive';";

        /// <summary>Widgets bump their entity's tag, currencies a plain tag; profiles write the users' preferences tags.</summary>
        private readonly BatchHarness _harness = new(
            registry: new MapRegistry(
                new Dictionary<TableName, string[]> { [Widgets] = [VersionTagNames.ForEntity("Widget")], [Currencies] = ["currencies"] },
                new UserVersionTagRule(Profiles, UserVersionTagNames.Preferences, "UserId")));

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_writing_persist_batch_takes_its_lock_then_re_checks_then_runs_its_body_and_commits_last()
        {
            DataBatch batch = Marked(BatchPurpose.Persist, GuardPremises.ForUser(Settings, Permissions), new DelegateContributor(1, static (batch, stage) =>
            {
                // The lock is a transaction-head statement the epilogue stage appends, like the connect prologue's.
                var data = (DataBatch)batch;
                if (stage == DataBatchStage.Epilogue)
                {
                    data.Append(data.Begin(BatchSection.TransactionHead), Lock);
                    data.Append(data.Begin(), "PRINT 'epilogue';");
                }
            }));
            _ = batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE [Id] = {1};", new SqlOptions(writes: Set(Widgets)));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);
            PersistChecks checks = Assert.IsType<PersistChecks>(assembly.Checks);

            Assert.Equal(
                string.Join(
                    "\n",
                    BatchAssembly.SessionSettings,
                    BatchAssembly.SchemaGuardText,
                    Prologue,
                    BatchAssembly.GuardOpen,
                    BatchAssembly.TransactionOpen,
                    Lock,
                    checks.Text,
                    "DELETE FROM [fixture].[Widgets] WHERE [Id] = @tb1_p0;",
                    "PRINT 'epilogue';",
                    BatchAssembly.TransactionCommit,
                    BatchAssembly.GuardClose,
                    string.Empty),
                assembly.Text);

            // --- A user's re-checks: the tag rows one at a time in name order, a bumped name under an
            // exclusive lock and every other under a shared one, then the caller's stamp under a shared
            // lock, then the caller ---
            Assert.Equal(
                string.Join(
                    "\n",
                    "DECLARE @tm_guardOrder TABLE ([Name] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY, [Tag] uniqueidentifier NULL, [Bumped] bit NOT NULL);",
                    "INSERT INTO @tm_guardOrder ([Name], [Tag], [Bumped]) SELECT [Name], [Tag], [Bumped] FROM @tm_expectedTags;",
                    "DECLARE @tm_guardName nvarchar(128) = (SELECT MIN([Name]) FROM @tm_guardOrder), @tm_guardExpected uniqueidentifier, @tm_guardBumped bit, @tm_guardTag uniqueidentifier, @tm_guardStale bit = 0;",
                    "WHILE @tm_guardName IS NOT NULL",
                    "BEGIN",
                    "    SELECT @tm_guardExpected = [Tag], @tm_guardBumped = [Bumped] FROM @tm_guardOrder WHERE [Name] = @tm_guardName;",
                    "    IF @tm_guardBumped = 1",
                    "        SET @tm_guardTag = (SELECT [Tag] FROM [core].[VersionTags] WITH (XLOCK, ROWLOCK, FORCESEEK) WHERE [Name] = @tm_guardName);",
                    "    ELSE",
                    "        SET @tm_guardTag = (SELECT [Tag] FROM [core].[VersionTags] WITH (REPEATABLEREAD, ROWLOCK, FORCESEEK) WHERE [Name] = @tm_guardName);",
                    "    IF @tm_guardExpected <> @tm_guardTag SET @tm_guardStale = 1;",
                    "    SET @tm_guardName = (SELECT TOP (1) [Name] FROM @tm_guardOrder WHERE [Name] > @tm_guardName ORDER BY [Name]);",
                    "END;",
                    "IF @tm_guardStale = 1",
                    "   OR (SELECT SUM(IIF(s.[PermissionsTag] = @tm_ExpectedUserPermissionsTag, 0, 1)) FROM [core].[UserStamps] AS s WITH (REPEATABLEREAD, ROWLOCK) WHERE s.[UserId] = @tm_UserId) > 0",
                    "    THROW 50412, N'StaleVersionTag', 1;",
                    "IF NOT EXISTS (SELECT 1 FROM [core].[Users] WHERE [Id] = @tm_UserId AND [IsActive] = 1)",
                    "    THROW 50401, N'CallerInvalid', 1;"),
                checks.Text);

            // --- The guard's table binds once, after the fingerprint; the checks' lines are the frame's ---
            Assert.Equal([ReservedSqlNames.SchemaFingerprint, ReservedSqlNames.ExpectedTags, "@tb1_p0"], assembly.Parameters.Select(static parameter => parameter.Name));
            int checksLine = assembly.Text.Split('\n').ToList().IndexOf(checks.Text.Split('\n')[0]) + 1;
            Assert.Null(assembly.StatementOrdinalAt(checksLine));
        }

        [Fact]
        public async Task The_expected_tags_hold_settings_permissions_and_the_dependencies_in_name_order_and_mark_what_the_batch_bumps()
        {
            DataBatch batch = Marked(BatchPurpose.Persist, GuardPremises.ForUser(Settings, Permissions));
            batch.DependsOn(
            [
                Dependency("currencies", Tag(3)),
                Dependency(VersionTagNames.Entities, Tag(4)),
                Dependency(VersionTagNames.ForEntity("Widget"), Tag(5)),
                Dependency(VersionTagNames.ForEntity("Agent"), Tag(6)),
            ]);
            batch.BumpVersionTag("currencies");
            _ = batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE 1 = 0;", new SqlOptions(writes: Set(Widgets)));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            // The written table's tag, the explicit bump, and the entities tag any entity bump implies.
            Assert.Equal(
                [
                    ("currencies", Tag(3).Value, true),
                    (VersionTagNames.Entities, Tag(4).Value, true),
                    (VersionTagNames.ForEntity("Agent"), Tag(6).Value, false),
                    (VersionTagNames.ForEntity("Widget"), Tag(5).Value, true),
                    (VersionTagNames.Permissions, Permissions.Value, false),
                    (VersionTagNames.Settings, Settings.Value, false),
                ],
                assembly.Checks!.ExpectedTags.Select(static row => (row.Name, row.Tag, row.Bumped)));
        }

        [Fact]
        public async Task A_name_the_batch_bumps_without_expecting_it_is_locked_in_the_same_pass_and_never_compared()
        {
            DataBatch batch = Marked(BatchPurpose.Persist, GuardPremises.ForUser(Settings, Permissions));
            _ = batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE 1 = 0;", new SqlOptions(writes: Set(Widgets)));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            Assert.Equal(
                [
                    (VersionTagNames.Entities, PersistChecks.NoExpectation, true),
                    (VersionTagNames.ForEntity("Widget"), PersistChecks.NoExpectation, true),
                    (VersionTagNames.Permissions, Permissions.Value, false),
                    (VersionTagNames.Settings, Settings.Value, false),
                ],
                assembly.Checks!.ExpectedTags.Select(static row => (row.Name, row.Tag, row.Bumped)));
        }

        [Fact]
        public async Task A_batch_that_bumps_no_tenant_tag_marks_no_expected_row_for_the_exclusive_lock()
        {
            DataBatch batch = Marked(BatchPurpose.Persist, GuardPremises.ForUser(Settings, Permissions));
            _ = batch.Sql($"DELETE FROM [core].[Jobs] WHERE 1 = 0;", new SqlOptions(writes: Set(new TableName("core", "Jobs"))));

            PersistChecks checks = (await BatchHarness.SealAsync(batch)).Checks!;

            Assert.All(checks.ExpectedTags, static row => Assert.False(row.Bumped));
            Assert.Contains("[core].[UserStamps] AS s WITH (REPEATABLEREAD, ROWLOCK)", checks.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_expectation_of_no_tag_is_compared_like_any_other()
        {
            // A kind read while its row was missing expects no tag: once the row exists, the guard fails.
            DataBatch batch = Marked(BatchPurpose.Persist, GuardPremises.ForUser(Settings, VersionTag.None));
            batch.BumpVersionTag(VersionTagNames.Settings);
            _ = batch.Sql($"DELETE FROM [core].[Jobs] WHERE 1 = 0;", new SqlOptions(writes: Set(new TableName("core", "Jobs"))));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            Assert.Equal(
                [(VersionTagNames.Permissions, (Guid?)Guid.Empty, false), (VersionTagNames.Settings, Settings.Value, true)],
                assembly.Checks!.ExpectedTags.Select(static row => (row.Name, row.Tag, row.Bumped)));
            Assert.Null(PersistChecks.NoExpectation);
        }

        [Fact]
        public async Task Two_expectations_of_one_tag_that_disagree_expect_a_tag_no_row_holds_so_the_guard_fails()
        {
            DataBatch batch = Marked(BatchPurpose.Persist, GuardPremises.ForUser(Settings, Permissions));
            batch.DependsOn(
            [
                Dependency("currencies", Tag(3)),
                Dependency("currencies", Tag(7)),
                Dependency("units", Tag(8)),
                Dependency("units", Tag(8)),
                Dependency(VersionTagNames.Settings, Tag(9)),
            ]);
            _ = batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE 1 = 0;", new SqlOptions(writes: Set(Widgets)));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            var expected = assembly.Checks!.ExpectedTags.ToDictionary(static row => row.Name, static row => row.Tag);
            Assert.Equal(PersistChecks.Conflicting, expected["currencies"]);
            Assert.Equal(Tag(8).Value, expected["units"]);
            Assert.Equal(PersistChecks.Conflicting, expected[VersionTagNames.Settings]);
        }

        [Fact]
        public async Task The_platforms_own_batch_re_checks_the_tags_alone()
        {
            DataBatch batch = Marked(BatchPurpose.Persist, GuardPremises.ForSystem(Settings));
            _ = batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE 1 = 0;", new SqlOptions(writes: Set(Widgets)));

            PersistChecks checks = (await BatchHarness.SealAsync(batch)).Checks!;

            Assert.DoesNotContain("UserStamps", checks.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(ReservedSqlNames.UserId, checks.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(VersionTagNames.Permissions, checks.ExpectedTags.Select(static row => row.Name));
            Assert.EndsWith($"THROW {TellmaSqlErrors.StaleVersionTag}, N'{TellmaSqlErrors.StaleVersionTagCode}', 1;", checks.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_batch_that_writes_the_callers_stamp_reads_it_under_an_update_lock()
        {
            // --- Writing the stamps table itself ---
            DataBatch stamps = Marked(BatchPurpose.Persist, GuardPremises.ForUser(Settings, Permissions));
            _ = stamps.Sql($"UPDATE [core].[UserStamps] SET [LastActiveAt] = SYSDATETIMEOFFSET() WHERE 1 = 0;", new SqlOptions(writes: Set(new TableName("core", "UserStamps"))));
            Assert.Contains("[core].[UserStamps] AS s WITH (UPDLOCK, ROWLOCK)", (await BatchHarness.SealAsync(stamps)).Checks!.Text, StringComparison.Ordinal);

            // --- Writing a table whose rule bumps the preferences tag ---
            DataBatch profiles = Marked(BatchPurpose.Persist, GuardPremises.ForUser(Settings, Permissions));
            SqlIdentifier users = profiles.DeclareIdTable();
            _ = profiles.Sql($"DELETE FROM [fixture].[Profiles] OUTPUT deleted.[UserId] INTO {users} WHERE 1 = 0;", new SqlOptions(writes: Set(Profiles), userIds: users));
            Assert.Contains("[core].[UserStamps] AS s WITH (UPDLOCK, ROWLOCK)", (await BatchHarness.SealAsync(profiles)).Checks!.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Only_a_writing_persist_batch_that_carries_the_connect_prologue_re_checks_in_any_transaction_mode()
        {
            var premises = GuardPremises.ForUser(Settings, Permissions);

            // --- Reads and validation, a persist with no write, and an unmarked persist do not ---
            foreach ((BatchPurpose purpose, bool marked, bool writes) in new[]
            {
                (BatchPurpose.Read, true, true),
                (BatchPurpose.Validate, true, true),
                (BatchPurpose.Persist, true, false),
                (BatchPurpose.Persist, false, true),
                (BatchPurpose.Maintenance, false, true),
            })
            {
                DataBatch batch = marked ? Marked(purpose, premises) : _harness.Batch(purpose);
                _ = batch.Sql($"DELETE FROM [fixture].[Widgets] WHERE 1 = 0;", writes ? new SqlOptions(writes: Set(Widgets)) : null);
                Assert.Null((await BatchHarness.SealAsync(batch)).Checks);
            }

            // --- Autocommit and an explicit transaction re-check without a transaction in the text ---
            // The write bumps no tag: an autocommit batch may not bump one.
            foreach (TransactionMode mode in new[] { TransactionMode.None, TransactionMode.Explicit })
            {
                DataBatch batch = Marked(BatchPurpose.Persist, premises);
                batch.TransactionMode = mode;
                _ = batch.Sql($"DELETE FROM [core].[Jobs] WHERE 1 = 0;", new SqlOptions(writes: Set(new TableName("core", "Jobs"))));
                BatchAssembly assembly = await BatchHarness.SealAsync(batch);

                Assert.NotNull(assembly.Checks);
                Assert.DoesNotContain(BatchAssembly.TransactionOpen, assembly.Text, StringComparison.Ordinal);
                Assert.Contains(BatchAssembly.GuardOpen, assembly.Text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task A_transaction_head_statement_is_refused_by_a_batch_whose_text_opens_no_transaction()
        {
            foreach ((TransactionMode mode, bool writes) in new[] { (TransactionMode.None, true), (TransactionMode.Explicit, true), (TransactionMode.Auto, false) })
            {
                DataBatch batch = _harness.Batch(BatchPurpose.Persist);
                batch.TransactionMode = mode;
                batch.Append(batch.Begin(BatchSection.TransactionHead), Lock);
                _ = batch.Sql($"DELETE FROM [core].[Jobs] WHERE 1 = 0;", writes ? new SqlOptions(writes: Set(new TableName("core", "Jobs"))) : null);

                _ = await Assert.ThrowsAsync<InvalidOperationException>(() => batch.ExecuteAsync(Token));
            }
        }

        [Fact]
        public void The_guards_table_counts_toward_the_parameter_budget()
        {
            // The fingerprint, the guard's table and 1998 holes make 2000; one hole more is refused at assembly.
            foreach ((int holes, bool fits) in new[] { (1998, true), (1999, false) })
            {
                DataBatch batch = Marked(BatchPurpose.Persist, GuardPremises.ForUser(Settings, Permissions));
                string format = "SELECT " + string.Join(", ", Enumerable.Range(0, holes).Select(static index => "{" + index.ToString(CultureInfo.InvariantCulture) + "}")) + ";";
                _ = batch.Sql(FormattableStringFactory.Create(format, [.. Enumerable.Range(0, holes).Cast<object?>()]), new SqlOptions(writes: Set(Widgets)));

                if (fits)
                {
                    Assert.Equal(BatchAssembly.MaxParameters, batch.Assemble().Parameters.Count);
                }
                else
                {
                    _ = Assert.Throws<InvalidOperationException>(batch.Assemble);
                }
            }
        }

        [Fact]
        public async Task An_assembly_may_run_twice_only_when_every_statement_reads_or_is_idempotent()
        {
            DataBatch reads = _harness.Batch(BatchPurpose.Read);
            _ = reads.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
            Assert.True((await BatchHarness.SealAsync(reads)).MayRunTwice);

            DataBatch idempotent = _harness.Batch(BatchPurpose.Persist);
            _ = idempotent.Sql($"DELETE FROM [fixture].[Widgets] WHERE 1 = 0;", new SqlOptions(writes: Set(Widgets), idempotent: true));
            Assert.True((await BatchHarness.SealAsync(idempotent)).MayRunTwice);

            DataBatch writes = _harness.Batch(BatchPurpose.Persist);
            _ = writes.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));
            _ = writes.Sql($"DELETE FROM [fixture].[Widgets] WHERE 1 = 0;", new SqlOptions(writes: Set(Widgets)));
            Assert.False((await BatchHarness.SealAsync(writes)).MayRunTwice);
        }

        [Fact]
        public void The_first_declared_inserted_roots_are_probed_and_the_tag_parameter_binds_the_batchs_tag()
        {
            DataBatch batch = _harness.Batch(BatchPurpose.Persist);
            batch.DeclareInsertedRoots(Widgets, Array.Empty<int>());
            Assert.Null(batch.Probe);

            batch.DeclareInsertedRoots(Widgets, [3, 4]);
            batch.DeclareInsertedRoots(Profiles, [5L]);
            Assert.Equal((Widgets, 2), (batch.Probe!.Table, batch.Probe.Count));

            ScalarParameter tag = Assert.IsType<ScalarParameter>(batch.TagParameter());
            Assert.Equal((ReservedSqlNames.Tag, SqlDbType.UniqueIdentifier, (object?)batch.Tag), (tag.Name, tag.Type, tag.Value));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A version tag from a small number.</summary>
        /// <param name="value">The number.</param>
        /// <returns>The tag.</returns>
        private static VersionTag Tag(int value)
        {
            return new VersionTag(new Guid(value, 0, 0, new byte[8]));
        }

        /// <summary>A dependency that re-runs on a mismatch.</summary>
        /// <param name="name">The tag's name.</param>
        /// <param name="tag">The tag expected.</param>
        /// <returns>The dependency.</returns>
        private static VersionTagDependency Dependency(string name, VersionTag tag)
        {
            return new VersionTagDependency(name, tag, VersionTagMismatchPolicy.Rerun);
        }

        /// <summary>A set of tables.</summary>
        /// <param name="tables">The tables.</param>
        /// <returns>The set.</returns>
        private static HashSet<TableName> Set(params TableName[] tables)
        {
            return [.. tables];
        }

        /// <summary>A batch marked with premises whose prologue stand-in declares the guard variable.</summary>
        /// <param name="purpose">The purpose.</param>
        /// <param name="premises">The premises.</param>
        /// <param name="contributors">More contributors.</param>
        /// <returns>The batch.</returns>
        private DataBatch Marked(BatchPurpose purpose, GuardPremises premises, params IDataBatchContributor[] contributors)
        {
            DataBatch batch = _harness.Batch(purpose, [new TextContributor(0, Prologue, null), .. contributors]);
            batch.MarkConnectPrologue(premises);
            return batch;
        }

        /// <summary>
        ///     A registry over a fixed map of tables to tenant tags, and fixed user rules; its names are
        ///     the platform's three, the map's, and the tags the tests depend on that no table bumps.
        /// </summary>
        /// <param name="tenant">Each table's tenant-level tags.</param>
        /// <param name="rules">The user-level rules.</param>
        private sealed class MapRegistry(Dictionary<TableName, string[]> tenant, params UserVersionTagRule[] rules) : IVersionTagRegistry
        {
            /// <inheritdoc />
            public IReadOnlyList<string> Names =>
            [
                .. new[] { VersionTagNames.Settings, VersionTagNames.Permissions, VersionTagNames.Entities, VersionTagNames.ForEntity("Agent"), "units" }
                    .Concat(tenant.Values.SelectMany(static names => names))
                    .Distinct(StringComparer.Ordinal),
            ];

            /// <inheritdoc />
            public VersionTagEffects Resolve(IReadOnlySet<TableName> writtenTables)
            {
                return new VersionTagEffects(
                    [.. writtenTables.SelectMany(table => tenant.GetValueOrDefault(table, [])).Distinct(StringComparer.Ordinal)],
                    [.. rules.Where(rule => writtenTables.Contains(rule.Table))]);
            }
        }
    }
}
