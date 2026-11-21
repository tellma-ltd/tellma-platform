// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     One row a save under the <c>Check</c> concurrency mode found changed since the caller
    ///     loaded it, with who changed it, so a client can offer "Sara changed this record;
    ///     overwrite?" and resend under <c>Override</c>.
    /// </summary>
    /// <param name="Id">
    ///     The row's id. Display data on the wire, a client never matching it to loaded rows: a JSON
    ///     number within ±(2^53 − 1), else a string (<see cref="DisplayIds" />).
    /// </param>
    /// <param name="ModifiedAt">The stored stamp as the opaque string the client echoes back; null when the row is missing.</param>
    /// <param name="ModifiedById">The user who last modified the row; null when the row is missing.</param>
    /// <param name="ModifiedByName">That user's name in the request's language; null when the row is missing.</param>
    /// <param name="IsMissing">
    ///     True for a row deleted under the caller. A save of entities reports it only beside a row
    ///     that conflicts by stamp: when every row is missing, the outcome is a not-found instead. A
    ///     settings save reports it on its own, since the editor read the entry it expects; its id is
    ///     then 0 when the entry's row was gone before the save read it. A settings save also reports
    ///     id 0 with this false and no stamp or modifier when another save inserted the entry first:
    ///     the row exists, but its stamp and author are not read once the save rolled back.
    /// </param>
    public sealed record ConcurrencyConflict(
        [property: JsonConverter(typeof(DisplayIdJsonConverter))] long Id, string? ModifiedAt, int? ModifiedById, string? ModifiedByName, bool IsMissing);
}
