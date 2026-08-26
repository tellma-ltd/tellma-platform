// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     Resource ceilings for one compilation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Configurable per call site: an interactive filter box and a stored report definition
    ///         warrant different ceilings. Every ceiling is enforced before emission, because
    ///         bounded input bounds the output: guard operands are kept atomic, so the emitted SQL
    ///         grows linearly with the number of bound nodes.
    ///     </para>
    ///     <para>
    ///         The profile participates in every cache key, so a text admitted under a generous
    ///         call site can never satisfy a stricter one from cache.
    ///     </para>
    /// </remarks>
    public sealed record QueryexLimits
    {
        /// <summary>The default ceilings.</summary>
        public static QueryexLimits Default { get; } = new QueryexLimits();

        /// <summary>Maximum characters in one expression text. Default 8192.</summary>
        public int MaxInputLength { get; init; } = 8192;

        /// <summary>Maximum tokens in one expression text. Default 4096.</summary>
        public int MaxTokens { get; init; } = 4096;

        /// <summary>Maximum nesting depth of a parsed expression. Default 64.</summary>
        public int MaxSyntaxDepth { get; init; } = 64;

        /// <summary>Maximum bound nodes across a whole query. Default 4096.</summary>
        public int MaxTypedNodes { get; init; } = 4096;

        /// <summary>Maximum items in one comma-separated expression list. Default 128.</summary>
        public int MaxListItems { get; init; } = 128;

        /// <summary>Maximum joins in a whole query, counted after pruning. Default 32.</summary>
        public int MaxJoins { get; init; } = 32;

        /// <summary>
        ///     Maximum parameter slots in a whole query, counted after deduplication and pruning.
        ///     Default 512.
        /// </summary>
        public int MaxParameters { get; init; } = 512;
    }
}
