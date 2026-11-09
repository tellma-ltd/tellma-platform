// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The parameter and table-variable names the platform reserves in the SQL text of a batch.
    ///     Raw SQL written outside the platform may declare none of them: it names its own values
    ///     through the holes of an interpolated statement, which the executor renames into the
    ///     platform's per-statement namespace.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Three prefixes are reserved, each compared ignoring case because SQL Server matches
    ///         variable names that way:
    ///     </para>
    ///     <list type="bullet">
    ///         <item><description>
    ///             <see cref="EnginePrefix" />, the query engine's names: <c>@qx{b}_p{n}</c> parameters,
    ///             <c>@qx{b}_v{n}</c> values and <c>@qx{b}_skip</c>/<c>@qx{b}_take</c>.
    ///         </description></item>
    ///         <item><description>
    ///             <see cref="StatementPrefix" />, the data layer's per-statement names, formed by
    ///             <see cref="Statement(int, string)" /> from the statement's batch ordinal and one of
    ///             the names of <see cref="StatementNames" />.
    ///         </description></item>
    ///         <item><description>
    ///             <see cref="BatchPrefix" />, the once-per-batch names of the schema guard, the connect
    ///             prologue, the tag guard and the tag bumps, such as <see cref="SchemaFingerprint" />.
    ///         </description></item>
    ///     </list>
    ///     <para><c>{b}</c> is always the statement's batch ordinal.</para>
    /// </remarks>
    public static class ReservedSqlNames
    {
        /// <summary>The prefix of the query engine's names, <c>@qx</c>.</summary>
        public const string EnginePrefix = "@qx";

        /// <summary>The prefix of the data layer's per-statement names, <c>@tb</c>.</summary>
        public const string StatementPrefix = "@tb";

        /// <summary>The prefix of the once-per-batch names, <c>@tm_</c>.</summary>
        public const string BatchPrefix = "@tm_";

        /// <summary>The running model's storage fingerprint, bound once per batch for the schema guard.</summary>
        public const string SchemaFingerprint = "@tm_schema";

        /// <summary>The calling user's id, bound by the connect prologue.</summary>
        public const string UserId = "@tm_UserId";

        /// <summary>The connect prologue's verdict, which guards the batch body.</summary>
        public const string Guard = "@tm_Guard";

        /// <summary>The version tags a persist batch expects, checked by the tag guard.</summary>
        public const string ExpectedTags = "@tm_expectedTags";

        /// <summary>The new version tag a batch's bumps write.</summary>
        public const string Tag = "@tm_tag";

        /// <summary>The tenant-level tag names a batch bumps.</summary>
        public const string TagNames = "@tm_tagNames";

        /// <summary>The ids of the callers a batch acts for.</summary>
        public const string CallerIds = "@tm_callerIds";

        /// <summary>The prefix of the per-column user-id lists the user-level tag bumps read, <c>@tm_userIds_&lt;Column&gt;</c>.</summary>
        public const string UserIdsPrefix = "@tm_userIds_";

        /// <summary>The rows the connect prologue found visible to the caller.</summary>
        public const string Visible = "@tm_visible";

        /// <summary>
        ///     The three reserved prefixes. <see cref="EnginePrefix" /> and <see cref="StatementPrefix" />
        ///     reserve a name only when a batch ordinal and <c>_</c> follow them (<c>@qx0_</c>,
        ///     <c>@tb12_</c>); <see cref="BatchPrefix" /> reserves every name it begins.
        /// </summary>
        public static IReadOnlyList<string> Prefixes { get; } = [EnginePrefix, StatementPrefix, BatchPrefix];

        /// <summary>
        ///     Whether a parameter or table-variable name falls in a reserved namespace, ignoring case:
        ///     it begins with <c>@tm_</c>, or with <c>@qx</c> or <c>@tb</c> followed by one or more
        ///     digits and <c>_</c>. Names such as <c>@tmp</c>, <c>@tbl</c> or <c>@qxRate</c> stay free.
        /// </summary>
        /// <param name="name">The name, with its leading <c>@</c>.</param>
        /// <returns>True when the name is in a reserved namespace.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name" /> is null.</exception>
        public static bool IsReserved(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            return name.StartsWith(BatchPrefix, StringComparison.OrdinalIgnoreCase)
                || HasOrdinalPrefix(name, EnginePrefix)
                || HasOrdinalPrefix(name, StatementPrefix);
        }

        /// <summary>Forms a per-statement name, <c>@tb{b}_{name}</c>.</summary>
        /// <param name="batchOrdinal">The statement's batch ordinal, zero or more.</param>
        /// <param name="name">The name within the statement, usually one of <see cref="StatementNames" />.</param>
        /// <returns>The reserved name.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="batchOrdinal" /> is negative.</exception>
        /// <exception cref="ArgumentException"><paramref name="name" /> is null or empty.</exception>
        public static string Statement(int batchOrdinal, string name)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(batchOrdinal);
            ArgumentException.ThrowIfNullOrEmpty(name);

            return string.Create(CultureInfo.InvariantCulture, $"{StatementPrefix}{batchOrdinal}_{name}");
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
            while (index < name.Length && char.IsAsciiDigit(name[index]))
            {
                index++;
            }

            return index > prefix.Length && index < name.Length && name[index] == '_';
        }

        /// <summary>
        ///     The names within one statement's <c>@tb{b}_</c> namespace that the data layer and its
        ///     sibling specifications' fixed statements declare.
        /// </summary>
        public static class StatementNames
        {
            /// <summary>The statement's clock, read once from <c>SYSUTCDATETIME()</c>.</summary>
            public const string Now = "now";

            /// <summary>The keys a statement addresses.</summary>
            public const string Keys = "keys";

            /// <summary>The ids a statement addresses.</summary>
            public const string Ids = "ids";

            /// <summary>The rows a save inserts.</summary>
            public const string New = "new";

            /// <summary>The rows a save wrote.</summary>
            public const string Saved = "saved";

            /// <summary>The parents a child write touched.</summary>
            public const string Touched = "touched";

            /// <summary>The rows whose concurrency stamp did not match.</summary>
            public const string Conflicts = "conflicts";

            /// <summary>The message a statement throws.</summary>
            public const string Message = "msg";

            /// <summary>The rows an update or a delete affected.</summary>
            public const string Affected = "aff";

            /// <summary>The before images of the rows a statement changes.</summary>
            public const string Old = "old";

            /// <summary>The node paths a tree statement computes.</summary>
            public const string Paths = "paths";

            /// <summary>The ancestors a tree statement reads.</summary>
            public const string Ancestors = "anc";

            /// <summary>The first id of a reserved range.</summary>
            public const string First = "first";

            /// <summary>The last id of a reserved range.</summary>
            public const string Last = "last";

            /// <summary>The jobs a claim statement claimed.</summary>
            public const string Claimed = "claimed";

            /// <summary>The schedules a scheduler statement found due.</summary>
            public const string Due = "due";

            /// <summary>The schedules a scheduler statement fired.</summary>
            public const string Fired = "fired";

            /// <summary>The job ids a job statement addresses.</summary>
            public const string JobIds = "jobIds";

            /// <summary>The name of the statement's <paramref name="index" />th table-valued parameter, <c>t{i}</c>.</summary>
            /// <param name="index">The parameter's index within the statement.</param>
            /// <returns>The name within the statement's namespace.</returns>
            public static string TableValued(int index)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                return string.Create(CultureInfo.InvariantCulture, $"t{index}");
            }

            /// <summary>The name of the statement's <paramref name="index" />th scalar parameter, <c>p{i}</c>.</summary>
            /// <param name="index">The parameter's index within the statement.</param>
            /// <returns>The name within the statement's namespace.</returns>
            public static string Scalar(int index)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                return string.Create(CultureInfo.InvariantCulture, $"p{index}");
            }

            /// <summary>The name of the statement's <paramref name="index" />th capture table, <c>cap{n}</c>.</summary>
            /// <param name="index">The capture's index within the statement.</param>
            /// <returns>The name within the statement's namespace.</returns>
            public static string Capture(int index)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                return string.Create(CultureInfo.InvariantCulture, $"cap{index}");
            }

            /// <summary>
            ///     The name of the blob capture table of one blob-reference column,
            ///     <c>blob_&lt;Schema&gt;_&lt;Table&gt;_&lt;Column&gt;</c>.
            /// </summary>
            /// <param name="schema">The table's schema.</param>
            /// <param name="table">The table's name.</param>
            /// <param name="column">The blob-reference column.</param>
            /// <returns>The name within the statement's namespace.</returns>
            public static string BlobCapture(string schema, string table, string column)
            {
                ArgumentException.ThrowIfNullOrEmpty(schema);
                ArgumentException.ThrowIfNullOrEmpty(table);
                ArgumentException.ThrowIfNullOrEmpty(column);
                return $"blob_{schema}_{table}_{column}";
            }
        }
    }
}
