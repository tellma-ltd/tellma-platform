// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Caching;
using Tellma.Core.Data.Batches;
using Tellma.Core.Settings;

namespace Tellma.Core.Access
{
    // The texts are spec 0014 §7.3, one per premises case; the parameters are §7.2's. Each text is
    // built once, so every batch of a variant carries it byte for byte and binds the same parameters
    // with the same SQL types: a batch's plan differs from another's only by its body.
    /// <summary>
    ///     The connect prologue: the three texts, the parameters each binds, and the reader of its
    ///     result sets. Set 0 is the connect row, set 1 every tenant-level tag, set 2 the caller's
    ///     permission rows when the cached set is stale or a reload was asked, set 3 the caller's
    ///     profile when the cached one is stale, then the two sets of the absorbed settings load when
    ///     the settings are stale or missing.
    /// </summary>
    internal static class ConnectPrologue
    {
        /// <summary>The users' table.</summary>
        private static readonly TableName Users = PersistChecks.Users;

        /// <summary>The tables the subject variant's statement may write: the activity stamp and the join flip.</summary>
        private static readonly FrozenSet<TableName> SubjectWrites = new[] { UserStampConfiguration.Table, Users }.ToFrozenSet();

        /// <summary>The variables every user variant declares, apart from the user id.</summary>
        private const string UserVariables = """
                    @tm_Kind varchar(10), @tm_IsActive bit, @tm_State varchar(8),
                    @tm_PreferencesTag uniqueidentifier, @tm_UserPermissionsTag uniqueidentifier,
                    @tm_PermissionsTag uniqueidentifier, @tm_SettingsTag uniqueidentifier,
                    @tm_PermissionsStale bit = 0, @tm_ProfileStale bit = 0, @tm_SettingsStale bit = 0, @tm_Guard bit = 0,
                    @tm_Now datetimeoffset(3) = SYSUTCDATETIME();
            """;

        /// <summary>The ForSubject text: resolves the caller by subject, and may stamp and flip.</summary>
        public static readonly string SubjectText = UserText(bySubject: true);

        /// <summary>The ForUser text: resolves a user by the bound id, and never stamps or flips.</summary>
        public static readonly string UserIdText = UserText(bySubject: false);

        /// <summary>The ForSystem text: no user lookup, and a guard that only a strict stale <c>settings</c> fails.</summary>
        public static readonly string SystemText = string.Join(
            '\n',
            string.Create(
                CultureInfo.InvariantCulture,
                $"DECLARE {ReservedSqlNames.UserId} int = {WellKnownIds.SystemUserId}, @tm_SettingsTag uniqueidentifier, @tm_SettingsStale bit = 0, {ReservedSqlNames.Guard} bit = 0;"),
            $"SELECT @tm_SettingsTag = [Tag] FROM {VersionTagRowConfiguration.Table.Delimited} WHERE [Name] = N'{VersionTagNames.Settings}';",
            $"SET @tm_SettingsStale = CASE WHEN @tm_SettingsTag <> {ReservedSqlNames.ExpectedSettingsTag} THEN 1 ELSE 0 END;",
            $"SET {ReservedSqlNames.Guard} = CASE WHEN {ReservedSqlNames.SettingsStrict} = 0 OR @tm_SettingsStale = 0 THEN 1 ELSE 0 END;",
            $"SELECT {WellKnownIds.SystemUserId.ToString(CultureInfo.InvariantCulture)} AS [UserId], '{nameof(UserKind.System)}' AS [Kind], CAST(1 AS bit) AS [IsActive], CAST(NULL AS varchar(8)) AS [State],",
            "       CAST(NULL AS uniqueidentifier) AS [PreferencesTag], CAST(NULL AS uniqueidentifier) AS [UserPermissionsTag],",
            $"       CAST(0 AS bit) AS [PermissionsStale], CAST(0 AS bit) AS [ProfileStale], @tm_SettingsStale AS [SettingsStale], {ReservedSqlNames.Guard} AS [GuardPassed];",
            VersionTagPrelude.Text,
            SettingsLoad("@tm_SettingsStale"));

