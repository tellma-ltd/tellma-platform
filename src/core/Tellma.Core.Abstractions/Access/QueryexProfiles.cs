// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The Queryex limit profiles access relies on: one for validating a single stored filter, and
    ///     one for every pipeline compile, which must admit the disjunction a caller's grants compose.
    /// </summary>
    public static class QueryexProfiles
    {
        /// <summary>
        ///     The profile a stored permission filter is validated under: at most 2048 characters (the
        ///     column's width), 8 joins, 64 parameters and 512 typed nodes; every other limit at the
        ///     engine's default.
        /// </summary>
        public static QueryexLimits AccessLimits { get; } = QueryexLimits.Default with
        {
            MaxInputLength = AccessOptions.FilterColumnLength,
            MaxJoins = 8,
            MaxParameters = 64,
            MaxTypedNodes = 512,
        };

        /// <summary>
        ///     The profile of every pipeline compile: 1024 parameters and 64 joins, the floor the
        ///     composed permission disjunction needs, and 512 list items, as a report's run admits, so
        ///     a filter a report runs also runs on its drill-through list; every other limit at the
        ///     engine's default.
        /// </summary>
        public static QueryexLimits PipelineLimits { get; } = QueryexLimits.Default with
        {
            MaxParameters = 1024,
            MaxJoins = 64,
            MaxListItems = 512,
        };
    }
}
