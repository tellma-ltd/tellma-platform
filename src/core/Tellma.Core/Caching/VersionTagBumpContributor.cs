// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.Common;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Access;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.TableTypes;

namespace Tellma.Core.Caching
{
    // The bump and the cache.changed table are spec 0013 §2.5.
    /// <summary>
    ///     The bump epilogue: the last statement before <c>COMMIT</c> of every batch whose written
    ///     tables or explicit bumps resolve to a tag. It moves the tenant-level tags to the batch's one
    ///     application-generated tag, moves the user-level tags of every user the written rows name,
    ///     returns those users, and after the commit announces each move to the clients it concerns.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The tenant-level move must find a row for every name: a registered name the database
    ///         lacks is a deployment defect that fails the batch with <c>VersionTag.Missing</c>, never a
    ///         silent miss. Only the migrator inserts rows.
    ///     </para>
    ///     <para>
    ///         The users come from the batch's user-id sources: the rows a platform statement wrote, and
    ///         the ids every raw statement declared for a table with a user-level rule.
    ///     </para>
    /// </remarks>
    /// <param name="publisher">The client event publisher, which the commit callback publishes through.</param>
    /// <param name="metrics">The version tags' instruments.</param>
    internal sealed class VersionTagBumpContributor(IClientEventPublisher publisher, VersionTagMetrics metrics) : IDataBatchContributor
    {
        /// <summary>The contributor's order: its epilogue runs after those of every higher order.</summary>
        public const int BumpOrder = 50;

        /// <summary>The client event every bump announces.</summary>
        public const string CacheChangedEvent = "cache.changed";

        /// <summary>
        ///     The tenant-level move: one row at a time, in the guard's binary name order, so the bump
        ///     converts its locks in the order every guard takes them; then the refusal when a name has
        ///     no row.
        /// </summary>
        private static readonly string TenantMove = string.Join(
            '\n',
            $"DECLARE {ReservedSqlNames.BumpName} nvarchar(128) = (SELECT MIN([Id] COLLATE {PersistChecks.NameOrder}) FROM {ReservedSqlNames.TagNames}), {ReservedSqlNames.BumpCount} int = 0;",
            $"WHILE {ReservedSqlNames.BumpName} IS NOT NULL",
            "BEGIN",
            $"    UPDATE {VersionTagRowConfiguration.Table.Delimited} SET [Tag] = {ReservedSqlNames.Tag} WHERE [Name] = {ReservedSqlNames.BumpName};",
            $"    SET {ReservedSqlNames.BumpCount} += @@ROWCOUNT;",
            $"    SET {ReservedSqlNames.BumpName} = (SELECT MIN([Id] COLLATE {PersistChecks.NameOrder}) FROM {ReservedSqlNames.TagNames} WHERE [Id] COLLATE {PersistChecks.NameOrder} > {ReservedSqlNames.BumpName});",
            "END;",
            string.Create(
                CultureInfo.InvariantCulture,
                $"IF {ReservedSqlNames.BumpCount} <> (SELECT COUNT(*) FROM {ReservedSqlNames.TagNames}) THROW {TellmaSqlErrors.Invariant}, N'{TellmaSqlErrors.VersionTagMissingCode}', 1;"));

        /// <inheritdoc />
        public int Order => BumpOrder;

        /// <summary>The batch-local table of the users whose tag in a column the batch bumps.</summary>
        /// <param name="column">The user-level tag.</param>
        /// <returns><c>@tm_userIds_&lt;Column&gt;</c>.</returns>
        public static string UserIdsTable(UserVersionTagNames column)
        {
            return ReservedSqlNames.UserIdsPrefix + column.ToString();
        }

        /// <summary>The <c>core.UserStamps</c> column holding a user-level tag.</summary>
        /// <param name="column">The user-level tag.</param>
        /// <returns><c>PermissionsTag</c> or <c>PreferencesTag</c>.</returns>
        public static string StampColumn(UserVersionTagNames column)
        {
            return column switch
            {
                UserVersionTagNames.Permissions => nameof(UserStamp.PermissionsTag),
                UserVersionTagNames.Preferences => nameof(UserStamp.PreferencesTag),
                _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Not a user-level version tag."),
            };
        }

        /// <summary>
        ///     The bump's text and the tables it writes, for a plan and the batch's user-id sources.
        ///     Separate from <see cref="Contribute" /> so its shape can be checked without a batch.
        /// </summary>
        /// <param name="plan">What the batch bumps.</param>
        /// <param name="sources">Where the affected users are found.</param>
        /// <returns>The text, and the tables it writes.</returns>
        /// <exception cref="InvalidOperationException">A user-level rule fires for a table no source names the users of.</exception>
        public static (string Text, IReadOnlySet<TableName> Writes) Compose(VersionTagBumpPlan plan, IReadOnlyList<UserIdSource> sources)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(sources);

