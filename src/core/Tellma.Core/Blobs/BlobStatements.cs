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

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The blob machinery's fixed statements, each appended on the batch's internal channel with
    ///     <c>core.Blobs</c> declared as the table it writes, so no caller authority check refuses it:
    ///     <c>core.Blobs</c> belongs to no stack.
    /// </summary>
    internal static class BlobStatements
    {
        /// <summary>The blob table.</summary>
        public static TableName Blobs { get; } = new("core", "Blobs");

        /// <summary>The blob table's sequence, the one source of blob ids.</summary>
        public const string BlobsSequence = "[core].[sq_Blobs]";

        /// <summary>
        ///     Appends the stage statement (spec 0017 §3.4): the soft per-user quota check, unless
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
}
