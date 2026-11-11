// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     Statements composed into one round trip. Each member appends one statement (or a
    ///     handful the platform generates) and returns a handle whose value is available after
    ///     <see cref="ExecuteAsync" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every statement receives a batch ordinal in the order it is appended, and names its
    ///         batch-local parameters and tables after it (<c>@tb{ordinal}_…</c>), so a name a
    ///         statement returns can be used by any statement appended after it. The batch's
    ///         contributors add a prologue before the first statement and an epilogue after the last.
    ///     </para>
    ///     <para>
    ///         These members are the caller channel: a statement that writes a table some stack owns
    ///         is refused when the table is outside <see cref="CallerAuthority" />.
    ///     </para>
    /// </remarks>
    public interface IDataBatch
    {
        /// <summary>What the batch is for, fixed when it was created.</summary>
        BatchPurpose Purpose { get; }

        /// <summary>How the batch's statements are grouped into a transaction; <see cref="TransactionMode.Auto" /> by default.</summary>
        /// <exception cref="InvalidOperationException">Set after the batch executed.</exception>
        TransactionMode TransactionMode { get; set; }

        /// <summary>The limits every query statement of the batch is compiled under; the default limits unless set.</summary>
        QueryexLimits Limits { get; set; }

        /// <summary>The union of every statement's declared writes, the platform's own included.</summary>
        IReadOnlySet<TableName> WrittenTables { get; }

        /// <summary>
        ///     The stack-owned tables a caller statement may write. A table no stack owns may always
        ///     be written.
        /// </summary>
        IReadOnlySet<TableName> CallerAuthority { get; }

        /// <summary>Appends an entity query, with its child and related queries.</summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <param name="query">The query.</param>
        /// <returns>The materialised entities, after execution.</returns>
        BatchResult<EntityQueryResult<TEntity>> Query<TEntity>(EntityQuery<TEntity> query)
            where TEntity : class;

        /// <summary>Appends a columnar query: one page of rows, with the count and ancestors the options ask for.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="arguments">The query's declared parameters, or null for none.</param>
        /// <param name="options">The extras, or null for none.</param>
        /// <returns>The page, after execution.</returns>
        BatchResult<RowPage> Rows(QuerySpec spec, QueryArguments? arguments, RowQueryOptions? options);

        /// <summary>Appends a capped count: a result of <paramref name="cap" /> + 1 means more than the cap.</summary>
        /// <param name="spec">The query whose rows to count.</param>
        /// <param name="arguments">The query's declared parameters, or null for none.</param>
        /// <param name="cap">The most rows counted.</param>
        /// <returns>The count, after execution.</returns>
        BatchResult<int> Count(QuerySpec spec, QueryArguments? arguments, int cap = 10000);

        /// <summary>Appends one row of path-free expressions, such as a report's parameter defaults.</summary>
        /// <param name="items">The expressions.</param>
        /// <param name="queryexVersion">The query-language version the expressions are written in.</param>
        /// <returns>The row, after execution.</returns>
        BatchResult<QueryRowSet> EvaluateConstant(IReadOnlyList<ConstantItem> items, int queryexVersion);

        /// <summary>Appends a save of root rows and their supplied children.</summary>
        /// <typeparam name="TEntity">The root entity type.</typeparam>
        /// <param name="rows">The rows.</param>
        /// <param name="concurrency">Whether each updated row's stamp is compared.</param>
        /// <returns>The receipt and the batch-local names the save declared.</returns>
        SaveHandle Save<TEntity>(IReadOnlyList<TEntity> rows, ConcurrencyMode concurrency = ConcurrencyMode.Check)
            where TEntity : class;

        /// <summary>Appends a uniform, stamped update.</summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <param name="spec">Which rows, and what to assign.</param>
        /// <returns>The updated ids, after execution.</returns>
        BatchResult<UpdateReceipt> Update<TEntity>(UpdateSpec<TEntity> spec)
            where TEntity : class;

        /// <summary>Appends a delete, children first.</summary>
        /// <typeparam name="TEntity">The entity type.</typeparam>
        /// <param name="spec">Which rows.</param>
        /// <returns>The receipt and the batch-local names the delete declared.</returns>
        DeleteHandle Delete<TEntity>(DeleteSpec<TEntity> spec)
            where TEntity : class;

        /// <summary>
        ///     Appends a count assertion: the batch fails with <paramref name="errorNumber" /> and
        ///     <paramref name="code" /> when the query's count differs from <paramref name="expected" />.
        /// </summary>
        /// <param name="countSpec">The query whose rows to count.</param>
        /// <param name="arguments">The query's declared parameters, or null for none.</param>
        /// <param name="expected">The expected count.</param>
        /// <param name="errorNumber">A platform number or one in the distribution band.</param>
        /// <param name="code">The stable code the failure carries.</param>
        void Assert(QuerySpec countSpec, QueryArguments? arguments, int expected, int errorNumber, string code);

        /// <summary>
        ///     Appends raw text. Each hole becomes a scalar parameter, a table-valued parameter (a
        ///     list of a table-type row), or an identifier (<see cref="SqlIdentifier" />).
        /// </summary>
        /// <param name="sql">The text with its holes.</param>
        /// <param name="options">What the statement declares, or null for a read without result sets.</param>
        /// <returns>The statement's result sets, after execution.</returns>
        BatchResult<RawResult> Sql(FormattableString sql, SqlOptions? options);

        /// <summary>Binds rows of a table type as a table-valued parameter for later statements.</summary>
        /// <typeparam name="TRow">The row type: a standalone table type or an entity with a table type.</typeparam>
        /// <param name="rows">The rows, enumerated when the batch is sent.</param>
        /// <returns>The parameter's name.</returns>
        SqlIdentifier Tvp<TRow>(IReadOnlyList<TRow> rows)
            where TRow : class;

        /// <summary>Declares an empty batch-local id table (<c>[Id] int PRIMARY KEY</c>) for later statements to fill and read.</summary>
        /// <returns>The table's name.</returns>
        SqlIdentifier DeclareIdTable();

        /// <summary>Declares cached inputs the batch's result depends on.</summary>
        /// <param name="dependencies">The dependencies.</param>
        void DependsOn(IReadOnlyList<VersionTagDependency> dependencies);

        /// <summary>
        ///     The cached list of a cacheable entity when its tag matches the snapshot; otherwise a
        ///     capped probe appended after the prologue, reporting an oversized list without one.
        /// </summary>
        /// <typeparam name="TEntity">The cacheable entity type.</typeparam>
        /// <returns>The cached list, now or after execution.</returns>
        BatchResult<CacheResult<CachedEntitySet<TEntity>>> FromCache<TEntity>()
            where TEntity : class;

        /// <summary>Adds a tenant-level version tag to this batch's bumps.</summary>
        /// <param name="name">The tag name.</param>
        void BumpVersionTag(string name);

        /// <summary>
        ///     Registers a callback that runs after the batch committed, outside the transaction, in
        ///     registration order. A failing callback is logged, never thrown.
        /// </summary>
        /// <param name="callback">The callback.</param>
        void OnCommitted(Action<BatchOutcome> callback);

        /// <summary>
        ///     Runs the contributors, assembles the text, executes it in one round trip, reads every
        ///     result set and runs the commit callbacks. A batch executes once.
        /// </summary>
        /// <param name="cancellationToken">
        ///     Cancels a read or validation together with the scope's token; a persist, once sent,
        ///     runs to completion.
        /// </param>
        /// <returns>The outcome.</returns>
        /// <exception cref="InvalidOperationException">The batch already executed.</exception>
        Task<BatchOutcome> ExecuteAsync(CancellationToken cancellationToken = default);
    }
}
