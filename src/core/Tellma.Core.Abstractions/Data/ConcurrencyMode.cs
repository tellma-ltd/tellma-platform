// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     Whether a save compares each updated row's stamp with the one the client read. Defined once,
    ///     here, for every layer that saves.
    /// </summary>
    public enum ConcurrencyMode
    {
        /// <summary>
        ///     The default: a row whose stored stamp differs from the supplied one is a conflict.
        /// </summary>
        Check,

        /// <summary>
        ///     The stamp comparison is skipped, so the save overwrites whatever is stored. The
        ///     existence check still runs: a deleted row is never resurrected.
        /// </summary>
        Override,
    }
}
