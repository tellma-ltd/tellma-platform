// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Caching;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tests.Data.Metadata;

namespace Tellma.Core.Tests.Caching
{
    /// <summary>
    ///     What a batch bumps and how: the plan its writes and explicit bumps resolve to, the bump
    ///     epilogue's text pinned, its refusal of a rule no source covers, and the <c>cache.changed</c>
    ///     events each move announces.
    /// </summary>
    public sealed class VersionTagBumpTests
    {
        private static readonly TableName Currencies = new("vt", "Currencies");

        private static readonly TableName Lists = new("vt", "Lists");

        private static readonly TableName Profiles = new("vt", "Profiles");

        private static readonly TableName Grants = new("vt", "Grants");

        private static readonly TableName UserStamps = new("core", "UserStamps");

        /// <summary>The registry over the sound entities.</summary>
        private static readonly VersionTagRegistry Registry = new(MetadataModels.Build(
            typeof(VtCurrency), typeof(VtUnit), typeof(VtList), typeof(VtProfile), typeof(VtGrant)).Provider!);

        // ===================== The plan =====================

        [Fact]
        public void A_batch_that_writes_and_bumps_nothing_has_the_empty_plan()
        {
            Assert.Same(VersionTagBumpPlan.None, VersionTagBumpPlan.Resolve(Registry, new HashSet<TableName>(), new HashSet<string>()));
            Assert.Same(VersionTagBumpPlan.None, VersionTagBumpPlan.Resolve(null, new HashSet<TableName> { Currencies }, new HashSet<string>()));
            Assert.True(VersionTagBumpPlan.None.IsEmpty);
            Assert.False(VersionTagBumpPlan.None.WritesUserStamps);
        }

        [Fact]
        public void The_plan_joins_the_written_tables_names_and_the_explicit_bumps_once_each_in_ordinal_order()
        {
            var plan = VersionTagBumpPlan.Resolve(Registry, new HashSet<TableName> { Currencies }, new HashSet<string> { "units", "currencies", VersionTagNames.Settings });

            Assert.Equal(["currencies", VersionTagNames.Settings, "units"], plan.TenantNames);
            Assert.Empty(plan.UserRules);
            Assert.False(plan.WritesUserStamps);
        }

        [Fact]
        public void Any_entity_tag_brings_the_entities_tag_whether_written_or_bumped_explicitly()
        {
            Assert.Equal(
                [VersionTagNames.Entities, VersionTagNames.ForEntity("vt.VtList")],
                VersionTagBumpPlan.Resolve(Registry, new HashSet<TableName> { Lists }, new HashSet<string>()).TenantNames);
            Assert.Equal(
                [VersionTagNames.Entities, VersionTagNames.ForEntity("vt.VtList")],
                VersionTagBumpPlan.Resolve(Registry, new HashSet<TableName>(), new HashSet<string> { VersionTagNames.ForEntity("vt.VtList") }).TenantNames);
        }

        [Fact]
        public void A_batch_writes_the_stamps_when_it_declares_them_or_when_a_user_level_rule_fires()
        {
            var stamps = VersionTagBumpPlan.Resolve(Registry, new HashSet<TableName> { UserStamps }, new HashSet<string>());
            Assert.True(stamps.WritesUserStamps);
            Assert.True(stamps.IsEmpty);

            var profiles = VersionTagBumpPlan.Resolve(Registry, new HashSet<TableName> { Profiles, Grants }, new HashSet<string>());
            Assert.True(profiles.WritesUserStamps);
            Assert.False(profiles.IsEmpty);
            Assert.Equal([UserVersionTagNames.Permissions, UserVersionTagNames.Preferences], profiles.UserColumns);
        }

        // ===================== The epilogue's text =====================

