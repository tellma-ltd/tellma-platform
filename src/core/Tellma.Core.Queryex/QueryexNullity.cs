// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     Whether an expression can evaluate to an absent value.
    /// </summary>
    /// <remarks>
    ///     <see cref="Nullable" /> is always a safe answer; <see cref="NotNull" /> is a claim the
    ///     emitter relies on to omit guards, so the analysis returns it only where presence is
    ///     provable for every database state the schema permits.
    /// </remarks>
    public enum QueryexNullity
    {
        /// <summary>Provably present for every database state the schema permits.</summary>
        NotNull,

        /// <summary>May be absent.</summary>
        Nullable,

        /// <summary>Provably absent.</summary>
        Null,
    }
}
