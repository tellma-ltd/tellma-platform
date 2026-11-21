// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The blob machinery's fixed statements, each appended on the batch's internal channel: the
    ///     stage, the read a download resolves, and the reconcile of one kind. Those that write declare
    ///     <c>core.Blobs</c> as the table they write, which no caller authority check refuses:
    ///     <c>core.Blobs</c> belongs to no stack.
    /// </summary>
    internal static class BlobStatements
    {
        /// <summary>The blob table.</summary>
        public static TableName Blobs { get; } = new("core", "Blobs");

        /// <summary>The blob table's sequence, the one source of blob ids.</summary>
        public const string BlobsSequence = "[core].[sq_Blobs]";

        // spec 0017 §3.4: the stage statement.
        /// <summary>
        ///     Appends the stage statement: the soft per-user quota check, unless
        ///     <paramref name="quota" /> is null, then the insert of the <see cref="BlobState.Staged" />
        ///     row with its id taken from the blob sequence inside the statement, and its id and
        ///     deadline as the one result set.
        /// </summary>
        /// <remarks>
        ///     The quota reads the uploader's unexpired staged rows without locks: two concurrent
        ///     uploads may both pass one slot below the limit, and an expired row the sweep has not yet
        ///     claimed counts for nothing. Exceeding it throws <see cref="TellmaSqlErrors.Invariant" />
        ///     with <see cref="BlobRejectedException.StagingQuotaExceeded" />, which the service
        ///     translates.
        /// </remarks>
        /// <param name="batch">The batch the statement rides.</param>
        /// <param name="row">The staged row's values.</param>
        /// <param name="quota">The uploader's quota; null in the platform's own scope, which is exempt.</param>
        /// <returns>The new row's id and deadline, once the batch executed and the statement ran.</returns>
        public static BatchResult<StagedBlob> Stage(DataBatch batch, StagedRow row, StagingQuota? quota)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(row);

            BatchResult<StagedBlob> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The parameters, in the spec's order, each typed as the column it is compared with or stored in ---
                string userId = draft.Scalar(SqlDbType.Int, row.UserId);
                string kind = draft.Scalar(SqlDbType.NVarChar, row.Kind, 40);
                string storageKey = draft.Scalar(SqlDbType.NVarChar, row.StorageKey, 120);
                string contentType = draft.Scalar(SqlDbType.NVarChar, row.ContentType, 100);
                string fileName = draft.Scalar(SqlDbType.NVarChar, row.FileName, 255);
                string size = draft.Scalar(SqlDbType.BigInt, row.Size);
                string sha256 = draft.Scalar(SqlDbType.Binary, row.Sha256, 32);
                string width = draft.Scalar(SqlDbType.Int, row.Width);
                string height = draft.Scalar(SqlDbType.Int, row.Height);
                string variants = draft.Scalar(SqlDbType.NVarChar, row.Variants, 100);
                string ttl = draft.Scalar(SqlDbType.Int, row.TtlSeconds);

                // --- The statement's own names ---
                string now = draft.Name(ReservedSqlNames.StatementNames.Now);
                string expiry = draft.Name(ReservedSqlNames.StatementNames.Expiry);
                string id = draft.Name(ReservedSqlNames.StatementNames.Id);

                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {now} datetimeoffset(3) = SYSUTCDATETIME();\n");

                // --- The soft quota: the uploader's unexpired staged rows, read without locks ---
                if (quota is not null)
                {
                    string maxCount = draft.Scalar(SqlDbType.Int, quota.MaxCount);
                    string maxBytes = draft.Scalar(SqlDbType.BigInt, quota.MaxBytes);
                    string count = draft.Name(ReservedSqlNames.StatementNames.StagedCount);
                    string bytes = draft.Name(ReservedSqlNames.StatementNames.StagedBytes);
                    _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {count} int, {bytes} bigint;\n")
                        .Append(CultureInfo.InvariantCulture, $"SELECT {count} = COUNT(*), {bytes} = ISNULL(SUM([Size]), 0)\n")
                        .Append(CultureInfo.InvariantCulture, $"FROM {Blobs.Delimited} WHERE [CreatedById] = {userId} AND [State] = '{nameof(BlobState.Staged)}' AND [ExpiresAt] > {now};\n")
                        .Append(CultureInfo.InvariantCulture, $"IF {count} >= {maxCount} OR {bytes} + {size} > {maxBytes}\n")
                        .Append(CultureInfo.InvariantCulture, $"    THROW {TellmaSqlErrors.Invariant}, N'{BlobRejectedException.StagingQuotaExceeded}', 1;\n");
                }

                // --- The row: its id from the sequence inside the statement, never from the allocator ---
                _ = text.Append(CultureInfo.InvariantCulture, $"DECLARE {expiry} datetimeoffset(3) = DATEADD(second, {ttl}, {now});\n")
                    .Append(CultureInfo.InvariantCulture, $"DECLARE {id} int = NEXT VALUE FOR {BlobsSequence};\n")
                    .Append(CultureInfo.InvariantCulture, $"INSERT INTO {Blobs.Delimited} ([Id], [Kind], [StorageKey], [State], [ContentType], [FileName], [Size], [Sha256],\n")
                    .Append("    [Width], [Height], [Variants], [CreatedAt], [CreatedById], [CommittedAt], [ExpiresAt])\n")
                    .Append(CultureInfo.InvariantCulture, $"VALUES ({id}, {kind}, {storageKey}, '{nameof(BlobState.Staged)}', {contentType}, {fileName}, {size}, {sha256},\n")
                    .Append(CultureInfo.InvariantCulture, $"    {width}, {height}, {variants}, {now}, {userId}, NULL, {expiry});\n")
                    .Append(CultureInfo.InvariantCulture, $"SELECT {id} AS [Id], {expiry} AS [ExpiresAt];");

                // --- The reader: the one row of the id and the deadline ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text.ToString(), new HashSet<TableName> { Blobs }, idempotent: false, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    if (!await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(string.Create(
                            CultureInfo.InvariantCulture, $"The stage statement at {ordinal} returned no row."));
                    }

                    StagedBlob staged = new(set.GetInt32(0), set.GetFieldValue<DateTimeOffset>(1));
                    cursor.CountRows(1);
                    cursor.Complete(result, staged);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }
        // spec 0017 §5.1 step 3, statement 2: the blob row, by raw text, since its storage name lies
        // outside the query language's schema.
        /// <summary>
        ///     Appends the read of the blob row a download may serve: the row of the id and kind when it
        ///     is committed, or staged by the caller and not yet expired. A released or deleting row,
        ///     another kind and another uploader's staged row read as no row.
        /// </summary>
        /// <param name="batch">The batch the statement rides.</param>
        /// <param name="id">The blob's id.</param>
        /// <param name="kind">The kind the download names.</param>
        /// <param name="callerId">The caller, the only user a staged row is served to.</param>
        /// <returns>The row, or null when none matches, once the batch executed.</returns>
        public static BatchResult<ResolvedBlob?> Resolve(DataBatch batch, int id, string kind, int callerId)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(kind);

            BatchResult<ResolvedBlob?> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                // --- The parameters, in the spec's order ---
                string idParameter = draft.Scalar(SqlDbType.Int, id);
                string kindParameter = draft.Scalar(SqlDbType.NVarChar, kind, 40);
                string caller = draft.Scalar(SqlDbType.Int, callerId);

                string text = string.Create(CultureInfo.InvariantCulture, $"""
                    SELECT [Id], [Kind], [State], [{BlobConfiguration.StorageKey}], [ContentType], [Size], [FileName], [Width], [Height], [Variants]
                    FROM {Blobs.Delimited}
                    WHERE [Id] = {idParameter} AND [Kind] = {kindParameter}
                      AND ([State] = '{nameof(BlobState.Committed)}'
                           OR ([State] = '{nameof(BlobState.Staged)}' AND [CreatedById] = {caller} AND [ExpiresAt] > SYSUTCDATETIME()));
                    """);

                // --- The reader: at most one row, the id being the key ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text, new HashSet<TableName>(), idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    ResolvedBlob? row = null;
                    if (await set.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        row = new ResolvedBlob(
                            set.GetInt32(0),
                            set.GetString(1),
                            Enum.Parse<BlobState>(set.GetString(2), ignoreCase: false),
                            set.GetString(3),
                            set.GetString(4),
                            set.GetInt64(5),
                            set.IsDBNull(6) ? null : set.GetString(6),
                            set.IsDBNull(7) ? null : set.GetInt32(7),
                            set.IsDBNull(8) ? null : set.GetInt32(8),
                            set.GetString(9));
                        cursor.CountRows(1);
                    }

                    cursor.Complete(result, row);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }

        // spec 0017 §7.2: the reconcile of one kind, which the reconcile job runs once per kind on a
        // maintenance batch; a day's grace keeps a blob committed by a transaction still in flight.
        /// <summary>
        ///     Appends the release of the kind's committed blobs that no owner row references and that
        ///     were committed more than a day ago, returning how many it released. Running it twice
        ///     releases nothing more.
        /// </summary>
        /// <param name="batch">The batch the statement rides.</param>
        /// <param name="kind">The kind, with the table and column of its owner rows.</param>
        /// <returns>The number of blobs released, once the batch executed.</returns>
        public static BatchResult<int> Reconcile(DataBatch batch, BlobKindDescriptor kind)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(kind);

            BatchResult<int> result = new();
            StatementDraft draft = batch.Begin();
            try
            {
                string kindParameter = draft.Scalar(SqlDbType.NVarChar, kind.Kind, 40);
                string text = string.Create(CultureInfo.InvariantCulture, $"""
                    UPDATE b SET [State] = '{nameof(BlobState.Released)}', [ExpiresAt] = SYSUTCDATETIME()
                    FROM {Blobs.Delimited} AS b
                    WHERE b.[Kind] = {kindParameter} AND b.[State] = '{nameof(BlobState.Committed)}' AND b.[CommittedAt] < DATEADD(day, -1, SYSUTCDATETIME())
                      AND NOT EXISTS (SELECT 1 FROM {kind.OwnerTable.Delimited} AS o WHERE o.{ConventionModel.Delimit(kind.OwnerColumn)} = b.[Id]);
                    SELECT @@ROWCOUNT AS [Released];
                    """);

                // --- The reader: the one count ---
                int ordinal = draft.Ordinal;
                batch.Append(draft, text, new HashSet<TableName> { Blobs }, idempotent: true, reader: async (cursor, cancellationToken) =>
                {
                    DbDataReader set = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                    int released = await set.ReadAsync(cancellationToken).ConfigureAwait(false)
                        ? set.GetInt32(0)
                        : throw new InvalidOperationException("The reconcile statement returned no count.");
                    cursor.CountRows(1);
                    cursor.Complete(result, released);
                });
                return result;
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }
        }
    }

    /// <summary>The values of one staged row, as the stage statement binds them.</summary>
    /// <param name="UserId">The uploader, who alone may attach it.</param>
    /// <param name="Kind">The blob's kind.</param>
    /// <param name="StorageKey">The primary's object name.</param>
    /// <param name="ContentType">The stored primary's media type.</param>
    /// <param name="FileName">The sanitised file name, or null.</param>
    /// <param name="Size">The stored primary's length in bytes.</param>
    /// <param name="Sha256">The SHA-256 of the stored primary, 32 bytes.</param>
    /// <param name="Width">The stored image's width, or null.</param>
    /// <param name="Height">The stored image's height, or null.</param>
    /// <param name="Variants">The comma-separated variants stored beside the primary; empty for none.</param>
    /// <param name="TtlSeconds">How many seconds the row stays staged.</param>
    internal sealed record StagedRow(
        int UserId,
        string Kind,
        string StorageKey,
        string ContentType,
        string? FileName,
        long Size,
        byte[] Sha256,
        int? Width,
        int? Height,
        string Variants,
        int TtlSeconds);

    /// <summary>The uploader's soft staging quota.</summary>
    /// <param name="MaxCount">The most unexpired staged rows the uploader may hold.</param>
    /// <param name="MaxBytes">The most bytes of unexpired staged rows the uploader may hold.</param>
    internal sealed record StagingQuota(int MaxCount, long MaxBytes);

    /// <summary>What the stage statement returns.</summary>
    /// <param name="Id">The new row's id, from the blob sequence.</param>
    /// <param name="ExpiresAt">The row's staging deadline.</param>
    internal sealed record StagedBlob(int Id, DateTimeOffset ExpiresAt);

    /// <summary>A blob row a download may serve, as the resolve statement reads it.</summary>
    /// <param name="Id">The blob's id.</param>
    /// <param name="Kind">The blob's kind.</param>
    /// <param name="State">Committed, or staged by the caller and unexpired.</param>
    /// <param name="StorageKey">The primary's full object name.</param>
    /// <param name="ContentType">The stored primary's media type.</param>
    /// <param name="Size">The stored primary's length in bytes.</param>
    /// <param name="FileName">The sanitised original file name, or null.</param>
    /// <param name="Width">An image's width in pixels, or null.</param>
    /// <param name="Height">An image's height in pixels, or null.</param>
    /// <param name="Variants">The comma-separated variants stored beside the primary; empty for none.</param>
    internal sealed record ResolvedBlob(
        int Id,
        string Kind,
        BlobState State,
        string StorageKey,
        string ContentType,
        long Size,
        string? FileName,
        int? Width,
        int? Height,
        string Variants);
}
