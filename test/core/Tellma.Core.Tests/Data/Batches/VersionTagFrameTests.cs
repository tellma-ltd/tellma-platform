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
                    "UPDATE [core].[VersionTags] SET [Tag] = @tm_tag WHERE [Name] IN (SELECT [Id] FROM @tm_tagNames);",
                    "IF @@ROWCOUNT <> (SELECT COUNT(*) FROM @tm_tagNames) THROW 50422, N'VersionTag.Missing', 1;",
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

            // The caller's own rows name the caller.
            _ = batch.Sql($"DELETE FROM [vt].[Profiles] WHERE 1 = 0;", SqlOptions.ForCaller(new HashSet<TableName> { Profiles }));
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
        public async Task The_bump_plan_is_fixed_when_the_epilogue_starts_so_an_epilogues_own_writes_bump_nothing()
        {
            DataBatch batch = Batch(BatchPurpose.Persist, new DelegateContributor(60, static (batch, stage) =>
            {
                if (stage == DataBatchStage.Epilogue)
                {
                    _ = batch.Sql($"DELETE FROM [vt].[Currencies] WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { Currencies }));
                }
            }));
            _ = batch.Sql($"SELECT 1 AS [One];", new SqlOptions(resultSets: 1));

            BatchAssembly assembly = await BatchHarness.SealAsync(batch);

            Assert.DoesNotContain("UPDATE [core].[VersionTags]", assembly.Text, StringComparison.Ordinal);
            Assert.True(batch.Bumps.IsEmpty);
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
