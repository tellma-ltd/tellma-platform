// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     Facts about the Queryex language this engine implements.
    /// </summary>
    /// <remarks>
    ///     Queryex text is persisted in configuration — report definitions, saved filters, access
    ///     criteria — and recompiled long after it was written, so a host is expected to store the
    ///     version each stored expression was validated under.
    /// </remarks>
    public static class QueryexLanguage
    {
        /// <summary>
        ///     The language version. Incremented only by a change that alters the meaning of some
        ///     currently-valid expression; additive changes, such as a new function or a new
        ///     calendar code, do not move it.
        /// </summary>
        public const int Version = 1;
    }
}
