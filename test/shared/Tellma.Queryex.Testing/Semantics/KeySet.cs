// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using Tellma.Core.Queryex;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>
    ///     A table of keys a host supplies to restrict a query, as both implementations see it.
    /// </summary>
    /// <remarks>
    ///     One declaration, read two ways, like the fixture's own rows: the second reading filters
    ///     by the keys in memory, and a run on a server declares the same keys as a table variable
    ///     ahead of the statement that reads it.
    /// </remarks>
    /// <param name="KeyType">The type of the table's key column.</param>
    /// <param name="Keys">The keys it holds.</param>
    public sealed record KeySet(QueryexStoreType KeyType, IReadOnlyList<QxValue> Keys)
    {
        /// <summary>Whether a value is one of the keys.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it is present and equal to a key.</returns>
        /// <remarks>
        ///     An absent value is never one of them, whatever the table holds: that is what a
        ///     restriction promises, and what keeps it from ever matching a row with no key.
        /// </remarks>
        public bool Holds(QxValue value)
        {
            ArgumentNullException.ThrowIfNull(value);

            return !value.IsAbsent
                && Keys.Any(key => !key.IsAbsent && Interpreter.Order(key, value) == 0);
        }

        /// <summary>The statements that declare the table and fill it.</summary>
        /// <param name="name">The table variable's name, its marker included.</param>
        /// <returns>The statements, ending in a line feed.</returns>
        public string Declaration(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            StringBuilder script = new();
            script.Append("DECLARE ").Append(name).Append(" TABLE ([Id] ")
                .Append(LedgerDdl.TypeName(KeyType)).Append(");\n");

            if (Keys.Count > 0)
            {
                script.Append("INSERT INTO ").Append(name).Append(" ([Id]) VALUES ")
                    .Append(string.Join(", ", Keys.Select(key => "(" + LedgerData.Literal(key) + ")")))
                    .Append(";\n");
            }

            return script.ToString();
        }
    }
}
