// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Data;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Caching;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tests.Caching;
using Tellma.Core.Tests.Data.Metadata;

namespace Tellma.Core.Tests.Data.Batches
{
    /// <summary>
    ///     The version tags in a batch's frame with no database: the prelude heading every batch that
    ///     carries no connect prologue, the bump closing every batch whose writes or explicit bumps
    ///     resolve to a tag, and the refusals of an unregistered bump and of a write whose users go
    ///     unnamed.
    /// </summary>
    public sealed class VersionTagFrameTests : IDisposable
    {
        private static readonly TableName Currencies = new("vt", "Currencies");

        private static readonly TableName Profiles = new("vt", "Profiles");

        /// <summary>The harness, whose registry is the sound version-tag entities'.</summary>
        private readonly BatchHarness _harness = new(registry: new VersionTagRegistry(MetadataModels.Build(
            typeof(VtCurrency), typeof(VtUnit), typeof(VtList), typeof(VtProfile), typeof(VtGrant)).Provider!));

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        [Fact]
        public async Task The_prelude_follows_the_schema_guard_and_the_bump_is_the_last_statement_before_the_commit()
        {
            // A later contributor's prologue follows the prelude; its epilogue precedes the bump.
            DataBatch batch = Batch(BatchPurpose.Persist, new TextContributor(60, "PRINT 'prologue';", "PRINT 'epilogue';"));
            _ = batch.Sql($"DELETE FROM [vt].[Currencies] WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { Currencies }));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            Assert.Equal(
                string.Join(
                    "\n",
                    BatchAssembly.SessionSettings,
                    BatchAssembly.SchemaGuardText,
                    "SELECT [Name], [Tag] FROM [core].[VersionTags];",
                    "PRINT 'prologue';",
                    BatchAssembly.TransactionOpen,
                    "DELETE FROM [vt].[Currencies] WHERE 1 = 0;",
                    "PRINT 'epilogue';",
                    "DECLARE @tm_bumpName nvarchar(128) = (SELECT MIN([Id] COLLATE Latin1_General_100_BIN2) FROM @tm_tagNames), @tm_bumpCount int = 0;",
                    "WHILE @tm_bumpName IS NOT NULL",
                    "BEGIN",
                    "    UPDATE [core].[VersionTags] SET [Tag] = @tm_tag WHERE [Name] = @tm_bumpName;",
                    "    SET @tm_bumpCount += @@ROWCOUNT;",
                    "    SET @tm_bumpName = (SELECT MIN([Id] COLLATE Latin1_General_100_BIN2) FROM @tm_tagNames WHERE [Id] COLLATE Latin1_General_100_BIN2 > @tm_bumpName);",
                    "END;",
                    "IF @tm_bumpCount <> (SELECT COUNT(*) FROM @tm_tagNames) THROW 50422, N'VersionTag.Missing', 1;",
                    BatchAssembly.TransactionCommit,
                    string.Empty),
                assembly.Text);

            // --- The bump binds the batch's one tag and the names it moves; it may run twice ---
            ScalarParameter tag = Assert.IsType<ScalarParameter>(Assert.Single(assembly.Parameters, static parameter => parameter.Name == ReservedSqlNames.Tag));
            Assert.Equal((SqlDbType.UniqueIdentifier, (object?)batch.Tag), (tag.Type, tag.Value));
            TableParameter names = Assert.IsType<TableParameter>(Assert.Single(assembly.Parameters, static parameter => parameter.Name == ReservedSqlNames.TagNames));
            Assert.Equal(1, names.Rows);
            Assert.True(assembly.Statements[^1].MayRunTwice);
        }

