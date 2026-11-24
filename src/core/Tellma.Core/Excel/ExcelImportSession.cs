// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     A parsed import workbook, ready to save in chunks: its parent rows in the order their
    ///     self-references need, and for any range of them the lookups to read, the stored rows to
    ///     read, and the entities to save. Holds no connection and reads nothing: every database
    ///     answer is handed in, so a range resolves the same way each time it is given the same rows.
    /// </summary>
    /// <typeparam name="TEntity">The stack's leaf.</typeparam>
    public sealed class ExcelImportSession<TEntity>
        where TEntity : class
    {
        /// <summary>Wraps a parsed import.</summary>
        /// <param name="core">The parsed import.</param>
        internal ExcelImportSession(ImportSession core)
        {
            Core = core ?? throw new ArgumentNullException(nameof(core));
        }

        /// <summary>The parent rows: the positions of the import's order.</summary>
        public int RowCount => Core.Order.Count;

        /// <summary>The Excel row of each position.</summary>
        public IReadOnlyList<int> SheetRows => Core.SheetRows;

        /// <summary>
        ///     The fingerprint of the order and of the columns the import reads, with their sheets,
        ///     letters, paths and languages; a resumed import compares it to tell a changed file or mapping.
        /// </summary>
        public string OrderHash => Core.OrderHash;

        /// <summary>The parsed import.</summary>
        internal ImportSession Core { get; }

        /// <summary>
        ///     The lookups of the positions <paramref name="fromRow" /> to <paramref name="toRow" />,
        ///     read together in one round trip through the stack's row source.
        /// </summary>
        /// <param name="fromRow">The first position, zero-based.</param>
        /// <param name="toRow">The last position.</param>
        /// <returns>The queries; empty when the range looks nothing up.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The range is outside the positions.</exception>
        public IReadOnlyList<ExcelQuery> LookupsFor(int fromRow, int toRow)
        {
            return Core.LookupsFor(fromRow, toRow);
        }

        /// <summary>
        ///     The ids of the stored rows an update or upsert of the range overlays, read with their
        ///     collections at every depth before it resolves.
        /// </summary>
        /// <param name="fromRow">The first position.</param>
        /// <param name="toRow">The last position.</param>
        /// <param name="lookupRows">Each of the range's lookups with the rows it read.</param>
        /// <returns>The ids, each a <see cref="long" />; empty for an insert.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The range is outside the positions.</exception>
        public IReadOnlyList<object> HydrationIds(int fromRow, int toRow, IReadOnlyDictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows)
        {
            return [.. Core.HydrationIds(fromRow, toRow, lookupRows).Select(static id => (object)id)];
        }

        /// <summary>Resolves the range into the entities its save takes, with the cell of each payload path and the errors found.</summary>
        /// <param name="fromRow">The first position.</param>
        /// <param name="toRow">The last position.</param>
        /// <param name="lookupRows">Each of the range's lookups with the rows it read.</param>
        /// <param name="hydrated">The stored rows of the range's hydration ids.</param>
        /// <param name="committedNewIds">The ids an earlier chunk's new rows were given, by their negative sheet identity.</param>
        /// <returns>The chunk.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The range is outside the positions.</exception>
        public ExcelChunk<TEntity> Resolve(
            int fromRow,
            int toRow,
            IReadOnlyDictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> lookupRows,
            IReadOnlyList<TEntity> hydrated,
            IReadOnlyDictionary<long, long> committedNewIds)
        {
            ArgumentNullException.ThrowIfNull(hydrated);
            ImportChunk chunk = Core.Resolve(fromRow, toRow, lookupRows, hydrated, committedNewIds);
            return new ExcelChunk<TEntity>([.. chunk.Entities.Cast<TEntity>()], chunk.CoordinateMap, chunk.Errors) { Facts = chunk.Facts };
        }

        /// <summary>
        ///     For each new row of a saved range that a later row names by its negative sheet
        ///     identity, that identity mapped to the id the save gave the row.
        /// </summary>
        /// <param name="fromRow">The first position.</param>
        /// <param name="toRow">The last position.</param>
        /// <param name="chunk">The range's chunk, after its save.</param>
        /// <returns>The map.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The range is outside the positions.</exception>
        public IReadOnlyDictionary<long, long> LaterReferencedIds(int fromRow, int toRow, ExcelChunk<TEntity> chunk)
        {
            ArgumentNullException.ThrowIfNull(chunk);
            return Core.LaterReferencedIds(fromRow, toRow, chunk.Entities);
        }

        /// <summary>The negative sheet identities that rows at <paramref name="fromRow" /> or later name.</summary>
        /// <param name="fromRow">The first position.</param>
        /// <returns>The identities.</returns>
        public IReadOnlySet<long> ReferencedFrom(int fromRow)
        {
            return Core.ReferencedFrom(fromRow);
        }
    }
}