        /// <summary>
        ///     Marks a fresh batch as carrying the connect prologue for the premises, and appends the
        ///     prologue's statement with its parameters and its reader.
        /// </summary>
        /// <param name="batch">The batch, before its first statement.</param>
        /// <param name="premises">What the prologue assumes.</param>
        /// <param name="coldLoad">The settings cold load, which the prologue absorbs.</param>
        /// <param name="options">How the prologue's result is read and reported.</param>
        /// <returns>The prologue's result, complete once the batch executed.</returns>
        /// <exception cref="InvalidOperationException">The batch already has statements, or is a maintenance batch.</exception>
        public static BatchResult<ConnectResult> Append(
            DataBatch batch, ConnectPremises premises, SettingsColdLoadContributor coldLoad, ConnectReading options)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(premises);
            ArgumentNullException.ThrowIfNull(coldLoad);
            ArgumentNullException.ThrowIfNull(options);

            // --- The mark first: the prelude and the cold load yield to it, and a persist re-checks what it confirms ---
            batch.MarkConnectPrologue(Guard(premises));
            SettingsAbsorption settings = coldLoad.Absorb(batch);

            // --- The statement: the variant's text and its parameters, each always bound with the same type ---
            StatementDraft draft = batch.BeginConnectPrologue();
            int ordinal = draft.Ordinal;
            bool strict = batch.Purpose is BatchPurpose.Validate or BatchPurpose.Persist;
            (string text, bool reload) = Bind(draft, premises, strict);
            draft.Reserved(settings.Parameter);

            BatchResult<ConnectResult> result = new();
            StatementReader readSettings = settings.CreateReader(ordinal);
            batch.Append(
                draft,
                text,
                writes: premises is ConnectPremises.ForSubject ? SubjectWrites : null,
                idempotent: true,
                reader: (cursor, cancellationToken) => ReadAsync(batch, cursor, ordinal, reload, settings.Load, readSettings, result, options, cancellationToken));
            return result;
        }

        /// <summary>What the persist re-checks compare for the premises.</summary>
        /// <param name="premises">The premises.</param>
        /// <returns>The guard premises.</returns>
        private static GuardPremises Guard(ConnectPremises premises)
        {
            return premises switch
            {
                ConnectPremises.ForSubject subject => GuardPremises.ForUser(new VersionTag(subject.Expected.SettingsTag), Tag(subject.Expected.PermissionsTag)),
                ConnectPremises.ForUser user => GuardPremises.ForUser(new VersionTag(user.Expected.SettingsTag), Tag(user.Expected.PermissionsTag)),
                ConnectPremises.ForSystem system => GuardPremises.ForSystem(new VersionTag(system.ExpectedSettingsTag)),
                _ => throw new ArgumentException("The premises are not one of the three cases.", nameof(premises)),
            };
        }

        /// <summary>A cached tag, or none.</summary>
        /// <param name="tag">The tag, or null when not cached.</param>
        /// <returns>The version tag.</returns>
        private static VersionTag Tag(Guid? tag)
        {
            return tag is Guid value ? new VersionTag(value) : VersionTag.None;
        }

        /// <summary>Binds the variant's parameters and returns its text.</summary>
        /// <param name="draft">The prologue's statement.</param>
        /// <param name="premises">The premises.</param>
        /// <param name="strict">Whether a stale <c>settings</c> tag fails the guard: a validation or persist batch.</param>
        /// <returns>The text, and whether the permission rows are asked for even when the set's tags match.</returns>
        private static (string Text, bool Reload) Bind(StatementDraft draft, ConnectPremises premises, bool strict)
        {
            switch (premises)
            {
                case ConnectPremises.ForSubject subject:
                    draft.Reserved(new ScalarParameter(ReservedSqlNames.Subject, SqlDbType.VarChar, subject.Subject, size: 255));
                    draft.Reserved(Bit(ReservedSqlNames.StampActivity, subject.StampActivity));
                    draft.Reserved(Bit(ReservedSqlNames.AllowFlip, subject.AllowStateFlip));
                    BindExpectations(draft, subject.ExpectedUserId, subject.Expected, subject.ReloadPermissions, strict);
                    return (SubjectText, subject.ReloadPermissions);

                case ConnectPremises.ForUser user:
                    draft.Reserved(new ScalarParameter(ReservedSqlNames.UserId, SqlDbType.Int, user.UserId));
                    BindExpectations(draft, user.UserId, user.Expected, user.ReloadPermissions, strict);
                    return (UserIdText, user.ReloadPermissions);

                case ConnectPremises.ForSystem system:
                    draft.Reserved(Unique(ReservedSqlNames.ExpectedSettingsTag, system.ExpectedSettingsTag));
                    draft.Reserved(Bit(ReservedSqlNames.SettingsStrict, strict));
                    return (SystemText, false);

                default:
                    throw new ArgumentException("The premises are not one of the three cases.", nameof(premises));
            }
        }

