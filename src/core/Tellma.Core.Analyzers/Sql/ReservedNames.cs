// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Analyzers.Sql
{
    /// <summary>
    ///     The reserved parameter and table-variable namespaces of a batch's text, the same rule as
    ///     <c>ReservedSqlNames.IsReserved</c> in the contract (which this netstandard2.0 assembly cannot
    ///     reference; the analyzer suite pins the two against each other): a name beginning with
    ///     <c>@tm_</c>, or with <c>@qx</c> or <c>@tb</c> followed by one or more digits and <c>_</c>,
    ///     ignoring case. <c>@tmp</c>, <c>@tbl</c> and <c>@qxRate</c> stay free.
    /// </summary>
    internal static class ReservedNames
    {
        /// <summary>The once-per-batch prefix.</summary>
        private const string BatchPrefix = "@tm_";

        /// <summary>The query engine's prefix, reserved with an ordinal after it.</summary>
        private const string EnginePrefix = "@qx";

        /// <summary>The data layer's per-statement prefix, reserved with an ordinal after it.</summary>
        private const string StatementPrefix = "@tb";

        /// <summary>Whether a name is in a reserved namespace.</summary>
        /// <param name="name">The name, with its leading <c>@</c>.</param>
        /// <returns>True when it is.</returns>
        public static bool IsReserved(string name)
        {
            return name.StartsWith(BatchPrefix, StringComparison.OrdinalIgnoreCase)
                || HasOrdinalPrefix(name, EnginePrefix)
                || HasOrdinalPrefix(name, StatementPrefix);
        }

        /// <summary>Whether a name is <paramref name="prefix" />, one or more ASCII digits and <c>_</c>, then anything.</summary>
        /// <param name="name">The name.</param>
        /// <param name="prefix">The prefix.</param>
        /// <returns>True when it is.</returns>
        private static bool HasOrdinalPrefix(string name, string prefix)
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int index = prefix.Length;
            while (index < name.Length && name[index] is >= '0' and <= '9')
            {
                index++;
            }

            return index > prefix.Length && index < name.Length && name[index] == '_';
        }
    }
}
