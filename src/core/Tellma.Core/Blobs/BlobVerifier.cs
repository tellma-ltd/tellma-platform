// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Blobs
{
    // spec 0017 §7.3: the operator's comparison of a tenant's storage with its core.Blobs rows, after a
    // restore of either. Each registered kind is walked one fan-out segment at a time: the segment is
    // listed first and its rows read second. Staging writes the row before the bytes and the sweep
    // deletes the bytes before the row, so an object listed first always has its row by the time the
    // rows are read, unless no row will ever lead to it again: an object reported unreferenced is a
    // true orphan, and deleting it races nothing. Rows are never written.
    /// <summary>
    ///     Compares one tenant's stored objects with its blob rows: the objects no row references and
    ///     the rows whose objects are gone. The unreferenced objects are deleted on request.
    /// </summary>
    /// <param name="store">The blob store.</param>
    /// <param name="kinds">The registered kinds, whose objects are walked.</param>
    /// <param name="database">The tenant's database, which each segment's read comes from.</param>
    /// <param name="time">The clock of the young-row grace.</param>
    internal sealed class BlobVerifier(IBlobStore store, IBlobKindRegistry kinds, ITenantDatabase database, TimeProvider time)
    {
        /// <summary>
        ///     How old a row must be before its missing objects are reported: a row younger than this
        ///     may be staged and still writing its bytes.
        /// </summary>
        public static TimeSpan MissingGrace { get; } = TimeSpan.FromMinutes(5);

        /// <summary>Walks every registered kind of the tenant.</summary>
        /// <param name="tenantId">The tenant; positive.</param>
        /// <param name="deleteOrphans">Whether the objects no row references are deleted as they are found.</param>
        /// <param name="report">Receives each finding as it is found.</param>
        /// <param name="cancellationToken">Cancels the walk.</param>
        /// <returns>The walk's totals.</returns>
        /// <exception cref="BlobStoreException">The store failed a listing or a delete.</exception>
        public async Task<BlobVerification> VerifyAsync(int tenantId, bool deleteOrphans, Action<BlobFinding> report, CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tenantId);
            ArgumentNullException.ThrowIfNull(report);

            BlobVerification totals = new(0, 0, 0, 0, 0);
            foreach (BlobKindDescriptor kind in kinds.All.OrderBy(static kind => kind.Kind, StringComparer.Ordinal))
            {
                for (int fanOut = 0; fanOut <= byte.MaxValue; fanOut++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string segment = string.Create(CultureInfo.InvariantCulture, $"{kind.Kind}{BlobName.SegmentSeparator}{fanOut:x2}");
                    totals += await VerifySegmentAsync(tenantId, kind.Kind, segment, deleteOrphans, report, cancellationToken).ConfigureAwait(false);
                }
            }

            return totals;
        }

        /// <summary>Compares one segment: its listing first, its rows second.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="kind">The kind.</param>
        /// <param name="segment">The segment.</param>
        /// <param name="deleteOrphans">Whether the unreferenced objects are deleted.</param>
        /// <param name="report">Receives each finding.</param>
        /// <param name="cancellationToken">Cancels the walk.</param>
        /// <returns>The segment's totals.</returns>
        private async Task<BlobVerification> VerifySegmentAsync(
            int tenantId, string kind, string segment, bool deleteOrphans, Action<BlobFinding> report, CancellationToken cancellationToken)
        {
            // --- The listing, before the rows ---
            HashSet<string> listed = new(StringComparer.Ordinal);
            await foreach (BlobStoreEntry entry in store.ListAsync(tenantId, segment, cancellationToken).ConfigureAwait(false))
            {
                _ = listed.Add(entry.Name);
            }

            // --- The rows, then the grace of a row still writing its bytes, wide enough to absorb clock skew ---
            var read = (DataBatch)database.CreateBatch(BatchPurpose.Maintenance);
            BatchResult<IReadOnlyList<SegmentBlob>> rows = BlobStatements.Segment(read, kind, segment);
            _ = await read.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            DateTimeOffset youngSince = time.GetUtcNow() - MissingGrace;

            // --- Each row's objects: present ones are referenced, absent ones of a settled row are missing ---
            HashSet<string> referenced = new(StringComparer.Ordinal);
            int missing = 0;
            foreach (SegmentBlob row in rows.Value.OrderBy(static row => row.Id))
            {
                string[] names = BlobObjectNames.Of(row.StorageKey, row.Variants) ?? [row.StorageKey];
                referenced.UnionWith(names);

                // A deleting row's objects are going by design, and a young row may still be writing them.
                if (row.State == BlobState.Deleting || row.CreatedAt > youngSince)
                {
                    continue;
                }

                foreach (string name in names.Where(name => !listed.Contains(name)))
                {
                    report(new BlobFinding(BlobFindingKind.Missing, name, row.Id, row.State));
                    missing++;
                }
            }

            // --- The objects no row references, deleted together on request ---
            List<string> orphans = [.. listed.Where(name => !referenced.Contains(name)).Order(StringComparer.Ordinal)];
            foreach (string orphan in orphans)
            {
                report(new BlobFinding(BlobFindingKind.Orphan, orphan, null, null));
            }

            if (deleteOrphans && orphans.Count > 0)
            {
                await store.DeleteAsync(tenantId, orphans, cancellationToken).ConfigureAwait(false);
            }

            return new BlobVerification(listed.Count, rows.Value.Count, orphans.Count, missing, deleteOrphans ? orphans.Count : 0);
        }
    }

    /// <summary>What a finding of the verification is.</summary>
    internal enum BlobFindingKind
    {
        /// <summary>A stored object that no row references.</summary>
        Orphan,

        /// <summary>An object a row references that the store does not hold.</summary>
        Missing,
    }

    /// <summary>One finding of the verification.</summary>
    /// <param name="Kind">An orphaned object or a missing one.</param>
    /// <param name="Name">The object's name.</param>
    /// <param name="BlobId">The row that references a missing object; null for an orphan.</param>
    /// <param name="State">That row's state; null for an orphan.</param>
    internal sealed record BlobFinding(BlobFindingKind Kind, string Name, int? BlobId, BlobState? State);

    /// <summary>The totals of a verification.</summary>
    /// <param name="Objects">The objects listed.</param>
    /// <param name="Rows">The rows read.</param>
    /// <param name="Orphans">The objects no row references.</param>
    /// <param name="Missing">The objects rows reference that the store does not hold.</param>
    /// <param name="Deleted">The unreferenced objects deleted.</param>
    internal readonly record struct BlobVerification(int Objects, int Rows, int Orphans, int Missing, int Deleted)
    {
        /// <summary>Adds two totals.</summary>
        /// <param name="left">The first.</param>
        /// <param name="right">The second.</param>
        /// <returns>The sum.</returns>
        public static BlobVerification operator +(BlobVerification left, BlobVerification right)
        {
            return new BlobVerification(
                left.Objects + right.Objects,
                left.Rows + right.Rows,
                left.Orphans + right.Orphans,
                left.Missing + right.Missing,
                left.Deleted + right.Deleted);
        }
    }
}
