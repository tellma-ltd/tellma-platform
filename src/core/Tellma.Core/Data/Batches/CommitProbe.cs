// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;
using System.Globalization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.TableTypes;

namespace Tellma.Core.Data.Batches
{
    // The probe is spec 0012 §6.5's.
    /// <summary>
    ///     Answers whether a persist round trip that lost its connection committed: the rows it
    ///     inserted into its first root table exist exactly when it did, because a batch commits as
    ///     one transaction. The probe reads them under <c>READCOMMITTEDLOCK</c>, so while the first
    ///     attempt still runs on the server, holding its inserted keys under exclusive locks, the
    ///     probe waits for it to commit or roll back instead of reading past it as read-committed
    ///     snapshot would.
    /// </summary>
    internal sealed class CommitProbe
    {
        /// <summary>The name the probe binds the ids under.</summary>
        public static readonly string IdsName = ReservedSqlNames.Statement(0, ReservedSqlNames.StatementNames.TableValued(0));

        /// <summary>Creates the probe; see <see cref="ForIds(TableName, IReadOnlyList{int}, TableTypeBinder)" />.</summary>
        /// <param name="table">The root table.</param>
        /// <param name="count">How many distinct ids it probes for.</param>
        /// <param name="parameter">Creates the ids' parameter for one command.</param>
        private CommitProbe(TableName table, int count, Func<SqlParameter> parameter)
        {
            Table = table;
            Count = count;
            Parameter = parameter;
            Text = $"SELECT COUNT_BIG(*) FROM {table.Delimited} WITH (READCOMMITTEDLOCK) WHERE [Id] IN (SELECT [Id] FROM {IdsName});";
        }

        /// <summary>The root table the batch inserted into.</summary>
        public TableName Table { get; }

        /// <summary>How many distinct ids the batch inserted.</summary>
        public int Count { get; }

        /// <summary>The probe's text: one autocommit statement.</summary>
        public string Text { get; }

        /// <summary>Creates the ids' parameter for one command.</summary>
        public Func<SqlParameter> Parameter { get; }

        /// <summary>A probe for the <c>int</c> ids a batch inserted into a root table.</summary>
        /// <param name="table">The root table.</param>
        /// <param name="ids">The ids, at least one.</param>
        /// <param name="binder">The table-type binder.</param>
        /// <returns>The probe.</returns>
        public static CommitProbe ForIds(TableName table, IReadOnlyList<int> ids, TableTypeBinder binder)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(binder);
            ArgumentOutOfRangeException.ThrowIfZero(ids.Count);

            List<IdList> rows = [.. ids.Distinct().Select(static id => new IdList { Id = id })];
            TableTypeBinding binding = binder.Binding(typeof(IdList));
            return new CommitProbe(table, rows.Count, () => binding.Parameter(IdsName, rows));
        }

        /// <summary>A probe for the <c>bigint</c> ids a batch inserted into a root table.</summary>
        /// <param name="table">The root table.</param>
        /// <param name="ids">The ids, at least one.</param>
        /// <param name="binder">The table-type binder.</param>
        /// <returns>The probe.</returns>
        public static CommitProbe ForIds(TableName table, IReadOnlyList<long> ids, TableTypeBinder binder)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(binder);
            ArgumentOutOfRangeException.ThrowIfZero(ids.Count);

            List<BigIdList> rows = [.. ids.Distinct().Select(static id => new BigIdList { Id = id })];
            TableTypeBinding binding = binder.Binding(typeof(BigIdList));
            return new CommitProbe(table, rows.Count, () => binding.Parameter(IdsName, rows));
        }

        /// <summary>The verdict a count of the inserted rows found gives.</summary>
        /// <param name="found">The rows found.</param>
        /// <param name="expected">The rows the batch inserted.</param>
        /// <returns>
        ///     <see cref="CommitVerdict.Committed" /> when all exist, <see cref="CommitVerdict.NotCommitted" />
        ///     when none does, and <see cref="CommitVerdict.Unknown" /> for a part, which one transaction
        ///     cannot leave and so means the ids were not the batch's own.
        /// </returns>
        public static CommitVerdict Verdict(long found, int expected)
        {
            return found == expected ? CommitVerdict.Committed
                : found == 0 ? CommitVerdict.NotCommitted
                : CommitVerdict.Unknown;
        }

        /// <summary>
        ///     Whether an error is the duplicate key of the probed table's primary key: on the re-run
        ///     after a <see cref="CommitVerdict.NotCommitted" /> verdict it means the first attempt
        ///     reached its insert after the probe and committed.
        /// </summary>
        /// <param name="error">The re-run's error.</param>
        /// <returns>True for a 2627 on <c>PK_&lt;Table&gt;</c>.</returns>
        public bool IsOwnPrimaryKeyViolation(SqlErrorInfo error)
        {
            return error.Number == 2627
                && DataErrors.IndexName(error.Number, error.Message) is string index
                && string.Equals(index, $"PK_{Table.Name}", StringComparison.Ordinal);
        }

        /// <summary>Runs the probe as one autocommit round trip.</summary>
        /// <param name="connection">An open connection, not the lost one.</param>
        /// <param name="timeoutSeconds">The command timeout.</param>
        /// <returns>The verdict.</returns>
        public async Task<CommitVerdict> RunAsync(SqlConnection connection, int timeoutSeconds)
        {
            ArgumentNullException.ThrowIfNull(connection);

            // Never cancelled: the answer decides whether a write that may have committed is reported as one.
            await using SqlCommand command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = Text;
            command.CommandTimeout = timeoutSeconds;
            _ = command.Parameters.Add(Parameter());
            object? found = await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
            return Verdict(Convert.ToInt64(found, CultureInfo.InvariantCulture), Count);
        }
    }
}
