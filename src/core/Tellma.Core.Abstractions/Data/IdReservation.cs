// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The ids <see cref="IIdAllocator.Reserve" /> holds for one payload's new rows, until it
    ///     assigns them and, when no persist was attempted, until it gives them back.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Dispose the reservation when the request ends. Disposed before any batch that saves the
    ///         payload executes (a validation failure), it returns every id it took to the buffer: nothing
    ///         outside the process saw them. Once a batch saving the payload executed, whatever its
    ///         outcome, nothing returns.
    ///     </para>
    /// </remarks>
    public sealed class IdReservation : IDisposable
    {
        /// <summary>Creates the reservation over the allocator's hold.</summary>
        /// <param name="hold">The hold that implements it.</param>
        internal IdReservation(IIdReservationHold hold)
        {
            Hold = hold ?? throw new ArgumentNullException(nameof(hold));
        }

        /// <summary>
        ///     Whether the buffer covered every new row, so <see cref="Assign" /> may run at once;
        ///     otherwise it runs after the batch given to <see cref="IIdAllocator.Reserve" /> executed.
        /// </summary>
        public bool IsImmediate => Hold.IsImmediate;

        /// <summary>The allocator's hold behind the reservation.</summary>
        internal IIdReservationHold Hold { get; }

        /// <summary>
        ///     Assigns an id to every new row of the payload, roots first and then each child collection
        ///     in metadata order, recursively; sets every child's parent key to its parent's id; and
        ///     rewrites every temporary id the payload defines wherever a reference to its entity type
        ///     holds it. A temporary id no row defines is left as it is, for the caller to report.
        /// </summary>
        /// <param name="rows">The payload <see cref="IIdAllocator.Reserve" /> was given, unchanged in shape.</param>
        /// <exception cref="InvalidOperationException">
        ///     The ids are not reserved yet, the reservation round trip failed, the payload holds another
        ///     number of new rows than was reserved, or the ids were assigned already.
        /// </exception>
        /// <exception cref="ObjectDisposedException">The reservation was disposed.</exception>
        public void Assign(IReadOnlyList<object> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            Hold.Assign(rows);
        }

        /// <summary>Gives the ids back to the buffer when no batch saving the payload executed; otherwise releases nothing.</summary>
        public void Dispose()
        {
            Hold.Dispose();
        }
    }

    /// <summary>What implements an <see cref="IdReservation" />: the allocator's hold on the reserved ranges.</summary>
    internal interface IIdReservationHold
    {
        /// <summary>Whether the buffer covered every new row.</summary>
        bool IsImmediate { get; }

        /// <summary>Assigns the ids; see <see cref="IdReservation.Assign" />.</summary>
        /// <param name="rows">The payload.</param>
        void Assign(IReadOnlyList<object> rows);

        /// <summary>Releases the hold; see <see cref="IdReservation.Dispose" />.</summary>
        void Dispose();
    }
}