            StringBuilder text = new();
            HashSet<TableName> writes = [];

            // --- The tenant-level names: moved in name order, every one of which must find its row ---
            if (plan.TenantNames.Count > 0)
            {
                _ = text.Append(TenantMove).Append('\n');
                _ = writes.Add(VersionTagRowConfiguration.Table);
            }

            // --- The user-level tags: each column's users, gathered from every source, then moved ---
            IReadOnlyList<UserVersionTagNames> columns = plan.UserColumns;
            if (columns.Count > 0)
            {
                foreach (UserVersionTagNames column in columns)
                {
                    _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {UserIdsTable(column)} TABLE ([Id] int NOT NULL PRIMARY KEY);\n");
                }

                // Each source once per column; NOT EXISTS keeps a user two sources name from violating the key.
                HashSet<(UserVersionTagNames Column, string Source, string SourceColumn)> filled = [];
                foreach (UserVersionTagRule rule in plan.UserRules)
                {
                    List<UserIdSource> covering = [.. sources.Where(source => source.Tables.Contains(rule.Table) && (source.Column is null || source.Column == rule.UserIdColumn))];
                    if (covering.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"The batch writes {rule.Table}, whose writes bump the {rule.Column} tag of the users they name, but no statement declared which users it wrote. Declare them: SqlOptions.UserIds on a raw statement, or the rows of the platform's own statement.");
                    }

                    foreach (UserIdSource source in covering)
                    {
                        string sourceColumn = source.Column ?? "Id";
                        if (!filled.Add((rule.Column, source.Identifier.Name, sourceColumn)))
                        {
                            continue;
                        }

                        string target = UserIdsTable(rule.Column);
                        string read = $"s.[{sourceColumn.Replace("]", "]]", StringComparison.Ordinal)}]";
                        _ = text.Append(CultureInfo.InvariantCulture, $"INSERT INTO {target} ([Id]) SELECT DISTINCT {read} FROM {source.Identifier.Name} AS s WHERE {read} IS NOT NULL AND NOT EXISTS (SELECT 1 FROM {target} AS i WHERE i.[Id] = {read});\n");
                    }
                }

                foreach (UserVersionTagNames column in columns)
                {
                    _ = text.Append(CultureInfo.InvariantCulture, $"UPDATE u SET u.[{StampColumn(column)}] = {ReservedSqlNames.Tag} FROM {UserStampConfiguration.Table.Delimited} AS u JOIN {UserIdsTable(column)} AS i ON i.[Id] = u.[UserId];\n");
                }

                // The affected users, which the commit callback addresses the user events to.
                _ = text.Append(string.Join(
                    " UNION ALL ",
                    columns.Select((column, index) => index == 0
                        ? $"SELECT N'{column}' AS [Column], [Id] FROM {UserIdsTable(column)}"
                        : $"SELECT N'{column}', [Id] FROM {UserIdsTable(column)}"))).Append(';');
                _ = writes.Add(UserStampConfiguration.Table);
            }

            return (text.ToString().TrimEnd('\n'), writes);
        }

        /// <inheritdoc />
        public void Contribute(IDataBatch batch, DataBatchStage stage)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (stage != DataBatchStage.Epilogue || batch is not DataBatch data)
            {
                return;
            }

            VersionTagBumpPlan plan = data.Bumps;
            if (plan.TenantNames.Count == 0 && plan.UserRules.Count == 0)
            {
                return;
            }

            (string text, IReadOnlySet<TableName> writes) = Compose(plan, data.UserIdSources);

            // --- The statement: the tag, the names' table, and the reader of the affected users ---
            StatementDraft draft = data.Begin();
            try
            {
                int ordinal = draft.Ordinal;
                draft.Reserved(data.TagParameter());
                if (plan.TenantNames.Count > 0)
                {
                    List<StringList> names = [.. plan.TenantNames.Select(static name => new StringList { Id = name })];
                    TableTypeBinding binding = data.Binder.Binding(typeof(StringList));
                    draft.Reserved(new TableParameter(ReservedSqlNames.TagNames, names, names.Count, parameter => binding.Parameter(parameter, names)));
                }

                StatementReader? reader = plan.UserRules.Count == 0
                    ? null
                    : async (cursor, cancellationToken) => data.ReportAffectedUsers(await ReadAffectedAsync(cursor, ordinal, cancellationToken).ConfigureAwait(false));
                // Idempotent: a second run writes the batch's one tag again, so a re-run after a lost
                // connection moves nothing further.
                data.Append(draft, text, writes, idempotent: true, reader: reader);
            }
            catch
            {
                data.Discard(draft);
                throw;
            }

            // --- After the commit: meter the moves and announce them ---
            data.OnCommitted(outcome => Committed(data, plan, outcome));
        }

        /// <summary>Reads the affected users, on which the cursor's next set stands.</summary>
        /// <param name="cursor">The round trip's cursor.</param>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>Each affected user with the column bumped for it.</returns>
        private static async ValueTask<IReadOnlyList<AffectedUser>> ReadAffectedAsync(ResultSetCursor cursor, int ordinal, CancellationToken cancellationToken)
        {
            DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
            List<AffectedUser> affected = [];
            while (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                affected.Add(new AffectedUser(Enum.Parse<UserVersionTagNames>(set.GetString(0)), set.GetInt32(1)));
            }

            cursor.CountRows(affected.Count);
            return affected;
        }

        /// <summary>Meters a committed batch's moves and publishes <c>cache.changed</c> to the clients each concerns.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="plan">What it bumped.</param>
        /// <param name="outcome">Its outcome, whose snapshot carries the moved tags.</param>
        private void Committed(DataBatch batch, VersionTagBumpPlan plan, BatchOutcome outcome)
        {
            IReadOnlyList<AffectedUser> affected = batch.AffectedUsers ?? [];
            metrics.RecordBumps(plan.TenantNames, affected.Count);

            VersionTag tag = new(batch.Tag);
            foreach (ClientEvent clientEvent in Events(plan.TenantNames, affected, tag, outcome.VersionTags[VersionTagNames.Permissions]))
            {
                publisher.Publish(null, clientEvent);
            }
        }

        /// <summary>
        ///     The <c>cache.changed</c> events of a batch's moves: the tenant group hears of
        ///     <c>settings</c>, each <c>entity:*</c> list and <c>permissions</c> (without a value, since
        ///     the client's permissions tag is per user); each affected user hears of their own tags.
        ///     <c>entities</c> and a pack's tags are announced to no one.
        /// </summary>
        /// <param name="tenantNames">The tenant-level names moved.</param>
        /// <param name="affected">The users whose tags moved.</param>
        /// <param name="tag">The tag every moved name now holds.</param>
        /// <param name="tenantPermissions">The tenant's <c>permissions</c> tag after the batch, part of a user's composite.</param>
        /// <returns>The events.</returns>
        internal static IEnumerable<ClientEvent> Events(IReadOnlyList<string> tenantNames, IReadOnlyList<AffectedUser> affected, VersionTag tag, VersionTag tenantPermissions)
        {
            foreach (string name in tenantNames)
            {
                CacheChangedPayload? payload = name switch
                {
                    VersionTagNames.Settings => new(name, VersionTagWire.Settings(tag)),
                    VersionTagNames.Permissions => new(name, null),
                    _ when name.StartsWith(VersionTagNames.EntityPrefix, StringComparison.Ordinal) => new(name, VersionTagWire.Entities(tag)),
                    _ => null,
                };

                if (payload is not null)
                {
                    yield return new ClientEvent(CacheChangedEvent, [], payload);
                }
            }

            foreach (AffectedUser user in affected)
            {
                CacheChangedPayload payload = user.Column == UserVersionTagNames.Permissions
                    ? new(VersionTagWire.PermissionsItem, VersionTagWire.Permissions(tenantPermissions, tag))
                    : new(VersionTagWire.PreferencesItem, VersionTagWire.Preferences(tag));
                yield return new ClientEvent(CacheChangedEvent, [user.UserId], payload);
            }
        }
    }

    /// <summary>A user whose user-level tag a batch moved.</summary>
    /// <param name="Column">The tag moved.</param>
    /// <param name="UserId">The user.</param>
    internal sealed record AffectedUser(UserVersionTagNames Column, int UserId);

    /// <summary>The payload of <c>cache.changed</c>: the tag that moved and, when the client keys a cache by it, its new wire form.</summary>
    /// <param name="Tag">The tag's name as the client knows it: <c>settings</c>, <c>permissions</c>, <c>preferences</c> or <c>entity:&lt;Name&gt;</c>.</param>
    /// <param name="Value">The new wire tag, or null when the client must fetch it (the tenant's <c>permissions</c>).</param>
    internal sealed record CacheChangedPayload(string Tag, string? Value);
}
