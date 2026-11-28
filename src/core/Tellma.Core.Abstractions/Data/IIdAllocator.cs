// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     Allocates the ids of new rows from each table's sequence, through a per-tenant buffer of
    ///     ranges the process reserved ahead. A singleton.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A row is new while its <c>Id</c> is zero or negative; a negative id is a temporary id that
    ///         other rows of the same payload may reference. Ids are assigned before validators run, so
    ///         validators see final ids and cross-references.
    ///     </para>
    ///     <para>
    ///         Ranges the buffer does not hold are reserved by a statement that rides a batch going to
    ///         the database anyway, and every batch that writes a table the buffer serves may top the
    ///         buffer up, so the common create pays no round trip for its ids.
    ///     </para>
    /// </remarks>
    public interface IIdAllocator
    {
        /// <summary>
        ///     Reserves an id for every new row of a payload: the rows with an <c>Id</c> of zero or less,
        ///     roots and the rows of their supplied child collections, recursively. What the buffer holds
        ///     is taken at once; for every table still short, one statement is appended to the batch.
        /// </summary>
        /// <param name="batch">The batch the reservation statements ride, while it is composing.</param>
        /// <param name="entityType">The rows' entity type.</param>
        /// <param name="rows">The rows, with their child collections populated or null.</param>
        /// <returns>
        ///     The reservation, which assigns the ids once they are reserved and returns them when it is
        ///     disposed before any persist attempt.
        /// </returns>
        /// <exception cref="ArgumentException">
        ///     The batch is not one the platform created, a row is null or of another type, or two new
        ///     rows of one type share a temporary id.
        /// </exception>
        /// <exception cref="InvalidOperationException">A table holding new rows has no sequence.</exception>
        IdReservation Reserve(IDataBatch batch, Type entityType, IReadOnlyList<object> rows);

        /// <summary>
        ///     Takes ids for rows the caller inserts itself, outside any save: from the buffer, or with one
        ///     autocommit round trip of the database when the buffer runs short. The ids are never
        ///     returned.
        /// </summary>
        /// <param name="database">The tenant database whose sequence serves the ids.</param>
        /// <param name="entityType">The entity type whose table the ids are for.</param>
        /// <param name="count">How many ids to take; at least one.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The ids, ascending within each range taken, each an <see cref="int" /> or a <see cref="long" /> like the key.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The count is less than one.</exception>
        /// <exception cref="InvalidOperationException">The entity's table has no sequence.</exception>
        Task<IReadOnlyList<object>> TakeAsync(ITenantDatabase database, Type entityType, int count, CancellationToken cancellationToken = default);
    }
}