        [Fact]
        public void A_tenant_level_bump_moves_each_name_in_the_guards_order_and_must_find_a_row_for_every_name()
        {
            var plan = VersionTagBumpPlan.Resolve(Registry, new HashSet<TableName> { Currencies }, new HashSet<string>());

            (string text, IReadOnlySet<TableName> writes) = VersionTagBumpContributor.Compose(plan, []);

            Assert.Equal(
                string.Join(
                    "\n",
                    "DECLARE @tm_bumpOrder TABLE ([Name] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY);",
                    "INSERT INTO @tm_bumpOrder ([Name]) SELECT [Id] FROM @tm_tagNames;",
                    "DECLARE @tm_bumpName nvarchar(128) = (SELECT MIN([Name]) FROM @tm_bumpOrder), @tm_bumpCount int = 0;",
                    "WHILE @tm_bumpName IS NOT NULL",
                    "BEGIN",
                    "    UPDATE [core].[VersionTags] SET [Tag] = @tm_tag WHERE [Name] = @tm_bumpName;",
                    "    SET @tm_bumpCount += @@ROWCOUNT;",
                    "    SET @tm_bumpName = (SELECT TOP (1) [Name] FROM @tm_bumpOrder WHERE [Name] > @tm_bumpName ORDER BY [Name]);",
                    "END;",
                    "IF @tm_bumpCount <> (SELECT COUNT(*) FROM @tm_bumpOrder) THROW 50422, N'VersionTag.Missing', 1;"),
                text);
            Assert.Equal([new TableName("core", "VersionTags")], writes);
        }

        [Fact]
        public void A_user_level_bump_gathers_each_columns_users_from_every_covering_source_then_moves_and_returns_them()
        {
            var plan = VersionTagBumpPlan.Resolve(Registry, new HashSet<TableName> { Currencies, Profiles, Grants }, new HashSet<string>());
            UserIdSource profileIds = new(new HashSet<TableName> { Profiles }, null, new SqlIdentifier("@tb2_ids"));
            UserIdSource grantRows = new(new HashSet<TableName> { Grants }, "GranteeId", new SqlIdentifier("@tb3_saved"));
            UserIdSource bothIds = new(new HashSet<TableName> { Profiles, Grants }, null, new SqlIdentifier("@tb4_ids"));
            UserIdSource otherColumn = new(new HashSet<TableName> { Grants }, "GrantorId", new SqlIdentifier("@tb5_saved"));

            (string text, IReadOnlySet<TableName> writes) = VersionTagBumpContributor.Compose(plan, [profileIds, grantRows, bothIds, otherColumn]);

            // The source whose rows hold another column covers no rule; one naming both tables fills both columns.
            Assert.Equal(
                string.Join(
                    "\n",
                    "DECLARE @tm_bumpOrder TABLE ([Name] nvarchar(128) COLLATE Latin1_General_100_BIN2 NOT NULL PRIMARY KEY);",
                    "INSERT INTO @tm_bumpOrder ([Name]) SELECT [Id] FROM @tm_tagNames;",
                    "DECLARE @tm_bumpName nvarchar(128) = (SELECT MIN([Name]) FROM @tm_bumpOrder), @tm_bumpCount int = 0;",
                    "WHILE @tm_bumpName IS NOT NULL",
                    "BEGIN",
                    "    UPDATE [core].[VersionTags] SET [Tag] = @tm_tag WHERE [Name] = @tm_bumpName;",
                    "    SET @tm_bumpCount += @@ROWCOUNT;",
                    "    SET @tm_bumpName = (SELECT TOP (1) [Name] FROM @tm_bumpOrder WHERE [Name] > @tm_bumpName ORDER BY [Name]);",
                    "END;",
                    "IF @tm_bumpCount <> (SELECT COUNT(*) FROM @tm_bumpOrder) THROW 50422, N'VersionTag.Missing', 1;",
                    "DECLARE @tm_userIds_Permissions TABLE ([Id] int NOT NULL PRIMARY KEY);",
                    "DECLARE @tm_userIds_Preferences TABLE ([Id] int NOT NULL PRIMARY KEY);",
                    "INSERT INTO @tm_userIds_Permissions ([Id]) SELECT DISTINCT s.[GranteeId] FROM @tb3_saved AS s WHERE s.[GranteeId] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM @tm_userIds_Permissions AS i WHERE i.[Id] = s.[GranteeId]);",
                    "INSERT INTO @tm_userIds_Permissions ([Id]) SELECT DISTINCT s.[Id] FROM @tb4_ids AS s WHERE s.[Id] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM @tm_userIds_Permissions AS i WHERE i.[Id] = s.[Id]);",
                    "INSERT INTO @tm_userIds_Preferences ([Id]) SELECT DISTINCT s.[Id] FROM @tb2_ids AS s WHERE s.[Id] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM @tm_userIds_Preferences AS i WHERE i.[Id] = s.[Id]);",
                    "INSERT INTO @tm_userIds_Preferences ([Id]) SELECT DISTINCT s.[Id] FROM @tb4_ids AS s WHERE s.[Id] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM @tm_userIds_Preferences AS i WHERE i.[Id] = s.[Id]);",
                    "UPDATE u SET u.[PermissionsTag] = @tm_tag FROM [core].[UserStamps] AS u JOIN @tm_userIds_Permissions AS i ON i.[Id] = u.[UserId];",
                    "UPDATE u SET u.[PreferencesTag] = @tm_tag FROM [core].[UserStamps] AS u JOIN @tm_userIds_Preferences AS i ON i.[Id] = u.[UserId];",
                    "SELECT N'Permissions' AS [Column], [Id] FROM @tm_userIds_Permissions UNION ALL SELECT N'Preferences', [Id] FROM @tm_userIds_Preferences;"),
                text);
            Assert.Equal([UserStamps, new TableName("core", "VersionTags")], writes.Order(Comparer<TableName>.Create(static (x, y) => string.CompareOrdinal(x.ToString(), y.ToString()))));
        }

