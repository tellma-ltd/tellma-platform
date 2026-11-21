// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Batches
{
    // Spec 0013 §2.5: the bump epilogue fills each user-level tag's users from the rows every
    // platform statement wrote, its deleted rows included. A save, an update and a delete each
    // capture, for every table they write with a user-level rule, the rule's user-id column of each
    // row they insert, update or delete, before and after the write, into one statement-local table.
    /// <summary>
    ///     The statement-local table in which a platform write captures the users its rows name, for
    ///     the user-level version tags: one per written table with a user-level rule, holding each
    ///     written row's id and, per user-id column, its value before and after the write.
    /// </summary>
    internal static class UserIdCapture
    {
        /// <summary>The capture's column holding a written row's id.</summary>
        public const string RowId = "RowId";

        /// <summary>
        ///     The user-id columns of a table's user-level rules, distinct and in ordinal order; empty when
        ///     no rule names the table or no registry is composed.
        /// </summary>
        /// <param name="registry">The version tag registry, or null when none is composed.</param>
        /// <param name="table">The table.</param>
        /// <returns>The columns.</returns>
        public static IReadOnlyList<string> Columns(IVersionTagRegistry? registry, TableName table)
        {
            return registry is null
                ? []
                : [.. registry.Resolve(new HashSet<TableName> { table }).UserRules
                    .Where(rule => rule.Table == table)
                    .Select(static rule => rule.UserIdColumn)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)];
        }

        /// <summary>The capture's statement-local word, <c>uid_&lt;Schema&gt;_&lt;Table&gt;</c>.</summary>
        /// <param name="table">The written table.</param>
        /// <returns>The word.</returns>
        public static string Word(TableName table)
        {
            ArgumentNullException.ThrowIfNull(table);
            return $"uid_{table.Schema}_{table.Name}";
        }

        /// <summary>
        ///     The statement-local word of a delete's capture of the users named by a table's rows that
        ///     a key's <c>ON DELETE</c> action reaches, <c>uidk_&lt;Schema&gt;_&lt;Table&gt;</c>.
        /// </summary>
        /// <param name="table">The reached table.</param>
        /// <returns>The word.</returns>
        public static string ConsequenceWord(TableName table)
        {
            ArgumentNullException.ThrowIfNull(table);
            return $"uidk_{table.Schema}_{table.Name}";
        }

        /// <summary>The capture's column holding a user-id column's value before the write.</summary>
        /// <param name="column">The user-id column.</param>
        /// <returns>The capture's column.</returns>
        public static string Old(string column)
        {
            return "Old_" + column;
        }

        /// <summary>The capture's column holding a user-id column's value after the write.</summary>
        /// <param name="column">The user-id column.</param>
        /// <returns>The capture's column.</returns>
        public static string New(string column)
        {
            return "New_" + column;
        }

        /// <summary>The capture's declaration.</summary>
        /// <param name="name">The capture's batch-local name.</param>
        /// <param name="keyType">The SQL type of the written table's key.</param>
        /// <param name="columns">The user-id columns.</param>
        /// <returns>The <c>DECLARE</c> statement, terminated.</returns>
        public static string Declaration(string name, string keyType, IReadOnlyList<string> columns)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(columns);
            return $"DECLARE {name} TABLE ({SqlNames.Quote(RowId)} {keyType} NOT NULL{string.Concat(columns.Select(static column => $", {SqlNames.Quote(Old(column))} int NULL, {SqlNames.Quote(New(column))} int NULL"))});";
        }

        /// <summary>
        ///     The declaration of a capture that keeps no row id, for rows whose key the capturing
        ///     statement does not write: only each user-id column's before and after columns.
        /// </summary>
        /// <param name="name">The capture's batch-local name.</param>
        /// <param name="columns">The user-id columns.</param>
        /// <returns>The <c>DECLARE</c> statement, terminated.</returns>
        public static string ImageDeclaration(string name, IReadOnlyList<string> columns)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentNullException.ThrowIfNull(columns);
            return $"DECLARE {name} TABLE ({string.Join(", ", columns.Select(static column => $"{SqlNames.Quote(Old(column))} int NULL, {SqlNames.Quote(New(column))} int NULL"))});";
        }

        /// <summary>
        ///     The capture's target columns, in the order <see cref="Values" /> lists the values: the
        ///     row id, then each user-id column's before and after columns.
        /// </summary>
        /// <param name="columns">The user-id columns.</param>
        /// <returns>The column list, bracket-quoted and comma-separated.</returns>
        public static string Targets(IReadOnlyList<string> columns)
        {
            ArgumentNullException.ThrowIfNull(columns);
            return string.Join(", ", columns.SelectMany(static column => new[] { SqlNames.Quote(Old(column)), SqlNames.Quote(New(column)) }).Prepend(SqlNames.Quote(RowId)));
        }

        /// <summary>The values an <c>OUTPUT</c> clause writes into the capture for one written row.</summary>
        /// <param name="id">The written row's id, such as <c>inserted.[Id]</c>.</param>
        /// <param name="before">The alias of the row before the write, <c>deleted</c>; null for an insert.</param>
        /// <param name="after">The alias of the row after the write, <c>inserted</c>; null for a delete.</param>
        /// <param name="columns">The user-id columns.</param>
        /// <returns>The value list, comma-separated.</returns>
        public static string Values(string id, string? before, string? after, IReadOnlyList<string> columns)
        {
            ArgumentException.ThrowIfNullOrEmpty(id);
            ArgumentNullException.ThrowIfNull(columns);
            return string.Join(", ", columns.SelectMany(column => new[] { Image(before, column), Image(after, column) }).Prepend(id));
        }

        /// <summary>The sources a capture declares: one per user-id column, reading its before and after values.</summary>
        /// <param name="table">The written table.</param>
        /// <param name="columns">The user-id columns.</param>
        /// <param name="name">The capture's batch-local name.</param>
        /// <returns>The sources.</returns>
        public static IEnumerable<UserIdSource> Sources(TableName table, IReadOnlyList<string> columns, string name)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(columns);
            HashSet<TableName> tables = [table];
            return columns.Select(column => new UserIdSource(tables, column, new SqlIdentifier(name), [Old(column), New(column)]));
        }

        /// <summary>One image of a user-id column: the alias's column, or <c>NULL</c> when the write has no such image.</summary>
        /// <param name="alias">The alias, or null.</param>
        /// <param name="column">The column.</param>
        /// <returns>The value.</returns>
        private static string Image(string? alias, string column)
        {
            return alias is null ? "NULL" : $"{alias}.{SqlNames.Quote(column)}";
        }
    }
}