        /// <summary>Binds what a user variant compares: the expected user and the cached tags.</summary>
        /// <param name="draft">The prologue's statement.</param>
        /// <param name="expectedUserId">The cached user id; null on the cold path.</param>
        /// <param name="expected">The cached tags.</param>
        /// <param name="reload">Whether the rows are asked for even when the set's tags match.</param>
        /// <param name="strict">Whether a stale <c>settings</c> tag fails the guard.</param>
        private static void BindExpectations(StatementDraft draft, int? expectedUserId, ConnectExpectations expected, bool reload, bool strict)
        {
            draft.Reserved(new ScalarParameter(ReservedSqlNames.ExpectedUserId, SqlDbType.Int, expectedUserId));
            draft.Reserved(Unique(ReservedSqlNames.ExpectedPermissionsTag, expected.PermissionsTag));
            draft.Reserved(Unique(ReservedSqlNames.ExpectedUserPermissionsTag, expected.UserPermissionsTag));
            draft.Reserved(Unique(ReservedSqlNames.ExpectedPreferencesTag, expected.PreferencesTag));
            draft.Reserved(Unique(ReservedSqlNames.ExpectedSettingsTag, expected.SettingsTag));
            draft.Reserved(Bit(ReservedSqlNames.ReloadPermissions, reload));
            draft.Reserved(Bit(ReservedSqlNames.SettingsStrict, strict));
        }

        /// <summary>A <c>bit</c> parameter.</summary>
        /// <param name="name">The name.</param>
        /// <param name="value">The value.</param>
        /// <returns>The parameter.</returns>
        private static ScalarParameter Bit(string name, bool value)
        {
            return new ScalarParameter(name, SqlDbType.Bit, value);
        }

        /// <summary>A <c>uniqueidentifier</c> parameter.</summary>
        /// <param name="name">The name.</param>
        /// <param name="value">The value, or null for SQL <c>NULL</c>.</param>
        /// <returns>The parameter.</returns>
        private static ScalarParameter Unique(string name, Guid? value)
        {
            return new ScalarParameter(name, SqlDbType.UniqueIdentifier, value);
        }

        /// <summary>The absorbed settings load, under its condition.</summary>
        /// <param name="stale">The variable that holds whether the settings are stale.</param>
        /// <returns>The text.</returns>
        private static string SettingsLoad(string stale)
        {
            return string.Join(
                '\n',
                $"IF {stale} = 1 OR {ReservedSqlNames.LoadSettings} = 1",
                "BEGIN",
                SettingsColdLoad.Text.TrimEnd(),
                "END;");
        }