        [Fact]
        public async Task A_batch_that_bumps_nothing_carries_the_prelude_and_no_bump()
        {
            DataBatch batch = Batch(BatchPurpose.Read);
            _ = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            Assert.StartsWith(
                string.Join("\n", BatchAssembly.SessionSettings, BatchAssembly.SchemaGuardText, "SELECT [Name], [Tag] FROM [core].[VersionTags];", "SELECT 1 AS [One];"),
                assembly.Text,
                StringComparison.Ordinal);
            Assert.DoesNotContain("UPDATE [core].[VersionTags]", assembly.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_batch_marked_with_the_connect_prologue_reads_the_tags_there_so_the_prelude_yields()
        {
            DataBatch batch = Batch(BatchPurpose.Persist, new TextContributor(0, "SELECT [Name], [Tag] FROM [core].[VersionTags]; DECLARE @tm_Guard bit = 1;", null));
            batch.MarkConnectPrologue(GuardPremises.ForSystem(VersionTag.None));
            batch.DependsOn([new VersionTagDependency("currencies", VersionTag.None, VersionTagMismatchPolicy.Rerun)]);
            _ = batch.Sql($"DELETE FROM [vt].[Currencies] WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { Currencies }));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            Assert.Equal(2, assembly.Text.Split("SELECT [Name], [Tag] FROM [core].[VersionTags];").Length);

            // The guard re-checks the name the batch read and bumps under an update lock, and the bump closes the body.
            Assert.Contains("WITH (UPDLOCK, ROWLOCK, FORCESEEK)", assembly.Checks!.Text, StringComparison.Ordinal);
            Assert.Contains(("currencies", true), assembly.Checks.ExpectedTags.Select(static row => (row.Name, row.Bumped)));
            Assert.EndsWith($"N'VersionTag.Missing', 1;\n{BatchAssembly.TransactionCommit}\n{BatchAssembly.GuardClose}\n", assembly.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_write_with_a_user_level_rule_bumps_the_users_its_statement_names()
        {
            DataBatch batch = Batch(BatchPurpose.Persist);
            SqlIdentifier users = batch.DeclareIdTable();
            _ = batch.Sql($"DELETE FROM [vt].[Profiles] OUTPUT deleted.[UserId] INTO {users} WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { Profiles }, userIds: users));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            Assert.Contains($"INSERT INTO @tm_userIds_Preferences ([Id]) SELECT DISTINCT s.[Id] FROM {users.Name} AS s", assembly.Text, StringComparison.Ordinal);
            Assert.Contains("UPDATE u SET u.[PreferencesTag] = @tm_tag FROM [core].[UserStamps] AS u", assembly.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(assembly.Parameters, static parameter => parameter.Name == ReservedSqlNames.TagNames);
        }

        [Fact]
        public void A_write_with_a_user_level_rule_that_names_no_users_is_refused_when_appended()
        {
            DataBatch batch = Batch(BatchPurpose.Persist);

            ArgumentException refused = Assert.Throws<ArgumentException>(
                () => batch.Sql($"DELETE FROM [vt].[Profiles] WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { Profiles })));

            Assert.Contains("vt.Profiles, whose writes bump the Preferences tag of each user they name", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_callers_own_rows_name_the_caller_only_in_a_batch_that_carries_a_users_connect_prologue()
        {
            // --- No connect prologue, or the platform's: nothing declares the caller ---
            foreach (GuardPremises? premises in new[] { null, GuardPremises.ForSystem(VersionTag.None) })
            {
                DataBatch batch = Batch(BatchPurpose.Persist);
                if (premises is not null)
                {
                    batch.MarkConnectPrologue(premises);
                }

                InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                    () => batch.Sql($"DELETE FROM [vt].[Profiles] WHERE 1 = 0;", SqlOptions.ForCaller(new HashSet<TableName> { Profiles })));
                Assert.Contains("carries no user's connect prologue", refused.Message, StringComparison.Ordinal);
            }

            // --- A user's connect prologue declares the caller ---
            DataBatch marked = Batch(BatchPurpose.Persist);
            marked.MarkConnectPrologue(GuardPremises.ForUser(VersionTag.None, VersionTag.None));
            _ = marked.Sql($"DELETE FROM [vt].[Profiles] WHERE 1 = 0;", SqlOptions.ForCaller(new HashSet<TableName> { Profiles }));
        }

        [Fact]
        public async Task An_autocommit_batch_may_write_only_tables_that_bump_no_tag()
        {
            // --- A bumping write needs a transaction the bump commits with ---
            DataBatch bumping = Batch(BatchPurpose.Persist);
            bumping.TransactionMode = TransactionMode.None;
            _ = bumping.Sql($"DELETE FROM [vt].[Currencies] WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { Currencies }));

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => bumping.ExecuteAsync(TestContext.Current.CancellationToken));
            Assert.Contains("commits each statement on its own, but its writes bump version tags (currencies)", refused.Message, StringComparison.Ordinal);

            // --- A write that bumps nothing runs ---
            DataBatch plain = Batch(BatchPurpose.Persist);
            plain.TransactionMode = TransactionMode.None;
            _ = plain.Sql($"DELETE FROM [vt].[Elsewhere] WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { new("vt", "Elsewhere") }));
            Assert.DoesNotContain("[core].[VersionTags] SET", (await BatchHarness.SealAsync(plain)).Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Only_a_registered_name_may_be_bumped_explicitly()
        {
            DataBatch batch = Batch(BatchPurpose.Persist);

            ArgumentException refused = Assert.Throws<ArgumentException>(() => batch.BumpVersionTag("unheard-of"));

            Assert.Contains("The version tag 'unheard-of' is not registered", refused.Message, StringComparison.Ordinal);
            batch.BumpVersionTag("units");
            batch.BumpVersionTag(VersionTagNames.Settings);
        }

        [Fact]
        public async Task An_epilogue_write_that_the_fixed_bump_plan_does_not_cover_is_refused()
        {
            // --- An epilogue write whose tag the caller's writes do not bump would commit unannounced ---
            DataBatch uncovered = Batch(BatchPurpose.Persist, EpilogueWrite(Currencies));
            _ = uncovered.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => uncovered.ExecuteAsync(TestContext.Current.CancellationToken));
            Assert.Contains("An epilogue statement writes a table whose version tags the batch does not bump (currencies, where the caller's writes bump nothing)", refused.Message, StringComparison.Ordinal);
            Assert.True(uncovered.Bumps.IsEmpty);

            // --- One whose tags the plan already moves is covered by the bump ---
            DataBatch covered = Batch(BatchPurpose.Persist, EpilogueWrite(Currencies));
            _ = covered.Sql($"DELETE FROM [vt].[Currencies] WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { Currencies }));
            BatchAssembly assembly = await BatchHarness.SealAsync(covered);
            Assert.Equal(["currencies"], covered.Bumps.TenantNames);
            Assert.Contains("UPDATE [core].[VersionTags] SET [Tag] = @tm_tag", assembly.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_prologue_statements_writes_bump_nothing()
        {
            // The connect prologue's bookkeeping (the activity stamp, the join flip) is no cached input.
            DataBatch batch = Batch(BatchPurpose.Persist, new DelegateContributor(10, static (batch, stage) =>
            {
                if (stage == DataBatchStage.Prologue)
                {
                    var data = (DataBatch)batch;
                    data.Append(data.Begin(), "DELETE FROM [vt].[Currencies] WHERE 1 = 0;", new HashSet<TableName> { Currencies });
                }
            }));
            _ = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            Assert.True(batch.Bumps.IsEmpty);
            Assert.Contains(Currencies, batch.WrittenTables);
            Assert.DoesNotContain("[core].[VersionTags] SET", assembly.Text, StringComparison.Ordinal);
        }

        /// <summary>A contributor whose epilogue writes a table.</summary>
        /// <param name="table">The table.</param>
        /// <returns>The contributor, at an order whose epilogue runs before the bump's.</returns>
        private static DelegateContributor EpilogueWrite(TableName table)
        {
            return new DelegateContributor(60, (batch, stage) =>
            {
                if (stage == DataBatchStage.Epilogue)
                {
                    _ = batch.Sql($"DELETE FROM [vt].[Currencies] WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { table }));
                }
            });
        }

        /// <summary>A batch whose scope holds the prelude, the bump and the given contributors.</summary>
        /// <param name="purpose">The purpose.</param>
        /// <param name="contributors">More contributors.</param>
        /// <returns>The batch.</returns>
        private DataBatch Batch(BatchPurpose purpose, params IDataBatchContributor[] contributors)
        {
            IServiceProvider services = _harness.Services;
            return _harness.Batch(
                purpose,
                [
                    new VersionTagPreludeContributor(TimeProvider.System),
                    new VersionTagBumpContributor(new NoPublisher(), services.GetRequiredService<VersionTagMetrics>()),
                    .. contributors,
                ]);
        }

        /// <summary>A publisher no batch here reaches, since none commits.</summary>
        private sealed class NoPublisher : IClientEventPublisher
        {
            /// <inheritdoc />
            public void Publish(IDataBatch? batch, ClientEvent clientEvent)
            {
                throw new InvalidOperationException("No batch of this suite commits.");
            }

            /// <inheritdoc />
            public Task PublishAsync(ClientEvent clientEvent)
            {
                throw new InvalidOperationException("No batch of this suite commits.");
            }
        }
    }
}