        [Fact]
        public void A_user_level_bump_alone_moves_no_tenant_tag()
        {
            var plan = VersionTagBumpPlan.Resolve(Registry, new HashSet<TableName> { Profiles }, new HashSet<string>());

            (string text, IReadOnlySet<TableName> writes) = VersionTagBumpContributor.Compose(plan, [new(new HashSet<TableName> { Profiles }, null, new SqlIdentifier("@tb1_ids"))]);

            Assert.StartsWith("DECLARE @tm_userIds_Preferences TABLE", text, StringComparison.Ordinal);
            Assert.DoesNotContain("VersionTags", text, StringComparison.Ordinal);
            Assert.EndsWith("SELECT N'Preferences' AS [Column], [Id] FROM @tm_userIds_Preferences;", text, StringComparison.Ordinal);
            Assert.Equal([UserStamps], writes);
        }

        [Fact]
        public void A_rule_no_source_names_the_users_of_is_refused()
        {
            var plan = VersionTagBumpPlan.Resolve(Registry, new HashSet<TableName> { Profiles, Grants }, new HashSet<string>());

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
                () => VersionTagBumpContributor.Compose(plan, [new(new HashSet<TableName> { Profiles }, null, new SqlIdentifier("@tb1_ids"))]));

            Assert.Contains("The batch writes vt.Grants, whose writes bump the Permissions tag", refused.Message, StringComparison.Ordinal);
        }

        // ===================== The announcements =====================

        [Fact]
        public void Each_move_is_announced_to_the_clients_it_concerns_with_the_wire_tag_they_key_by()
        {
            VersionTag tag = new(new Guid("0000000a-0000-0000-0000-000000000000"));
            VersionTag tenantPermissions = new(new Guid("0000000b-0000-0000-0000-000000000000"));

            List<ClientEvent> events =
            [
                .. VersionTagBumpContributor.Events(
                    ["currencies", VersionTagNames.Entities, VersionTagNames.ForEntity("vt.VtList"), VersionTagNames.Permissions, VersionTagNames.Settings],
                    [new AffectedUser(UserVersionTagNames.Permissions, 7), new AffectedUser(UserVersionTagNames.Preferences, 8)],
                    tag,
                    tenantPermissions),
            ];

            // A pack's tag and the entities composite reach no one; the tenant's permissions carry no value.
            Assert.All(events, static clientEvent => Assert.Equal(VersionTagBumpContributor.CacheChangedEvent, clientEvent.Name));
            Assert.Equal(
                [
                    ("", new CacheChangedPayload("entity:vt.VtList", "1.0000000a000000000000000000000000")),
                    ("", new CacheChangedPayload("permissions", null)),
                    ("", new CacheChangedPayload("settings", "1.0000000a000000000000000000000000")),
                    ("7", new CacheChangedPayload("permissions", "1.0000000b0000000000000000000000000000000a000000000000000000000000")),
                    ("8", new CacheChangedPayload("preferences", "1.0000000a000000000000000000000000")),
                ],
                events.Select(static clientEvent => (string.Join(",", clientEvent.UserIds), (CacheChangedPayload)clientEvent.Payload!)));
        }
    }
}