        /// <summary>Builds a user variant's text.</summary>
        /// <param name="bySubject">Whether the caller is resolved by subject (ForSubject) rather than by the bound id (ForUser).</param>
        /// <returns>The text.</returns>
        private static string UserText(bool bySubject)
        {
            List<string> lines = [];

            // --- The variables, and the caller's id table that user-level bumps source from ---
            // The ForUser variant binds the user id as a parameter instead of declaring it.
            lines.Add(bySubject ? $"DECLARE {ReservedSqlNames.UserId} int," : "DECLARE");
            lines.Add(UserVariables.TrimEnd());
            lines.Add($"DECLARE {ReservedSqlNames.CallerIds} TABLE ([Id] int PRIMARY KEY);");

            // --- The resolve: one seek on the covering subject index (or the key), one on the stamps ---
            lines.Add($"SELECT {ReservedSqlNames.UserId} = U.[Id], @tm_Kind = U.[Kind], @tm_IsActive = U.[IsActive], @tm_State = U.[State],");
            lines.Add("       @tm_PreferencesTag = S.[PreferencesTag], @tm_UserPermissionsTag = S.[PermissionsTag]");
            lines.Add($"FROM {Users.Delimited} AS U");
            lines.Add($"JOIN {UserStampConfiguration.Table.Delimited} AS S ON S.[UserId] = U.[Id]");
            if (bySubject)
            {
                lines.Add($"WHERE U.[Subject] = {ReservedSqlNames.Subject};");
            }
            else
            {
                // A bound id that matches no row reads as no user, as an unknown subject does.
                lines.Add($"WHERE U.[Id] = {ReservedSqlNames.UserId};");
                lines.Add($"IF @tm_Kind IS NULL SET {ReservedSqlNames.UserId} = NULL;");
            }

            // --- The tags, read before any permission row: a racing bump yields an older tag than the data, never a newer ---
            lines.Add($"SELECT @tm_PermissionsTag = [Tag] FROM {VersionTagRowConfiguration.Table.Delimited} WHERE [Name] = N'{VersionTagNames.Permissions}';");
            lines.Add($"SELECT @tm_SettingsTag = [Tag] FROM {VersionTagRowConfiguration.Table.Delimited} WHERE [Name] = N'{VersionTagNames.Settings}';");

            // --- The active caller: the bookkeeping writes, then the stale flags and the guard ---
            lines.Add($"IF {ReservedSqlNames.UserId} IS NOT NULL AND @tm_IsActive = 1");
            lines.Add("BEGIN");
            lines.Add($"    INSERT INTO {ReservedSqlNames.CallerIds} ([Id]) VALUES ({ReservedSqlNames.UserId});");
            if (bySubject)
            {
                // Autocommit, idempotent and single-row: the throttled stamp matches no row on the
                // common request, and the first request's flip happens once per user, ever.
                lines.Add($"    IF {ReservedSqlNames.StampActivity} = 1");
                lines.Add($"        UPDATE {UserStampConfiguration.Table.Delimited} SET [LastActiveAt] = @tm_Now");
                lines.Add($"        WHERE [UserId] = {ReservedSqlNames.UserId} AND ([LastActiveAt] IS NULL OR [LastActiveAt] < DATEADD(second, -60, @tm_Now));");
                lines.Add($"    IF @tm_State = '{nameof(UserState.Invited)}' AND {ReservedSqlNames.AllowFlip} = 1");
                lines.Add("    BEGIN");
                lines.Add($"        UPDATE {Users.Delimited} SET [JoinedAt] = @tm_Now WHERE [Id] = {ReservedSqlNames.UserId} AND [JoinedAt] IS NULL;");
                lines.Add($"        SET @tm_State = '{nameof(UserState.Joined)}';");
                lines.Add("    END;");
            }

            // A user the premises did not expect is stale in every part: the cached set and profile
            // describe someone else, so the rows and the profile always come back for them.
            lines.Add($"    SET @tm_PermissionsStale = CASE WHEN {ReservedSqlNames.ExpectedUserId} IS NULL OR {ReservedSqlNames.UserId} <> {ReservedSqlNames.ExpectedUserId}");
            lines.Add($"                                      OR {ReservedSqlNames.ExpectedPermissionsTag} IS NULL OR {ReservedSqlNames.ExpectedPermissionsTag} <> @tm_PermissionsTag");
            lines.Add($"                                      OR {ReservedSqlNames.ExpectedUserPermissionsTag} IS NULL OR {ReservedSqlNames.ExpectedUserPermissionsTag} <> @tm_UserPermissionsTag THEN 1 ELSE 0 END;");
            lines.Add($"    SET @tm_ProfileStale = CASE WHEN {ReservedSqlNames.ExpectedUserId} IS NULL OR {ReservedSqlNames.UserId} <> {ReservedSqlNames.ExpectedUserId}");
            lines.Add($"                                OR {ReservedSqlNames.ExpectedPreferencesTag} IS NULL OR {ReservedSqlNames.ExpectedPreferencesTag} <> @tm_PreferencesTag THEN 1 ELSE 0 END;");
            lines.Add($"    SET @tm_SettingsStale = CASE WHEN @tm_SettingsTag <> {ReservedSqlNames.ExpectedSettingsTag} THEN 1 ELSE 0 END;");
            lines.Add($"    SET {ReservedSqlNames.Guard} = CASE WHEN {ReservedSqlNames.UserId} = {ReservedSqlNames.ExpectedUserId} AND @tm_PermissionsStale = 0");
            lines.Add($"                          AND ({ReservedSqlNames.SettingsStrict} = 0 OR @tm_SettingsStale = 0) THEN 1 ELSE 0 END;");
            lines.Add("END;");

            // --- Set 0, the connect row, and set 1, every tenant-level tag: always present ---
            lines.Add($"SELECT {ReservedSqlNames.UserId} AS [UserId], @tm_Kind AS [Kind], @tm_IsActive AS [IsActive], @tm_State AS [State],");
            lines.Add("       @tm_PreferencesTag AS [PreferencesTag], @tm_UserPermissionsTag AS [UserPermissionsTag],");
            lines.Add($"       @tm_PermissionsStale AS [PermissionsStale], @tm_ProfileStale AS [ProfileStale], @tm_SettingsStale AS [SettingsStale], {ReservedSqlNames.Guard} AS [GuardPassed];");
            lines.Add(VersionTagPrelude.Text);

            // --- Set 2, the permission rows: one per membership that has not ended, and one per active public role's permission ---
            lines.Add($"IF (@tm_PermissionsStale = 1 OR {ReservedSqlNames.ReloadPermissions} = 1) AND {ReservedSqlNames.UserId} IS NOT NULL AND @tm_IsActive = 1 AND @tm_Kind <> '{nameof(UserKind.System)}'");
            lines.Add("    SELECT P.[Id], P.[RoleId], R.[Name], R.[IsPublic], P.[Resource], P.[Action], P.[Filter], P.[FilterQueryexVersion], M.[StartsAt], M.[EndsAt]");
            lines.Add("    FROM [core].[Permissions] AS P JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]");
            lines.Add($"    LEFT JOIN [core].[RoleMemberships] AS M ON M.[RoleId] = R.[Id] AND M.[UserId] = {ReservedSqlNames.UserId}");
            lines.Add("                                           AND (M.[EndsAt] IS NULL OR M.[EndsAt] > @tm_Now)");
            lines.Add("    WHERE R.[IsActive] = 1 AND (R.[IsPublic] = 1 OR M.[Id] IS NOT NULL);");

            // --- Set 3, the profile ---
            lines.Add($"IF @tm_ProfileStale = 1 AND {ReservedSqlNames.UserId} IS NOT NULL AND @tm_IsActive = 1");
            lines.Add("    SELECT [Name], [Name2], [Name3], [Email], [ContactEmail], [ContactMobile], [ImageId], [SignatureId], [PreferredLanguage], [PreferredCalendar], [PreferredTimeZone], [PreferredRegion], [PreferredDigits], [Gender], [ModifiedAt]");
            lines.Add($"    FROM {Users.Delimited} WHERE [Id] = {ReservedSqlNames.UserId};");

            // --- The absorbed settings load ---
            lines.Add(SettingsLoad("@tm_SettingsStale"));
            return string.Join('\n', lines);
        }

