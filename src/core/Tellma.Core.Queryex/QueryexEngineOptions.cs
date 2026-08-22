// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     Engine-wide options.
    /// </summary>
    /// <remarks>
    ///     Every cache is bounded, and deliberately so: the engine accepts untrusted input, and an
    ///     uncapped text-keyed cache is a memory-exhaustion vector. An attacker feeding distinct
    ///     texts must age entries out rather than grow memory without bound.
    /// </remarks>
    public sealed record QueryexEngineOptions
    {
        /// <summary>Maximum cached parse results, keyed by text. Default 4096.</summary>
        public int MaxCachedSyntaxTrees { get; init; } = 4096;

        /// <summary>Maximum cached bound expressions. Default 4096.</summary>
        public int MaxCachedBoundExpressions { get; init; } = 4096;

        /// <summary>Maximum cached compiled queries. Default 1024.</summary>
        public int MaxCachedQueryTemplates { get; init; } = 1024;
    }
}
