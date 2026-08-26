// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Tellma.Core.Queryex.Functions
{
    /// <summary>
    ///     Every function, looked up by name.
    /// </summary>
    /// <remarks>
    ///     Built once into a frozen lookup and shared by every compilation on every thread. Nothing
    ///     here is mutable, so there is no lock on the path a query takes through it.
    /// </remarks>
    internal static class FunctionRegistry
    {
        /// <summary>The definitions, by name, matched case-insensitively.</summary>
        private static readonly FrozenDictionary<string, FunctionDefinition> Definitions =
            FunctionLibrary.All().ToFrozenDictionary(
                static definition => definition.Name,
                StringComparer.OrdinalIgnoreCase);

        /// <summary>Every definition, in the order the library declares them.</summary>
        internal static ImmutableArray<FunctionDefinition> All { get; } = [.. FunctionLibrary.All()];

        /// <summary>Finds a function by name.</summary>
        /// <param name="name">The name as written.</param>
        /// <returns>The definition, or null when no function goes by that name.</returns>
        internal static FunctionDefinition? Find(string name)
        {
            return Definitions.GetValueOrDefault(name);
        }

        /// <summary>Whether a name belongs to an aggregation.</summary>
        /// <param name="name">The name as written.</param>
        /// <returns>True when it does.</returns>
        /// <remarks>
        ///     Answerable without a schema and without binding, which is what lets the grouping-key
        ///     fact a whole query needs be read off the parse of its select list.
        /// </remarks>
        internal static bool IsAggregate(string name)
        {
            return Find(name) is { Category: FunctionCategory.Aggregate };
        }
    }
}