        /// <summary>Reads the prologue's sets, reports the guard and the tags to the batch, and stages the result.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="cursor">The round trip's cursor.</param>
        /// <param name="ordinal">The prologue statement's ordinal.</param>
        /// <param name="reload">Whether the rows were asked for even when the set's tags match.</param>
        /// <param name="loadSettings">Whether the settings were asked for even when not stale.</param>
        /// <param name="readSettings">The absorbed settings load's reader.</param>
        /// <param name="result">The result's handle.</param>
        /// <param name="options">How the result is read and reported.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>A task that completes when the sets are read.</returns>
        private static async ValueTask ReadAsync(
            DataBatch batch,
            ResultSetCursor cursor,
            int ordinal,
            bool reload,
            bool loadSettings,
            StatementReader readSettings,
            BatchResult<ConnectResult> result,
            ConnectReading options,
            CancellationToken cancellationToken)
        {
            // --- Set 0: the connect row, exactly one ---
            DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
            if (!await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The connect prologue returned no connect row.");
            }

            var row = ConnectRow.Read(set);
            cursor.CountRows(1);
            batch.ReportGuard(row.GuardPassed);

            // --- Set 1: every tenant-level tag, the batch's read snapshot ---
            await VersionTagPrelude.ReadAsync(batch, cursor, ordinal, options.Time, cancellationToken).ConfigureAwait(false);
            int tenantId = batch.Connection.TenantId;
            if (options.ReportUserTags && row.UserId is int userId && row.Kind != UserKind.System
                && row.PreferencesTag is Guid preferences && row.UserPermissionsTag is Guid permissions)
            {
                batch.ReportUserVersionTags(new UserVersionTagSnapshot(
                    tenantId, userId, new VersionTag(preferences), new VersionTag(permissions), options.Time.GetUtcNow()));
            }

            // --- Set 2: the permission rows, when stale or asked for ---
            bool active = row.UserId is not null && row.IsActive;
            List<PermissionRow>? rows = null;
            if ((row.PermissionsStale || reload) && active && row.Kind != UserKind.System)
            {
                rows = await ReadPermissionsAsync(cursor, ordinal, cancellationToken).ConfigureAwait(false);
            }

            // --- Set 3: the profile, when stale ---
            UserProfile? profile = null;
            if (row.ProfileStale && active)
            {
                profile = await ReadProfileAsync(cursor, ordinal, cancellationToken).ConfigureAwait(false);
            }

            // --- The absorbed settings sets, when stale or missing ---
            if (row.SettingsStale || loadSettings)
            {
                await readSettings(cursor, cancellationToken).ConfigureAwait(false);
            }

            // --- The round trip's prologue duration, and the result ---
            options.Measured?.Invoke(batch.AttemptStarted is long started ? options.Time.GetElapsedTime(started) : TimeSpan.Zero);
            Dictionary<string, Guid> tags = batch.ReadVersionTags is VersionTagSnapshot read
                ? read.Tags.ToDictionary(static pair => pair.Key, static pair => pair.Value.Value, StringComparer.Ordinal)
                : [];
            ConnectResult value = new(
                row.UserId,
                row.Kind,
                row.UserId is not null && row.IsActive,
                row.State,
                row.PreferencesTag,
                row.UserPermissionsTag,
                tags,
                row.PermissionsStale,
                row.ProfileStale,
                row.SettingsStale,
                row.GuardPassed,
                rows,
                profile);
            options.Read?.Invoke(value);
            cursor.Complete(result, value);
        }

