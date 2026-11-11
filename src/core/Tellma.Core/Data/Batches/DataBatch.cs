// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Frozen;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     The runtime batch: statements composed through the caller channel (<see cref="IDataBatch" />)
    ///     and the platform's internal channel (<see cref="Begin" />, <see cref="Append" />), run as one
    ///     round trip by <see cref="DataExecutor" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Ordinals are given in append order and never change, so a batch-local name a statement
    ///         returns stays valid. The contributors' prologue stage runs before the first statement
    ///         of any other section is appended, so its statements take the first ordinals; their
    ///         emission before every other statement is guaranteed by section either way.
    ///     </para>
    ///     <para>
    ///         The connect prologue marks the batch (<see cref="MarkConnectPrologue" />) before the
    ///         prologue stage runs and appends its statements in the <see cref="BatchSection.Prologue" />
    ///         section; the contributors that it absorbs then yield. Its reader reports the guard's
    ///         verdict (<see cref="ReportGuard" />), which decides whether the body's result sets exist.
    ///     </para>
    ///     <para>A batch is single-threaded: compose it, then execute it once.</para>
    /// </remarks>
    internal sealed class DataBatch : IDataBatch
    {
        /// <summary>The executor that runs the batch.</summary>
        private readonly DataExecutor _executor;

        /// <summary>The contributors, in prologue order.</summary>
        private readonly IReadOnlyList<IDataBatchContributor> _contributors;

        /// <summary>The appended statements, in append order.</summary>
        private readonly List<BatchStatement> _statements = [];

        /// <summary>The union of the statements' declared writes.</summary>
        private readonly HashSet<TableName> _written = [];

        /// <summary>The batch-local names the statements declared, which identifiers may name.</summary>
        private readonly HashSet<string> _declared = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The distinct parameter names bound so far, the schema fingerprint's included.</summary>
        private readonly HashSet<string> _parameterNames = new(StringComparer.OrdinalIgnoreCase) { ReservedSqlNames.SchemaFingerprint };

        /// <summary>The raw statements that may collapse into one, by text and values.</summary>
        private readonly Dictionary<SqlStatementKey, BatchResult<RawResult>> _shared = [];

        /// <summary>The tenant-level tags this batch bumps explicitly.</summary>
        private readonly HashSet<string> _bumps = new(StringComparer.Ordinal);

        /// <summary>The cached inputs the batch declared.</summary>
        private readonly List<VersionTagDependency> _dependencies = [];

        /// <summary>The sources of the users the batch's writes affect.</summary>
        private readonly List<UserIdSource> _userIdSources = [];

        /// <summary>The commit callbacks, in registration order.</summary>
        private readonly List<Action<BatchOutcome>> _committed = [];

        /// <summary>Where the batch is in its life.</summary>
        private Stage _stage;

        /// <summary>The statement being composed, if any; one at a time.</summary>
        private StatementDraft? _open;

        /// <summary>Whether a contributor failed during a stage, which leaves the batch unusable.</summary>
        private bool _broken;

        /// <summary>Creates a batch.</summary>
        /// <param name="executor">The executor that runs it.</param>
        /// <param name="connection">Where its round trip gets a connection.</param>
        /// <param name="services">The scope it runs in, which supplies the contributors and the counters.</param>
        /// <param name="purpose">What it is for.</param>
        public DataBatch(DataExecutor executor, IBatchConnection connection, IServiceProvider services, BatchPurpose purpose)
        {
            ArgumentNullException.ThrowIfNull(executor);
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(services);
            if (!Enum.IsDefined(purpose))
            {
                throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Not a batch purpose.");
            }

            _executor = executor;
            Connection = connection;
            Purpose = purpose;

            // --- The scope's contributors, ascending; ties keep registration order ---
            _contributors = [.. services.GetServices<IDataBatchContributor>().OrderBy(static contributor => contributor.Order)];

            // --- The scope's counters, cancellation and test-mode audit ---
            Access = services.GetService<DataAccessScope>() ?? new DataAccessScope();
            ScopeCancellation = services.GetService<DataCancellation>()?.Token ?? CancellationToken.None;
            Audit = services.GetService<IDataBatchAudit>();
        }

        /// <summary>Where a batch is in its life.</summary>
        private enum Stage
        {
            /// <summary>Nothing appended; the prologue stage has not run.</summary>
            Fresh,

            /// <summary>The contributors' prologues are being appended.</summary>
            Prologue,

            /// <summary>The caller's statements are being appended.</summary>
            Composing,

            /// <summary>The contributors' epilogues are being appended.</summary>
            Epilogue,

            /// <summary>Executed, or executing: nothing more may be appended.</summary>
            Sealed,
        }

        /// <inheritdoc />
        public BatchPurpose Purpose { get; }

        /// <inheritdoc />
        public TransactionMode TransactionMode
        {
            get;
            set
            {
                EnsureComposing();
                field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Not a transaction mode.");
            }
        } = TransactionMode.Auto;

        /// <inheritdoc />
        public QueryexLimits Limits
        {
            get;
            set => field = value ?? throw new ArgumentNullException(nameof(value));
        } = QueryexLimits.Default;

        /// <inheritdoc />
        public IReadOnlySet<TableName> WrittenTables => _written;

        /// <inheritdoc />
        public IReadOnlySet<TableName> CallerAuthority { get; private set; } = FrozenSet<TableName>.Empty;

        /// <summary>Where the batch's round trip gets its connection.</summary>
        internal IBatchConnection Connection { get; }

        /// <summary>The scope's data-access counters.</summary>
        internal DataAccessScope Access { get; }

        /// <summary>The scope's cancellation token, which cancels reads, validation and connection opening.</summary>
        internal CancellationToken ScopeCancellation { get; }

        /// <summary>The test-mode write audit, when the scope registers one.</summary>
        internal IDataBatchAudit? Audit { get; }

        /// <summary>The appended statements, in append order.</summary>
        internal IReadOnlyList<BatchStatement> Statements => _statements;

        /// <summary>Whether the batch carries the connect prologue, and so the guard wrapper.</summary>
        internal bool CarriesConnectPrologue { get; private set; }

        /// <summary>The guard's verdict, as the connect prologue's reader reported it.</summary>
        internal bool? GuardPassed { get; private set; }

        /// <summary>The tenant-level tags this batch bumps explicitly.</summary>
        internal IReadOnlySet<string> ExplicitBumps => _bumps;

        /// <summary>The cached inputs the batch declared.</summary>
        internal IReadOnlyList<VersionTagDependency> Dependencies => _dependencies;

        /// <summary>The sources of the users the batch's writes affect, for the user-level bumps.</summary>
        internal IReadOnlyList<UserIdSource> UserIdSources => _userIdSources;

        /// <summary>The commit callbacks, in registration order.</summary>
        internal IReadOnlyList<Action<BatchOutcome>> CommittedCallbacks => _committed;

        /// <summary>The value the batch's tag bumps write, generated once per batch.</summary>
        internal Guid Tag { get; } = Guid.NewGuid();

        /// <summary>The tenant snapshot the prelude's reader read, when it read one.</summary>
        internal VersionTagSnapshot? ReadVersionTags { get; private set; }

        /// <summary>The caller's user-level tags the connect prologue's reader read, when it read them.</summary>
        internal UserVersionTagSnapshot? ReadUserVersionTags { get; private set; }

        // ===================== The caller channel =====================

        /// <inheritdoc />
        public BatchResult<EntityQueryResult<TEntity>> Query<TEntity>(EntityQuery<TEntity> query)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(query);
            throw NotAvailable(nameof(Query));
        }

        /// <inheritdoc />
        public BatchResult<RowPage> Rows(QuerySpec spec, QueryArguments? arguments, RowQueryOptions? options)
        {
            ArgumentNullException.ThrowIfNull(spec);
            throw NotAvailable(nameof(Rows));
        }

        /// <inheritdoc />
        public BatchResult<int> Count(QuerySpec spec, QueryArguments? arguments, int cap = 10000)
        {
            ArgumentNullException.ThrowIfNull(spec);
            throw NotAvailable(nameof(Count));
        }

        /// <inheritdoc />
        public BatchResult<QueryRowSet> EvaluateConstant(IReadOnlyList<ConstantItem> items, int queryexVersion)
        {
            ArgumentNullException.ThrowIfNull(items);
            throw NotAvailable(nameof(EvaluateConstant));
        }

        /// <inheritdoc />
        public SaveHandle Save<TEntity>(IReadOnlyList<TEntity> rows, ConcurrencyMode concurrency = ConcurrencyMode.Check)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            throw NotAvailable(nameof(Save));
        }

        /// <inheritdoc />
        public BatchResult<UpdateReceipt> Update<TEntity>(UpdateSpec<TEntity> spec)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(spec);
            throw NotAvailable(nameof(Update));
        }

        /// <inheritdoc />
        public DeleteHandle Delete<TEntity>(DeleteSpec<TEntity> spec)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(spec);
            throw NotAvailable(nameof(Delete));
        }

        /// <inheritdoc />
        public void Assert(QuerySpec countSpec, QueryArguments? arguments, int expected, int errorNumber, string code)
        {
            ArgumentNullException.ThrowIfNull(countSpec);
            throw NotAvailable(nameof(Assert));
        }

        /// <inheritdoc />
        public BatchResult<RawResult> Sql(FormattableString sql, SqlOptions? options)
        {
            ArgumentNullException.ThrowIfNull(sql);
            options ??= SqlOptions.None;
            EnsureComposing();

            // The prologue stage first, so a prologue statement identical to this one is already
            // among the shared statements when this one looks itself up.
            EnsurePrologue();

            // --- Refusals at append: a write outside the caller's authority, a user-id source that is no batch table ---
            CheckAuthority(options.Writes);
            if (options.UserIds is SqlIdentifier userIds)
            {
                _ = userIds.IsVariable
                    ? RenderIdentifier(userIds)
                    : throw new ArgumentException(
                        $"The user ids '{userIds.Name}' name a permanent table; they name a table this batch declared, such as one a Tvp or DeclareIdTable call returned.",
                        nameof(options));
            }

            // --- A read, or a statement declared idempotent, collapses into an identical one ---
            object?[] arguments = sql.GetArguments();
            SqlStatementKey? key = options.Writes.Count == 0 || options.Idempotent ? new SqlStatementKey(sql.Format, arguments, options) : null;
            if (key is not null && _shared.TryGetValue(key, out BatchResult<RawResult>? shared))
            {
                return shared;
            }

            // --- The text, its holes bound in the statement's own namespace ---
            BatchResult<RawResult> result = new();
            StatementDraft draft = Begin();
            try
            {
                string?[] rendered = new string?[arguments.Length];
                string text = SqlTemplate.Render(sql.Format, arguments.Length, index => rendered[index] ??= RenderHole(draft, arguments[index], index));

                int ordinal = draft.Ordinal;
                int resultSets = options.ResultSets;
                Append(
                    draft,
                    text,
                    options.Writes,
                    options.Idempotent,
                    async (cursor, cancellationToken) => result.Complete(
                        await RawResultReader.ReadAsync(cursor, ordinal, resultSets, cancellationToken).ConfigureAwait(false)));
            }
            catch
            {
                Discard(draft);
                throw;
            }

            if (options.UserIds is SqlIdentifier ids && options.Writes.Count > 0)
            {
                _userIdSources.Add(new UserIdSource(options.Writes, Column: null, ids));
            }

            if (key is not null)
            {
                _ = _shared.TryAdd(key, result);
            }

            return result;
        }

        /// <inheritdoc />
        public SqlIdentifier Tvp<TRow>(IReadOnlyList<TRow> rows)
            where TRow : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            EnsureComposing();

            // A statement of its own with no text: it only binds the parameter later statements name.
            StatementDraft draft = Begin();
            try
            {
                string name = draft.Table(rows);
                Append(draft, string.Empty);
                return new SqlIdentifier(name);
            }
            catch
            {
                Discard(draft);
                throw;
            }
        }

        /// <inheritdoc />
        public SqlIdentifier DeclareIdTable()
        {
            EnsureComposing();
            StatementDraft draft = Begin();
            string name = draft.Name(ReservedSqlNames.StatementNames.Ids);
            Append(draft, $"DECLARE {name} TABLE ([Id] int PRIMARY KEY);");
            return new SqlIdentifier(name);
        }

        /// <inheritdoc />
        public void DependsOn(IReadOnlyList<VersionTagDependency> dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            EnsureComposing();
            foreach (VersionTagDependency dependency in dependencies)
            {
                _dependencies.Add(dependency ?? throw new ArgumentException("The dependencies must not hold a null entry.", nameof(dependencies)));
            }
        }

        /// <inheritdoc />
        public BatchResult<CacheResult<CachedEntitySet<TEntity>>> FromCache<TEntity>()
            where TEntity : class
        {
            throw NotAvailable(nameof(FromCache));
        }

        /// <inheritdoc />
        public void BumpVersionTag(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            EnsureComposing();
            _ = _bumps.Add(name);
        }

        /// <inheritdoc />
        public void OnCommitted(Action<BatchOutcome> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            EnsureComposing();
            _committed.Add(callback);
        }

        /// <inheritdoc />
        public Task<BatchOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            EnsureNotBroken();
            if (_stage == Stage.Sealed)
            {
                throw new InvalidOperationException("The batch already executed. A batch executes once; create a new batch for more work.");
            }

            if (_open is not null)
            {
                throw new InvalidOperationException("A statement of the batch is still being composed.");
            }

            // --- The contributors' stages: the prologue if nothing was appended, then the epilogue ---
            EnsurePrologue();
            _stage = Stage.Epilogue;
            try
            {
                for (int index = _contributors.Count - 1; index >= 0; index--)
                {
                    _contributors[index].Contribute(this, DataBatchStage.Epilogue);
                }
            }
            catch
            {
                // A half-run epilogue stage cannot be resumed or repeated: the batch is unusable.
                _broken = true;
                throw;
            }

            _stage = Stage.Sealed;
            return _executor.ExecuteAsync(this, cancellationToken);
        }

        // ===================== The internal channel =====================

        /// <summary>
        ///     Begins a statement in the internal channel: the statement takes the next ordinal, which
        ///     its batch-local names carry. Complete it with <see cref="Append" />, or give the
        ///     ordinal back with <see cref="Discard" />.
        /// </summary>
        /// <param name="section">
        ///     The section to emit it in; by default the stage's own: the prologue during the
        ///     prologue stage, the epilogue during the epilogue stage, the body otherwise. An explicit
        ///     <see cref="BatchSection.Prologue" /> statement may only be begun before the prologue
        ///     stage ended, and does not start it.
        /// </param>
        /// <returns>The draft.</returns>
        /// <exception cref="InvalidOperationException">
        ///     Another statement is being composed, the batch executed, or a prologue statement comes
        ///     after the prologue stage.
        /// </exception>
        internal StatementDraft Begin(BatchSection? section = null)
        {
            EnsureComposing();
            if (_open is not null)
            {
                throw new InvalidOperationException("Another statement of the batch is being composed. Append or discard it first.");
            }

            if (section == BatchSection.Prologue)
            {
                if (_stage is not (Stage.Fresh or Stage.Prologue))
                {
                    throw new InvalidOperationException("A prologue statement is appended before the batch's first statement of any other section.");
                }
            }
            else
            {
                EnsurePrologue();
            }

            BatchSection effective = section ?? _stage switch
            {
                Stage.Prologue => BatchSection.Prologue,
                Stage.Epilogue => BatchSection.Epilogue,
                Stage.Fresh or Stage.Composing or Stage.Sealed => BatchSection.Body,
                _ => throw new InvalidOperationException($"The batch is in an unknown stage, {_stage}."),
            };

            _open = new StatementDraft(_statements.Count, effective, _executor.Binder);
            return _open;
        }

        /// <summary>Appends the statement a draft composed. The internal channel: no write-authority check.</summary>
        /// <param name="draft">The draft <see cref="Begin" /> returned.</param>
        /// <param name="text">The statement's text; empty for one that only binds parameters.</param>
        /// <param name="writes">The tables it writes, or null for none.</param>
        /// <param name="idempotent">Whether running it twice has the effect of running it once.</param>
        /// <param name="reader">Reads its result sets, or null when it returns none.</param>
        /// <exception cref="InvalidOperationException">
        ///     The draft is not the open one, or the batch would declare more parameters than allowed.
        /// </exception>
        internal void Append(StatementDraft draft, string text, IReadOnlySet<TableName>? writes = null, bool idempotent = false, StatementReader? reader = null)
        {
            ArgumentNullException.ThrowIfNull(draft);
            ArgumentNullException.ThrowIfNull(text);
            if (!ReferenceEquals(draft, _open))
            {
                throw new InvalidOperationException("Only the statement being composed can be appended.");
            }

            // --- The parameter budget, checked at compose time: a defect, never a user error ---
            int added = draft.Parameters.Count(parameter => !_parameterNames.Contains(parameter.Name));
            if (_parameterNames.Count + added > BatchAssembly.MaxParameters)
            {
                throw BatchAssembly.TooManyParameters(_parameterNames.Count + added);
            }

            IReadOnlySet<TableName> written = writes is null || writes.Count == 0 ? [] : writes.ToFrozenSet();
            _statements.Add(new BatchStatement(draft.Ordinal, draft.Section, text, [.. draft.Parameters], written, idempotent, reader));
            _written.UnionWith(written);
            _parameterNames.UnionWith(draft.Parameters.Select(static parameter => parameter.Name));
            _declared.UnionWith(draft.Declared);
            _open = null;
        }

        /// <summary>Abandons the statement a draft was composing; its ordinal is given to the next statement.</summary>
        /// <param name="draft">The draft.</param>
        internal void Discard(StatementDraft draft)
        {
            if (ReferenceEquals(draft, _open))
            {
                _open = null;
            }
        }

        /// <summary>
        ///     Marks the batch as carrying the connect prologue: its body is wrapped in the guard, and
        ///     the contributors the connect prologue absorbs yield. The mark precedes the prologue stage.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        ///     The batch is a maintenance batch, or its prologue stage already ran.
        /// </exception>
        internal void MarkConnectPrologue()
        {
            if (Purpose == BatchPurpose.Maintenance)
            {
                throw new InvalidOperationException("A maintenance batch carries no connect prologue.");
            }

            if (_stage != Stage.Fresh)
            {
                throw new InvalidOperationException("The connect prologue marks the batch before its first statement is appended.");
            }

            CarriesConnectPrologue = true;
        }

        /// <summary>Reports the guard's verdict, from the connect prologue's reader.</summary>
        /// <param name="passed">Whether the guard passed, so the body ran.</param>
        internal void ReportGuard(bool passed)
        {
            GuardPassed = passed;
        }

        /// <summary>Reports the tenant snapshot the prelude's reader read.</summary>
        /// <param name="snapshot">The snapshot.</param>
        internal void ReportVersionTags(VersionTagSnapshot snapshot)
        {
            ReadVersionTags = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>Reports the caller's user-level tags the connect prologue's reader read.</summary>
        /// <param name="snapshot">The snapshot.</param>
        internal void ReportUserVersionTags(UserVersionTagSnapshot snapshot)
        {
            ReadUserVersionTags = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>Sets the authority of the current appender: the stack-owned tables its statements may write.</summary>
        /// <param name="authority">The tables.</param>
        internal void SetCallerAuthority(IReadOnlySet<TableName> authority)
        {
            ArgumentNullException.ThrowIfNull(authority);
            CallerAuthority = authority.ToFrozenSet();
        }

        /// <summary>Adds the users a platform write affects, for the user-level bumps.</summary>
        /// <param name="source">The source.</param>
        internal void AddUserIdSource(UserIdSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            _userIdSources.Add(source);
        }

        /// <summary>Assembles the batch into one framed command.</summary>
        /// <returns>The assembly.</returns>
        internal BatchAssembly Assemble()
        {
            return BatchAssembly.Create(
                _statements,
                _executor.Fingerprint,
                CarriesConnectPrologue,
                inTextTransaction: TransactionMode == TransactionMode.Auto && _written.Count > 0);
        }

        // ===================== Helpers =====================

        /// <summary>The refusal of a member whose statement compiler is not composed in this build.</summary>
        /// <param name="member">The member's name.</param>
        /// <returns>The exception.</returns>
        private static NotSupportedException NotAvailable(string member)
        {
            return new NotSupportedException($"{nameof(IDataBatch)}.{member} is not available: its statement compiler is not part of this build.");
        }

        /// <summary>Refuses everything once a contributor failed during a stage.</summary>
        /// <exception cref="InvalidOperationException">A contributor failed.</exception>
        private void EnsureNotBroken()
        {
            if (_broken)
            {
                throw new InvalidOperationException(
                    "A contributor failed while the batch ran its prologue or epilogue stage, so the batch is incomplete and can neither take more statements nor execute. Create a new batch.");
            }
        }

        /// <summary>Refuses an append once the batch is sealed or broken.</summary>
        private void EnsureComposing()
        {
            EnsureNotBroken();
            if (_stage == Stage.Sealed)
            {
                throw new InvalidOperationException("The batch already executed. A batch executes once; create a new batch for more work.");
            }
        }

        /// <summary>Runs the contributors' prologue stage, once, before the first statement of another section.</summary>
        private void EnsurePrologue()
        {
            if (_stage != Stage.Fresh)
            {
                return;
            }

            _stage = Stage.Prologue;
            try
            {
                foreach (IDataBatchContributor contributor in _contributors)
                {
                    contributor.Contribute(this, DataBatchStage.Prologue);
                }
            }
            catch
            {
                // A batch missing part of its prologue must never run.
                _broken = true;
                throw;
            }

            _stage = Stage.Composing;
        }

        /// <summary>Refuses a caller write to a stack-owned table outside the caller's authority.</summary>
        /// <param name="writes">The tables the statement writes.</param>
        private void CheckAuthority(IReadOnlySet<TableName> writes)
        {
            foreach (TableName table in writes)
            {
                if (_executor.Owners.OwnerOf(table) is string owner && !CallerAuthority.Contains(table))
                {
                    throw new InvalidOperationException(
                        $"The statement writes {table}, which the stack '{owner}' owns and the current caller may not write. Enlist the write with '{owner}', so it runs under that stack's authority.");
                }
            }
        }

        /// <summary>Renders one hole of a raw statement.</summary>
        /// <param name="draft">The statement's draft.</param>
        /// <param name="value">The hole's value.</param>
        /// <param name="index">The hole's index, for the message.</param>
        /// <returns>The text the hole becomes.</returns>
        private string RenderHole(StatementDraft draft, object? value, int index)
        {
            // An identifier is text; a scalar binds a parameter; a list of table-type rows binds a table.
            return value is SqlIdentifier identifier ? RenderIdentifier(identifier)
                : ScalarValues.IsScalar(value) ? draft.Scalar(value)
                : draft.TryTable(value!, out string? name) ? name!
                : throw new ArgumentException(
                    $"Hole {index} of the statement holds a {value!.GetType().Name}, which binds neither as a scalar nor as a table-valued parameter. A list binds as rows of a table type.",
                    nameof(value));
        }

        /// <summary>Renders an identifier: a declared or batch-wide variable as is, an object name bracket-quoted.</summary>
        /// <param name="identifier">The identifier.</param>
        /// <returns>The text.</returns>
        /// <exception cref="ArgumentException">A variable the batch did not declare, or a malformed name.</exception>
        private string RenderIdentifier(SqlIdentifier identifier)
        {
            string name = identifier.Name;
            bool known = SqlNames.IsVariableName(name)
                && (_declared.Contains(name) || name.StartsWith(ReservedSqlNames.BatchPrefix, StringComparison.OrdinalIgnoreCase));
            return !identifier.IsVariable
                ? SqlNames.QuoteObjectName(name)
                : known
                    ? name
                    : throw new ArgumentException(
                        $"The identifier '{name}' names no table this batch declared. Use the name a Tvp or DeclareIdTable call returned.", nameof(identifier));
        }
    }
}
