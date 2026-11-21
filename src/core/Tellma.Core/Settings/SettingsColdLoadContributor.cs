// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Caching;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Settings
{
    // The contributor is spec 0013 §5.7's cold load at Order 60, after the prelude at 50: bare on the
    // settings store's own batch; on a batch that carries the connect prologue, absorbed by it.
    /// <summary>
    ///     Appends the settings cold load to the settings store's own load batch, after the
    ///     version-tag prelude, and reads its rows into the store's pending load. A batch that
    ///     carries the connect prologue gets nothing from it: the prologue absorbs the load through
    ///     <see cref="Absorb" />.
    /// </summary>
    /// <param name="store">The settings store.</param>
    /// <param name="snapshots">The process's version-tag snapshots.</param>
    /// <param name="accessor">The scope's request context, whose kind decides whether the placeholder is accepted.</param>
    internal sealed class SettingsColdLoadContributor(TenantSettingsStore store, IVersionTagSnapshots snapshots, IRequestContextAccessor accessor)
        : IDataBatchContributor
    {
        /// <summary>The contributor's order: right after the version-tag prelude.</summary>
        public const int ColdLoadOrder = VersionTagPreludeContributor.PreludeOrder + 10;

        /// <inheritdoc />
        public int Order => ColdLoadOrder;

        /// <inheritdoc />
        public void Contribute(IDataBatch batch, DataBatchStage stage)
        {
            ArgumentNullException.ThrowIfNull(batch);

            // --- The prologue stage of an unmarked tenant batch the store created for its load; nothing else ---
            if (stage != DataBatchStage.Prologue
                || batch is not DataBatch { CarriesConnectPrologue: false } data
                || data.Purpose == BatchPurpose.Maintenance
                || !store.TryTakeRequest(data, out SettingsLoadRequest request))
            {
                return;
            }

            // --- The two statements, bare, their rows completing the store's load ---
            StatementDraft draft = data.Begin();
            data.Append(draft, SettingsColdLoad.Text, reader: Reader(data, draft.Ordinal, (cursor, entry, tag) => cursor.Settle(request.Result, () => (entry, tag))));
        }

        /// <summary>
        ///     What the connect prologue needs to absorb the cold load into a batch it marks: whether
        ///     the load is wanted, the parameter that tells the prologue's text, and the reader of the
        ///     two result sets, which stores what it reads.
        /// </summary>
        /// <param name="batch">The marked batch.</param>
        /// <returns>The absorption: the prologue appends <see cref="SettingsColdLoad.Text" /> under its condition and reads the sets through it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="batch" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The batch carries no connect prologue.</exception>
        public SettingsAbsorption Absorb(DataBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (!batch.CarriesConnectPrologue)
            {
                throw new InvalidOperationException("Only a batch marked with the connect prologue absorbs the settings load.");
            }

            // --- Wanted when the store holds no current entry at composition, or the store itself is loading ---
            int tenantId = batch.Connection.TenantId;
            bool requested = store.TryTakeRequest(batch, out SettingsLoadRequest request);
            bool load = requested || !store.IsCurrent(tenantId, snapshots.Current(tenantId)[VersionTagNames.Settings]);
            ScalarParameter parameter = new(ReservedSqlNames.LoadSettings, SqlDbType.Bit, load);

            // --- The sets complete the store's own load, or are offered to the store ---
            return new SettingsAbsorption(load, parameter, ordinal => Reader(batch, ordinal, (cursor, entry, tag) =>
            {
                if (requested)
                {
                    cursor.Settle(request.Result, () => (entry, tag));
                }
                else
                {
                    cursor.Settle(new BatchResult<bool>(), () => Offer(tenantId, entry, tag));
                }
            }));
        }

        /// <summary>Offers what an absorbed load read to the store.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="entry">The entry.</param>
        /// <param name="tag">The tag.</param>
        /// <returns>True, the settled result's value.</returns>
        private bool Offer(int tenantId, TenantSettingsEntry entry, VersionTag tag)
        {
            store.Offer(tenantId, entry, tag);
            return true;
        }

        /// <summary>The reader of the cold load's two result sets: they are read, the entry built under the prelude's tag, and handed on.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="ordinal">The ordinal of the statement that carries the sets.</param>
        /// <param name="settle">Stages what was read for when the round trip's results complete.</param>
        /// <returns>The reader.</returns>
        private StatementReader Reader(DataBatch batch, int ordinal, Action<ResultSetCursor, TenantSettingsEntry, VersionTag> settle)
        {
            return async (cursor, cancellationToken) =>
            {
                SettingsRows rows = await SettingsColdLoad.ReadAsync(cursor, ordinal, cancellationToken).ConfigureAwait(false);

                // The prelude's tag, read earlier in the same round trip; none when the database has no row for it.
                int tenantId = batch.Connection.TenantId;
                VersionTag tag = batch.ReadVersionTags is VersionTagSnapshot read && read.TenantId == tenantId
                    ? read[VersionTagNames.Settings]
                    : VersionTag.None;

                // The tenant is asserted on every load: a mismatch fails the batch here, closed.
                TenantSettingsEntry entry = store.Build(tenantId, accessor.Current.IsSystem, tag, rows);
                settle(cursor, entry, tag);
            };
        }
    }

    /// <summary>The settings cold load as the connect prologue absorbs it into a batch it marks.</summary>
    /// <param name="Load">Whether the load is wanted: the store holds no current entry, or is loading.</param>
    /// <param name="Parameter">The parameter the prologue binds, <c>@tm_LoadSettings</c>, set to <paramref name="Load" />.</param>
    /// <param name="CreateReader">Creates the reader of the two result sets, given the ordinal of the prologue's statement that carries them.</param>
    internal sealed record SettingsAbsorption(bool Load, BatchParameter Parameter, Func<int, StatementReader> CreateReader);
}