        /// <summary>Reads set 2: the caller's permission rows.</summary>
        /// <param name="cursor">The round trip's cursor.</param>
        /// <param name="ordinal">The prologue statement's ordinal.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The rows.</returns>
        private static async ValueTask<List<PermissionRow>> ReadPermissionsAsync(ResultSetCursor cursor, int ordinal, CancellationToken cancellationToken)
        {
            DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
            List<PermissionRow> rows = [];
            while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(new PermissionRow(
                    set.GetInt32(0),
                    set.GetInt32(1),
                    set.GetString(2),
                    set.GetBoolean(3),
                    set.GetString(4),
                    set.GetString(5),
                    set.IsDBNull(6) ? null : set.GetString(6),
                    set.IsDBNull(7) ? null : set.GetInt32(7),
                    set.IsDBNull(8) ? null : set.GetFieldValue<DateTimeOffset>(8),
                    set.IsDBNull(9) ? null : set.GetFieldValue<DateTimeOffset>(9)));
            }

            cursor.CountRows(rows.Count);
            return rows;
        }

        /// <summary>Reads set 3: the caller's profile.</summary>
        /// <param name="cursor">The round trip's cursor.</param>
        /// <param name="ordinal">The prologue statement's ordinal.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The profile.</returns>
        /// <exception cref="InvalidOperationException">The set holds no row.</exception>
        private static async ValueTask<UserProfile> ReadProfileAsync(ResultSetCursor cursor, int ordinal, CancellationToken cancellationToken)
        {
            DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
            if (!await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("The connect prologue's profile set holds no row for the user it resolved.");
            }

            UserProfile profile = new(
                set.GetString(0),
                Text(set, 1),
                Text(set, 2),
                Text(set, 3),
                Text(set, 4),
                Text(set, 5),
                set.IsDBNull(6) ? null : set.GetInt32(6),
                set.IsDBNull(7) ? null : set.GetInt32(7),
                Text(set, 8),
                Text(set, 9),
                Text(set, 10),
                Text(set, 11),
                Text(set, 12) is string digits ? Enum.Parse<DigitStyle>(digits) : null,
                Text(set, 13) is string gender ? Enum.Parse<Gender>(gender) : null,
                set.GetFieldValue<DateTimeOffset>(14));
            cursor.CountRows(1);
            return profile;
        }

        /// <summary>A nullable text column.</summary>
        /// <param name="set">The set.</param>
        /// <param name="ordinal">The column.</param>
        /// <returns>The text, or null.</returns>
        private static string? Text(DbDataReader set, int ordinal)
        {
            return set.IsDBNull(ordinal) ? null : set.GetString(ordinal);
        }

        /// <summary>The connect row of set 0.</summary>
        /// <param name="UserId">The resolved user's id; null when no user matched.</param>
        /// <param name="Kind">The user's kind; null when no user matched.</param>
        /// <param name="IsActive">Whether the user is active.</param>
        /// <param name="State">The user's state after the flip; null when no user matched, and for the system.</param>
        /// <param name="PreferencesTag">The user's preferences tag.</param>
        /// <param name="UserPermissionsTag">The user's permissions tag.</param>
        /// <param name="PermissionsStale">Whether the cached set's tags differ.</param>
        /// <param name="ProfileStale">Whether the cached profile's tag differs.</param>
        /// <param name="SettingsStale">Whether the settings tag differs.</param>
        /// <param name="GuardPassed">Whether the guard passed.</param>
        private sealed record ConnectRow(
            int? UserId,
            UserKind? Kind,
            bool IsActive,
            UserState? State,
            Guid? PreferencesTag,
            Guid? UserPermissionsTag,
            bool PermissionsStale,
            bool ProfileStale,
            bool SettingsStale,
            bool GuardPassed)
        {
            /// <summary>Reads the row the set stands on.</summary>
            /// <param name="set">The set.</param>
            /// <returns>The row.</returns>
            public static ConnectRow Read(DbDataReader set)
            {
                return new ConnectRow(
                    set.IsDBNull(0) ? null : set.GetInt32(0),
                    Text(set, 1) is string kind ? Enum.Parse<UserKind>(kind) : null,
                    !set.IsDBNull(2) && set.GetBoolean(2),
                    Text(set, 3) is string state ? Enum.Parse<UserState>(state) : null,
                    set.IsDBNull(4) ? null : set.GetGuid(4),
                    set.IsDBNull(5) ? null : set.GetGuid(5),
                    !set.IsDBNull(6) && set.GetBoolean(6),
                    !set.IsDBNull(7) && set.GetBoolean(7),
                    !set.IsDBNull(8) && set.GetBoolean(8),
                    !set.IsDBNull(9) && set.GetBoolean(9));
            }
        }
    }

    /// <summary>How one connect prologue's result is read and reported.</summary>
    /// <param name="Time">The clock that stamps the snapshots and measures the round trip.</param>
    /// <param name="ReportUserTags">Whether the user's tags are the scope's caller's, which the batch's outcome reports.</param>
    /// <param name="Measured">Receives the round trip's duration up to the end of the prologue's sets, per attempt; null for none.</param>
    /// <param name="Read">Receives each result read, before it is staged; null for none.</param>
    internal sealed record ConnectReading(TimeProvider Time, bool ReportUserTags, Action<TimeSpan>? Measured = null, Action<ConnectResult>? Read = null);
}
