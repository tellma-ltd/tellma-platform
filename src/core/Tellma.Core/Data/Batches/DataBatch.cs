// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Frozen;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Caching;
using Tellma.Core.Data.Ids;
using Tellma.Core.Data.TableTypes;
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
    ///         The premises it marks the batch with are what a writing persist batch re-checks inside
    ///         its transaction.
    ///     </para>
    ///     <para>
    ///         A persist batch that inserts root rows declares their ids (<see cref="DeclareInsertedRoots(TableName, IReadOnlyList{int})" />),
    ///         so a round trip that lost its connection can be probed for whether it committed.
    ///     </para>
    ///     <para>
    ///         Before its epilogue the batch carries the allocator's refills of the tables it writes. Once
    ///         it ran, and before its commit callbacks, the platform's own completion steps run
    ///         (<see cref="OnExecuted" />): settling id reservations, stamping saved entities.
    ///     </para>
    ///     <para>A batch is single-threaded: compose it, then execute it once.</para>
    /// </remarks>
    internal sealed partial class DataBatch : IDataBatch
    {
        /// <summary>The executor that runs the batch.</summary>
        private readonly DataExecutor _executor;

        /// <summary>The scope the batch runs in, which a sibling batch shares.</summary>
        private readonly IServiceProvider _services;

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

        /// <summary>The platform's completion steps, in registration order.</summary>
        private readonly List<Func<CancellationToken, Task>> _executed = [];

        /// <summary>The id reservations whose rows a save of the batch inserts.</summary>
        private readonly HashSet<ReservationHold> _holds = [];

        /// <summary>The ordinals of the connect prologue's own statements, whose writes are bookkeeping that bumps nothing.</summary>
        private readonly HashSet<int> _connectStatements = [];

        /// <summary>Where the batch is in its life.</summary>
        private Stage _stage;

        /// <summary>Whether the bump statement is appended, after which no statement may write a table with a tag.</summary>
        private bool _bumpAppended;

        /// <summary>The statement being composed, if any; one at a time.</summary>
        private StatementDraft? _open;

        /// <summary>Whether a contributor failed during a stage, which leaves the batch unusable.</summary>
        private bool _broken;

        /// <summary>Whether a statement needs the batch's statements in one transaction, as a save does.</summary>
        private bool _needsTransaction;

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
            _services = services;
            Connection = connection;
            Purpose = purpose;

            // --- The scope's contributors, ascending; ties keep registration order ---
            _contributors = [.. services.GetServices<IDataBatchContributor>().OrderBy(static contributor => contributor.Order)];

            // --- The scope's counters, cancellation and test-mode audit ---
            // The composed scope's counters belong to its recorder, which records them when the scope ends.
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
        public bool IsComposing => !_broken && _stage != Stage.Sealed;

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

        /// <summary>
        ///     How many parameters the batch declares so far, the schema fingerprint included: what a
        ///     caller appending several statements measures against <see cref="BatchAssembly.MaxParameters" />
        ///     before appending the first, so a call that would overflow the cap leaves the batch as it was.
        /// </summary>
        internal int ParameterCount => _parameterNames.Count;

        /// <summary>Whether the batch carries the connect prologue, and so the guard wrapper.</summary>
        internal bool CarriesConnectPrologue => Premises is not null;

        /// <summary>What the connect prologue confirmed, when the batch carries it.</summary>
        internal GuardPremises? Premises { get; private set; }

        /// <summary>The commit probe of the root rows the batch inserts, when it declared them.</summary>
        internal CommitProbe? Probe { get; private set; }

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

        /// <summary>The platform's completion steps, in registration order.</summary>
        internal IReadOnlyList<Func<CancellationToken, Task>> ExecutedSteps => _executed;

        /// <summary>The value the batch's tag bumps write, generated once per batch.</summary>
        internal Guid Tag { get; } = Guid.NewGuid();

        /// <summary>The tenant snapshot the prelude's reader read, when it read one.</summary>
        internal VersionTagSnapshot? ReadVersionTags { get; private set; }

        /// <summary>The caller's user-level tags the connect prologue's reader read, when it read them.</summary>
        internal UserVersionTagSnapshot? ReadUserVersionTags { get; private set; }

        /// <summary>The users whose user-level tags the bump epilogue moved, as its reader read them; null when it moved none.</summary>
        internal IReadOnlyList<AffectedUser>? AffectedUsers { get; private set; }

        /// <summary>The table-type binder, through which platform statements bind their table-valued parameters.</summary>
        internal TableTypeBinder Binder => _executor.Binder;

        /// <summary>
        ///     What the batch bumps: the tenant-level names and user-level rules its written tables and
        ///     explicit bumps resolve to. Fixed when the epilogue stage starts, from the writes declared
        ///     before it, so the bump epilogue's own writes never count; resolved afresh before then. The
        ///     writes of the connect prologue's own statements never count: they are the platform's
        ///     bookkeeping (the activity stamp and the join flip), which no cache reads. Any other
        ///     prologue statement that writes a table with a tag is refused when appended.
        /// </summary>
        internal VersionTagBumpPlan Bumps
        {
            get => field ?? ResolveBumps();
            private set;
        }

        // ===================== The caller channel =====================

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
            CheckUserIds(options);
            if (options.UserIds is SqlIdentifier { Name: ReservedSqlNames.CallerIds } && Premises is not { IsSystem: false })
            {
                // Only a user's connect prologue declares the caller's ids; without it the batch would
                // fail to compile on the server.
                throw new InvalidOperationException(
                    "The statement names the caller's own rows (SqlOptions.ForCaller), but the batch carries no user's connect prologue, which declares the caller. Pass the user ids in SqlOptions.UserIds instead.");
            }

            if (options.UserIds is SqlIdentifier userIds)
            {
                _ = userIds.IsVariable
                    ? RenderIdentifier(userIds)
                    : throw new ArgumentException(
                        $"The user ids '{userIds.Name}' name a permanent table; they name a table this batch declared, such as one a Tvp or DeclareIdTable call returned.",
                        nameof(options));
            }

            // --- A read, or a statement declared idempotent, collapses into an identical one ---
            // Only within a write epoch (Append clears the cache at each declared write), and never
            // when it names a mutable batch table, whose fills no statement can declare.
            object?[] arguments = sql.GetArguments();
            SqlStatementKey? key = (options.Writes.Count == 0 || options.Idempotent) && !NamesMutableBatchTable(arguments)
                ? new SqlStatementKey(sql.Format, arguments, options)
                : null;
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
                    async (cursor, cancellationToken) => cursor.Complete(
                        result, await RawResultReader.ReadAsync(cursor, ordinal, resultSets, cancellationToken).ConfigureAwait(false)));
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

            // --- Every name checked before any is kept: a refused list leaves the batch unchanged ---
            // A name the registry does not know (one differing from a registered name in case alone
            // among them) has no row to guard, and would collide with its registered twin in the
            // guard's case-insensitive key; it is refused here, where the mistake is.
            foreach (VersionTagDependency dependency in dependencies)
            {
                if (dependency is null)
                {
                    throw new ArgumentException("The dependencies must not hold a null entry.", nameof(dependencies));
                }

                if (_executor.VersionTags is IVersionTagRegistry registry && !registry.Names.Contains(dependency.Name, StringComparer.Ordinal))
                {
                    throw new ArgumentException(
                        $"The dependency on the version tag '{dependency.Name}' names no registered tag. Name the tag exactly as it is registered: a platform tag ('{VersionTagNames.Settings}', '{VersionTagNames.Permissions}', '{VersionTagNames.Entities}'), a list's tag, or one an entity declares with [BumpsVersionTag].",
                        nameof(dependencies));
                }
            }

            _dependencies.AddRange(dependencies);
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

            // A name the registry does not know has no row in any database the migrator seeded, so
            // the bump would fail at commit; it is refused here, where the mistake is.
            if (_executor.VersionTags is IVersionTagRegistry registry && !registry.Names.Contains(name, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"The version tag '{name}' is not registered. Bump a tag an entity declares with [BumpsVersionTag], or a platform tag ('{VersionTagNames.Settings}', '{VersionTagNames.Permissions}', '{VersionTagNames.Entities}').",
                    nameof(name));
            }

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

            // A save whose reservation was disposed after the save was composed would insert ids the
            // buffer already took back, and may have handed to another reservation.
            foreach (ReservationHold hold in _holds)
            {
                if (hold.IsReturned)
                {
                    throw ReturnedReservation();
                }
            }

            // --- The contributors' stages: the prologue if nothing was appended, then the epilogue ---
            // The allocator's refills ride the body, after the caller's statements; they declare no
            // writes, so the bumps, fixed next from the writes the caller declared, are unchanged.
            EnsurePrologue();
            _executor.Allocator.AppendRefills(this);
            VersionTagBumpPlan plan = ResolveBumps();

            // An autocommit batch commits each write on its own, so a failure after one would leave it
            // committed with no tag moved: a write that bumps a tag needs a transaction.
            if (TransactionMode == TransactionMode.None && !plan.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"The batch runs in {nameof(TransactionMode.None)} mode, which commits each statement on its own, but its writes bump version tags ({Describe(plan)}). The bump must commit with the writes it covers: run the batch in {nameof(TransactionMode.Auto)} or {nameof(TransactionMode.Explicit)} mode.");
            }

            Bumps = plan;
            _stage = Stage.Epilogue;
            try
            {
                for (int index = _contributors.Count - 1; index >= 0; index--)
                {
                    _contributors[index].Contribute(this, DataBatchStage.Epilogue);
                }

                // An epilogue statement that writes a table whose tags the fixed plan does not move
                // would commit rows no cache learns of: refuse it rather than leave them stale.
                VersionTagBumpPlan after = ResolveBumps();
                if (after.TenantNames.Except(plan.TenantNames, StringComparer.Ordinal).Any() || after.UserRules.Except(plan.UserRules).Any())
                {
                    throw new InvalidOperationException(
                        $"An epilogue statement writes a table whose version tags the batch does not bump ({Describe(after)}, where the caller's writes bump {Describe(plan)}). The bump is fixed when the epilogue stage starts; such a write belongs in the body.");
                }
            }
            catch
            {
                // A half-run epilogue stage cannot be resumed or repeated: the batch is unusable.
                _broken = true;
                throw;
            }

            _stage = Stage.Sealed;

            // A persist is attempted from here: whatever its outcome, the ids its saves insert may be
            // in the database, so their reservations never return them. The mark is refused for a
            // reservation disposed since the check above; the batch then never runs, and a hold
            // marked before it only leaves a gap in its sequence.
            foreach (ReservationHold hold in _holds)
            {
                if (!hold.TryMarkPersistAttempted())
                {
                    throw ReturnedReservation();
                }
            }

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

            FrozenSet<TableName> written = writes is null || writes.Count == 0 ? [] : writes.ToFrozenSet();
            CheckTaggedWritePlacement(draft, written);

            // A declared write ends the write epoch: a later read must see it, so no earlier handle
            // is shared past this point. Cleared before the caller caches this statement's own key,
            // so two adjacent identical idempotent writes still collapse.
            if (written.Count > 0)
            {
                _shared.Clear();
                _sharedQueries.Clear();
            }

            _statements.Add(new BatchStatement(draft.Ordinal, draft.Section, text, [.. draft.Parameters], written, idempotent, reader, draft.EntityType));
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
                _ = _connectStatements.Remove(draft.Ordinal);
                _open = null;
            }
        }

        /// <summary>
        ///     Registers a completion step of the platform's own: it runs once the batch executed and its
        ///     body ran, before the commit callbacks, and may run round trips of its own. A step never
        ///     throws: the batch may have committed.
        /// </summary>
        /// <param name="step">The step, given the caller's cancellation token.</param>
        internal void OnExecuted(Func<CancellationToken, Task> step)
        {
            ArgumentNullException.ThrowIfNull(step);
            EnsureComposing();
            _executed.Add(step);
        }

        /// <summary>
        ///     Creates a batch in the same scope and on the same connection, for a completion step's own
        ///     round trip.
        /// </summary>
        /// <param name="purpose">The sibling's purpose.</param>
        /// <returns>The sibling.</returns>
        internal DataBatch CreateSibling(BatchPurpose purpose)
        {
            return new DataBatch(_executor, Connection, _services, purpose);
        }

        /// <summary>
        ///     Begins one of the connect prologue's own statements, in the <see cref="BatchSection.Prologue" />
        ///     section: its writes (the activity stamp, the join flip) are the platform's bookkeeping,
        ///     which bumps no tag. Every other prologue statement that writes a table with a tag is refused.
        /// </summary>
        /// <returns>The draft; complete it with <see cref="Append" />.</returns>
        /// <exception cref="InvalidOperationException">
        ///     The batch carries no connect prologue (<see cref="MarkConnectPrologue" />), or its prologue
        ///     stage ended.
        /// </exception>
        internal StatementDraft BeginConnectPrologue()
        {
            if (Premises is null)
            {
                throw new InvalidOperationException("Only a batch marked with the connect prologue takes the connect prologue's statements.");
            }

            StatementDraft draft = Begin(BatchSection.Prologue);
            _ = _connectStatements.Add(draft.Ordinal);
            return draft;
        }

        /// <summary>
        ///     Records that the bump statement is appended. A later statement that writes a table with
        ///     a tag is refused: it would commit after the bump, and its users would read no source.
        /// </summary>
        internal void MarkBumpAppended()
        {
            _bumpAppended = true;
        }

        /// <summary>
        ///     Marks the batch as carrying the connect prologue: its body is wrapped in the guard, and
        ///     the contributors the connect prologue absorbs yield. The mark precedes the prologue stage.
        /// </summary>
        /// <param name="premises">
        ///     What the connect prologue confirms: the tags a writing persist batch re-checks inside its
        ///     transaction, and whether a user's re-check applies.
        /// </param>
        /// <exception cref="InvalidOperationException">
        ///     The batch is a maintenance batch, or its prologue stage already ran.
        /// </exception>
        internal void MarkConnectPrologue(GuardPremises premises)
        {
            ArgumentNullException.ThrowIfNull(premises);
            if (Purpose == BatchPurpose.Maintenance)
            {
                throw new InvalidOperationException("A maintenance batch carries no connect prologue.");
            }

            if (_stage != Stage.Fresh)
            {
                throw new InvalidOperationException("The connect prologue marks the batch before its first statement is appended.");
            }

            Premises = premises;
        }

        /// <summary>
        ///     The parameter binding the value the batch's tag bumps write, <c>@tm_tag</c>; every
        ///     statement that writes a tag binds it, and binds the one value.
        /// </summary>
        /// <returns>The parameter, for <see cref="StatementDraft.Reserved" />.</returns>
        internal BatchParameter TagParameter()
        {
            return new ScalarParameter(ReservedSqlNames.Tag, System.Data.SqlDbType.UniqueIdentifier, Tag);
        }

        /// <summary>
        ///     Declares the <c>int</c> ids the batch inserts into a root table, which the commit probe
        ///     looks for when a round trip loses its connection. The first declaration is the probe's;
        ///     later ones, and an empty list, change nothing.
        /// </summary>
        /// <param name="table">The root table, whose primary key is <c>PK_&lt;table&gt;</c> on <c>[Id]</c>.</param>
        /// <param name="ids">The ids inserted.</param>
        internal void DeclareInsertedRoots(TableName table, IReadOnlyList<int> ids)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(ids);
            EnsureComposing();
            if (Probe is null && ids.Count > 0)
            {
                Probe = CommitProbe.ForIds(table, ids, _executor.Binder);
            }
        }

        /// <summary>Declares the <c>bigint</c> ids the batch inserts into a root table; see <see cref="DeclareInsertedRoots(TableName, IReadOnlyList{int})" />.</summary>
        /// <param name="table">The root table, whose primary key is <c>PK_&lt;table&gt;</c> on <c>[Id]</c>.</param>
        /// <param name="ids">The ids inserted.</param>
        internal void DeclareInsertedRoots(TableName table, IReadOnlyList<long> ids)
        {
            ArgumentNullException.ThrowIfNull(table);
            ArgumentNullException.ThrowIfNull(ids);
            EnsureComposing();
            if (Probe is null && ids.Count > 0)
            {
                Probe = CommitProbe.ForIds(table, ids, _executor.Binder);
            }
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

        /// <summary>Reports the users whose user-level tags the bump epilogue moved, from its reader.</summary>
        /// <param name="affected">Each user, with the tag moved for them.</param>
        internal void ReportAffectedUsers(IReadOnlyList<AffectedUser> affected)
        {
            AffectedUsers = affected ?? throw new ArgumentNullException(nameof(affected));
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

        /// <summary>
        ///     Assembles the batch into one framed command: a writing persist batch that carries the
        ///     connect prologue opens its transaction with the re-checks of what the prologue confirmed.
        /// </summary>
        /// <returns>The assembly.</returns>
        /// <exception cref="InvalidOperationException">
        ///     A transaction-head statement is appended to a batch that opens no transaction in its text.
        /// </exception>
        internal BatchAssembly Assemble()
        {
            bool inTextTransaction = TransactionMode == TransactionMode.Auto && _written.Count > 0;

            // A save's guard holds its locks to the commit and its statements stand or fall together.
            if (_needsTransaction && TransactionMode == TransactionMode.None)
            {
                throw new InvalidOperationException(
                    $"The batch carries a save, whose statements run in one transaction; a batch in {nameof(TransactionMode.None)} mode runs them in autocommit. Leave the batch in {nameof(TransactionMode.Auto)} mode.");
            }

            // A transaction head (an application lock) is held to the COMMIT of the transaction the text
            // opens; an explicit transaction would hold it past the text, autocommit not at all.
            if (!inTextTransaction && _statements.Any(static statement => statement.Section == BatchSection.TransactionHead))
            {
                throw new InvalidOperationException(
                    $"A transaction-head statement needs the transaction the batch text opens, which only a batch in {nameof(TransactionMode.Auto)} mode that declares a write has.");
            }

            // --- The re-checks: on a writing persist batch that carries the connect prologue, whatever its transaction mode ---
            PersistChecks? checks = Purpose == BatchPurpose.Persist && Premises is GuardPremises premises && _written.Count > 0
                ? PersistChecks.Compose(premises, _dependencies, Bumps, _executor.Binder)
                : null;

            return BatchAssembly.Create(_statements, _executor.Fingerprint, CarriesConnectPrologue, inTextTransaction, checks);
        }

        // ===================== Helpers =====================

        /// <summary>The refusal of a member whose statement compiler is not composed in this build.</summary>
        /// <param name="member">The member's name.</param>
        /// <returns>The exception.</returns>
        private static NotSupportedException NotAvailable(string member)
        {
            return new NotSupportedException($"{nameof(IDataBatch)}.{member} is not available: its statement compiler is not part of this build.");
        }

        /// <summary>The bumps of the writes declared outside the connect prologue's own statements, and of the explicit bumps.</summary>
        /// <returns>The plan.</returns>
        private VersionTagBumpPlan ResolveBumps()
        {
            HashSet<TableName> written = [.. _statements.Where(statement => !_connectStatements.Contains(statement.Ordinal)).SelectMany(static statement => statement.Writes)];
            return VersionTagBumpPlan.Resolve(_executor.VersionTags, written, _bumps);
        }

        /// <summary>
        ///     Refuses a write to a table with a tag where no bump can cover it: in a prologue statement
        ///     other than the connect prologue's own, which runs outside the batch's transaction, or after
        ///     the bump statement, which then no longer runs last and has read its users already.
        /// </summary>
        /// <param name="draft">The statement's draft.</param>
        /// <param name="written">The tables it writes.</param>
        /// <exception cref="InvalidOperationException">The statement writes such a table there.</exception>
        private void CheckTaggedWritePlacement(StatementDraft draft, FrozenSet<TableName> written)
        {
            bool prologue = draft.Section == BatchSection.Prologue && !_connectStatements.Contains(draft.Ordinal);
            if (written.Count == 0 || !(prologue || _bumpAppended))
            {
                return;
            }

            var plan = VersionTagBumpPlan.Resolve(_executor.VersionTags, written, FrozenSet<string>.Empty);
            if (plan.IsEmpty)
            {
                return;
            }

            throw new InvalidOperationException(prologue
                ? $"A prologue statement writes a table whose writes bump version tags ({Describe(plan)}). The prologue runs before the batch's transaction, where no bump covers it; such a write belongs in the body."
                : $"A statement appended after the bump writes a table whose writes bump version tags ({Describe(plan)}). The bump is the last statement before the commit; such a write belongs in the body, or in an epilogue that runs before the bump.");
        }

        /// <summary>A plan's names and rules, for a refusal's message.</summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The description.</returns>
        private static string Describe(VersionTagBumpPlan plan)
        {
            IEnumerable<string> items = plan.TenantNames.Concat(plan.UserRules.Select(static rule => $"{rule.Column} of the users {rule.Table} names"));
            return items.Any() ? string.Join(", ", items) : "nothing";
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

        /// <summary>The refusal of a batch carrying a save whose reservation already gave its ids back.</summary>
        /// <returns>The exception.</returns>
        private static InvalidOperationException ReturnedReservation()
        {
            return new InvalidOperationException(
                "A save of the batch holds ids whose reservation was disposed before the batch executed, so the ids went back to the buffer. Keep the reservation until the batch that saves its rows executes.");
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

        /// <summary>
        ///     Refuses a raw write to a table with a user-level tag rule that does not say which users it
        ///     wrote: their cached permissions or preferences would never be invalidated.
        /// </summary>
        /// <param name="options">The statement's options.</param>
        /// <exception cref="ArgumentException">The statement writes such a table without <see cref="SqlOptions.UserIds" />.</exception>
        private void CheckUserIds(SqlOptions options)
        {
            if (options.UserIds is not null || options.Writes.Count == 0 || _executor.VersionTags is not IVersionTagRegistry registry)
            {
                return;
            }

            if (registry.Resolve(options.Writes).UserRules is [UserVersionTagRule rule, ..])
            {
                throw new ArgumentException(
                    $"The statement writes {rule.Table}, whose writes bump the {rule.Column} tag of each user they name, but it does not say which users it wrote. Pass their ids in SqlOptions.UserIds, or SqlOptions.ForCaller for the caller's own rows.",
                    nameof(options));
            }
        }

        /// <summary>
        ///     Whether a statement's holes name a batch table that statements may fill: one a
        ///     <see cref="DeclareIdTable" /> call or the batch itself declared. Table-valued parameters
        ///     are read-only and do not count.
        /// </summary>
        /// <param name="arguments">The statement's hole values.</param>
        /// <returns>True when one does.</returns>
        private bool NamesMutableBatchTable(object?[] arguments)
        {
            foreach (object? argument in arguments)
            {
                if (argument is SqlIdentifier { IsVariable: true } identifier && !_parameterNames.Contains(identifier.Name))
                {
                    return true;
                }
            }

            return false;
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
