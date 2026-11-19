// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     The ceilings of an entity's queries the client respects before it asks: the stack's
    ///     <see cref="StackLimits" />, and the longest filter expression the query pipeline accepts.
    /// </summary>
    /// <param name="MaxTake">The most rows one page returns.</param>
    /// <param name="CountCap">The count a query stops counting at.</param>
    /// <param name="MaxSkipWindow">The furthest row a page may start at.</param>
    /// <param name="MaxSearchLength">The longest search text.</param>
    /// <param name="MaxIds">The most ids one request names.</param>
    /// <param name="MaxExtras">The most details extras one read asks for.</param>
    /// <param name="MaxFilterLength">The longest filter expression, in characters.</param>
    public sealed record ClientEntityLimits(
        int MaxTake,
        int CountCap,
        int MaxSkipWindow,
        int MaxSearchLength,
        int MaxIds,
        int MaxExtras,
        int MaxFilterLength);
}
