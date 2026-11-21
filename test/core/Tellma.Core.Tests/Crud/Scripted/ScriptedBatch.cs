// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Crud.Scripted
{
    /// <summary>
    ///     A batch with no database: it records every call in append order and, when it executes,
    ///     completes each statement's result with what its script answers for it, so a unit test drives
    ///     the pipeline's composition and reads back exactly what it appended.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Executing a batch holding a statement the script has no response for fails before any
    ///         result is completed, naming the statement, so a test never reads a value nobody
    ///         scripted. Statements without a result (a table-valued parameter, an id table, an
    ///         assertion, a dependency, a tag bump) need no response.
    ///     </para>
    ///     <para>
    ///         The double keeps no SQL semantics: identical read statements are recorded twice rather
    ///         than collapsed, an assertion never fails on its own, and no version tag is checked. The
    ///         one authority it checks is the script's owned tables against a raw statement's declared
    ///         writes, as a real batch refuses a write outside the appender's authority. A script that
    ///         needs a failure throws it from its execution hook.
    ///     </para>
    /// </remarks>
    internal sealed class ScriptedBatch : IDataBatch, IBatchKeyTables, IBatchNamedTables, IBatchAuthority, IBatchDependencies, IBatchOrdinals
    {
        /// <summary>The script that answers the statements.</summary>
        private readonly BatchScript _script;

        /// <summary>Every call, in append order.</summary>
        private readonly List<ScriptedStatement> _statements = [];

        /// <summary>The completion of each statement that has a result, in append order.</summary>
        private readonly List<(ScriptedStatement Statement, Action<ScriptedStatement> Complete)> _results = [];

        /// <summary>The rows of each table-valued parameter, by its name.</summary>
        private readonly Dictionary<string, IReadOnlyList<object>> _tvps = new(StringComparer.Ordinal);

        /// <summary>Whether a deferred recording claimed the batch's recording names.</summary>
        private bool _recordingNamesClaimed;

        /// <summary>The commit callbacks, in registration order.</summary>
        private readonly List<Action<BatchOutcome>> _committed = [];

        /// <summary>The key-capture table of each rows handle whose statement captured its keys.</summary>
        private readonly Dictionary<object, SqlIdentifier> _captured = new(ReferenceEqualityComparer.Instance);

        /// <summary>The union of the raw statements' declared writes.</summary>
        private readonly HashSet<TableName> _written = [];

        /// <summary>Creates a batch; a script creates its batches through <see cref="BatchScript.NewBatch" />.</summary>
        /// <param name="script">The script that answers the statements.</param>
        /// <param name="number">The batch's 1-based number among the script's batches.</param>
        /// <param name="purpose">What the batch is for.</param>
        internal ScriptedBatch(BatchScript script, int number, BatchPurpose purpose)
        {
            _script = script;
            Number = number;
            Purpose = purpose;
        }

        /// <summary>The batch's 1-based number among its script's batches.</summary>
        public int Number { get; }

        /// <summary>Every call, in append order.</summary>
        public IReadOnlyList<ScriptedStatement> Statements => _statements;

        /// <inheritdoc />
        public IReadOnlyList<VersionTagDependency> Dependencies =>
            [.. Of(ScriptedKind.DependsOn).SelectMany(static statement => (IReadOnlyList<VersionTagDependency>)statement.Statement!)];

        /// <summary>The calls that append a statement with a result, in append order: what a round trip would carry.</summary>
        public IReadOnlyList<ScriptedStatement> Results => [.. _results.Select(static result => result.Statement)];

        /// <summary>Whether the batch has executed.</summary>
        public bool IsExecuted { get; private set; }

        /// <inheritdoc />
        public BatchPurpose Purpose { get; }

        /// <inheritdoc />
        public TransactionMode TransactionMode
        {
            get;
            set
            {
                EnsureComposing();
                field = value;
            }
        } = TransactionMode.Auto;

        /// <inheritdoc />
        public bool IsComposing => !IsExecuted;

        /// <inheritdoc />
        public QueryexLimits Limits
        {
            get;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                EnsureComposing();
                field = value;
            }
        } = QueryexLimits.Default;

        /// <inheritdoc />
        public IReadOnlySet<TableName> WrittenTables => _written;

        /// <inheritdoc />
        /// <remarks>The script's authority until the pipeline sets the current appender's.</remarks>
        public IReadOnlySet<TableName> CallerAuthority
        {
            get => field ?? _script.CallerAuthority;
            private set;
        }

        /// <summary>Whether the batch executed without its guard passing, so nothing it held ran.</summary>
        public bool IsRefused { get; private set; }

        /// <summary>The rows bound to a table-valued parameter this batch declared.</summary>
        /// <typeparam name="TRow">The row type.</typeparam>
        /// <param name="name">The parameter's name.</param>
        /// <returns>The rows.</returns>
        /// <exception cref="KeyNotFoundException">The batch declared no parameter of that name.</exception>
        public IReadOnlyList<TRow> TvpRows<TRow>(string name)
            where TRow : class
        {
            return _tvps.TryGetValue(name, out IReadOnlyList<object>? rows)
                ? [.. rows.Cast<TRow>()]
                : throw new KeyNotFoundException($"Batch {Number} declared no table-valued parameter named {name}.");
        }

        /// <summary>The calls of one kind, in append order.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The calls.</returns>
        public IReadOnlyList<ScriptedStatement> Of(ScriptedKind kind)
        {
            return [.. _statements.Where(statement => statement.Kind == kind)];
        }

        /// <inheritdoc />
        public BatchResult<EntityQueryResult<TEntity>> Query<TEntity>(EntityQuery<TEntity> query)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(query);
            BatchResult<EntityQueryResult<TEntity>> result = new();
            ScriptedStatement statement = Append(ScriptedKind.Query, typeof(TEntity), query, query.Arguments, options: null, name: null);
            Expect(statement, recorded => result.Complete(new EntityQueryResult<TEntity>(_script.Answer<IReadOnlyList<TEntity>>(recorded), new RelatedEntities())));
            return result;
        }

        /// <inheritdoc />
        public BatchResult<RowPage> Rows(QuerySpec spec, QueryArguments? arguments, RowQueryOptions? options)
        {
            ArgumentNullException.ThrowIfNull(spec);
            BatchResult<RowPage> result = new();
            ScriptedStatement statement = Append(ScriptedKind.Rows, entity: null, spec, arguments, options, name: null);
            Expect(statement, recorded => result.Complete(_script.Answer<RowPage>(recorded)));
            if (options is { CaptureKeys: true } or { IncludeAncestors: true })
            {
                // Named as the platform's batch names a captured table: the statement's ordinal, then the word.
                _captured[result] = new SqlIdentifier(string.Create(CultureInfo.InvariantCulture, $"@tb{statement.Ordinal}_keys"));
            }

            return result;
        }

        /// <inheritdoc />
        public BatchResult<int> Count(QuerySpec spec, QueryArguments? arguments, int cap = 10000)
        {
            ArgumentNullException.ThrowIfNull(spec);
            BatchResult<int> result = new();
            ScriptedStatement statement = Append(ScriptedKind.Count, entity: null, spec, arguments, cap, name: null);
            Expect(statement, recorded => result.Complete(_script.Answer<int>(recorded)));
            return result;
        }

        /// <inheritdoc />
        public BatchResult<QueryRowSet> EvaluateConstant(IReadOnlyList<ConstantItem> items, int queryexVersion)
        {
            ArgumentNullException.ThrowIfNull(items);
            BatchResult<QueryRowSet> result = new();
            ScriptedStatement statement = Append(ScriptedKind.EvaluateConstant, entity: null, items, arguments: null, queryexVersion, name: null);
            Expect(statement, recorded => result.Complete(_script.Answer<QueryRowSet>(recorded)));
            return result;
        }

        /// <inheritdoc />
        public SaveHandle Save<TEntity>(IReadOnlyList<TEntity> rows, ConcurrencyMode concurrency = ConcurrencyMode.Check)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            BatchResult<SaveReceipt> receipt = new();
            ScriptedStatement statement = Append(ScriptedKind.Save, typeof(TEntity), rows, arguments: null, concurrency, name: null);
            DeclareWrites(typeof(TEntity));
            Expect(statement, recorded => receipt.Complete(_script.Answer<SaveReceipt>(recorded)));
            string prefix = string.Create(CultureInfo.InvariantCulture, $"@save{statement.Ordinal}_");
            return new SaveHandle(receipt, new SqlIdentifier(prefix + "saved"), new SqlIdentifier(prefix + "new"), new SqlIdentifier(prefix + "touched"), []);
        }

        /// <inheritdoc />
        public BatchResult<UpdateReceipt> Update<TEntity>(UpdateSpec<TEntity> spec)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(spec);
            BatchResult<UpdateReceipt> result = new();
            ScriptedStatement statement = Append(ScriptedKind.Update, typeof(TEntity), spec, arguments: null, options: null, name: null);
            DeclareWrites(typeof(TEntity));
            Expect(statement, recorded => result.Complete(_script.Answer<UpdateReceipt>(recorded)));
            return result;
        }

        /// <inheritdoc />
        public DeleteHandle Delete<TEntity>(DeleteSpec<TEntity> spec)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(spec);
            BatchResult<DeleteReceipt> receipt = new();
            ScriptedStatement statement = Append(ScriptedKind.Delete, typeof(TEntity), spec, arguments: null, options: null, name: null);
            DeclareWrites(typeof(TEntity));
            Expect(statement, recorded => receipt.Complete(_script.Answer<DeleteReceipt>(recorded)));
            return new DeleteHandle(receipt, new SqlIdentifier(string.Create(CultureInfo.InvariantCulture, $"@delete{statement.Ordinal}_deleted")), []);
        }

        /// <summary>Records the tables the script says a typed write of an entity type declares, as the platform's batch records its own statements' writes.</summary>
        /// <param name="entity">The entity type written.</param>
        private void DeclareWrites(Type entity)
        {
            if (_script.Writes is { } writes)
            {
                _written.UnionWith(writes(entity));
            }
        }

        /// <inheritdoc />
        public void Assert(QuerySpec countSpec, QueryArguments? arguments, int expected, int errorNumber, string code)
        {
            ArgumentNullException.ThrowIfNull(countSpec);
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            _ = Append(ScriptedKind.Assert, entity: null, countSpec, arguments, (expected, errorNumber), code);
        }

        /// <inheritdoc />
        public BatchResult<RawResult> Sql(FormattableString sql, SqlOptions? options)
        {
            ArgumentNullException.ThrowIfNull(sql);
            BatchResult<RawResult> result = new();

            // --- A declared write of a table the script says a stack owns, outside the appender's authority ---
            foreach (TableName table in options?.Writes.Where(_script.Owners.ContainsKey) ?? [])
            {
                if (!CallerAuthority.Contains(table))
                {
                    throw new InvalidOperationException(
                        $"The statement writes {table}, which the stack '{_script.Owners[table]}' owns and the current caller may not write. Enlist the write with '{_script.Owners[table]}', so it runs under that stack's authority.");
                }
            }

            ScriptedStatement statement = Append(ScriptedKind.Sql, entity: null, sql, arguments: null, options, name: null);
            if (options is not null)
            {
                _written.UnionWith(options.Writes);
            }

            Expect(statement, recorded => result.Complete(_script.Answer<RawResult>(recorded)));
            return result;
        }

        /// <inheritdoc />
        public SqlIdentifier Tvp<TRow>(IReadOnlyList<TRow> rows)
            where TRow : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            EnsureComposing();
            string name = string.Create(CultureInfo.InvariantCulture, $"@tvp{_statements.Count}");
            _tvps.Add(name, [.. rows]);
            _ = Append(ScriptedKind.Tvp, typeof(TRow), rows, arguments: null, options: null, name);
            return new SqlIdentifier(name);
        }

        /// <inheritdoc />
        public SqlIdentifier Tvp<TRow>(IReadOnlyList<TRow> rows, string name)
            where TRow : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            EnsureComposing();
            _tvps.Add(name, [.. rows]);
            _ = Append(ScriptedKind.Tvp, typeof(TRow), rows, arguments: null, options: null, name);
            return new SqlIdentifier(name);
        }

        /// <inheritdoc />
        public void ClaimRecordingNames()
        {
            EnsureComposing();
            if (_recordingNamesClaimed)
            {
                throw new InvalidOperationException("The scripted batch already takes the tables of a deferred recording.");
            }

            _recordingNamesClaimed = true;
        }

        /// <inheritdoc />
        public SqlIdentifier DeclareIdTable()
        {
            return DeclareKeyTable(typeof(int));
        }

        /// <inheritdoc />
        /// <remarks>The statement's options hold the keys' type.</remarks>
        public SqlIdentifier DeclareKeyTable(Type keyType, string name)
        {
            ArgumentNullException.ThrowIfNull(keyType);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            EnsureComposing();
            _ = Append(ScriptedKind.DeclareIdTable, entity: null, statement: null, arguments: null, keyType, name);
            return new SqlIdentifier(name);
        }

        /// <inheritdoc />
        /// <remarks>The statement's options hold the keys' type.</remarks>
        public SqlIdentifier DeclareKeyTable(Type keyType)
        {
            ArgumentNullException.ThrowIfNull(keyType);
            EnsureComposing();
            string name = string.Create(CultureInfo.InvariantCulture, $"@ids{_statements.Count}");
            _ = Append(ScriptedKind.DeclareIdTable, entity: null, statement: null, arguments: null, keyType, name);
            return new SqlIdentifier(name);
        }

        /// <inheritdoc />
        public SqlIdentifier? KeysOf(object handle)
        {
            ArgumentNullException.ThrowIfNull(handle);
            return _captured.GetValueOrDefault(handle);
        }

        /// <inheritdoc />
        public void DependsOn(IReadOnlyList<VersionTagDependency> dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            _ = Append(ScriptedKind.DependsOn, entity: null, dependencies, arguments: null, options: null, name: null);
        }

        /// <inheritdoc />
        public BatchResult<CacheResult<CachedEntitySet<TEntity>>> FromCache<TEntity>()
            where TEntity : class
        {
            BatchResult<CacheResult<CachedEntitySet<TEntity>>> result = new();
            ScriptedStatement statement = Append(ScriptedKind.FromCache, typeof(TEntity), statement: null, arguments: null, options: null, name: null);
            Expect(statement, recorded => result.Complete(_script.Answer<CacheResult<CachedEntitySet<TEntity>>>(recorded)));
            return result;
        }

        /// <inheritdoc />
        public void BumpVersionTag(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            _ = Append(ScriptedKind.BumpVersionTag, entity: null, statement: null, arguments: null, options: null, name);
        }

        /// <inheritdoc />
        public void OnCommitted(Action<BatchOutcome> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            EnsureComposing();
            _committed.Add(callback);
        }

        /// <inheritdoc />
        public async Task<BatchOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            if (IsExecuted)
            {
                throw new InvalidOperationException($"Batch {Number} already executed; a batch executes once.");
            }

            IsExecuted = true;

            // --- Every result statement is scripted, or none is completed ---
            ScriptedStatement? unscripted = _results.Select(static result => result.Statement).FirstOrDefault(statement => !_script.Answers(statement));
            if (unscripted is not null)
            {
                throw new InvalidOperationException($"Batch {Number} holds {unscripted}, which its script has no response for. Script a response for every statement the test expects.");
            }

            // --- The round trip: the script's hook may wait, observe the cancellation or fail ---
            _script.CountRoundTrip();
            await _script.ExecuteHookAsync(this, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            foreach ((ScriptedStatement statement, Action<ScriptedStatement> complete) in _results)
            {
                complete(statement);
            }

            BatchOutcome outcome = new(new VersionTagSnapshot(_script.TenantId, new Dictionary<string, VersionTag>(StringComparer.Ordinal), DateTimeOffset.UnixEpoch), null, RoundTrips: 1);
            foreach (Action<BatchOutcome> callback in _committed)
            {
                callback(outcome);
            }

            return outcome;
        }

        /// <inheritdoc />
        public void SetCallerAuthority(IReadOnlySet<TableName> authority)
        {
            ArgumentNullException.ThrowIfNull(authority);
            CallerAuthority = authority;
        }

        /// <inheritdoc />
        /// <remarks>The ordinal is the next call's position among the batch's calls, its <see cref="ScriptedStatement.Ordinal" />.</remarks>
        public int NextOrdinal()
        {
            EnsureComposing();
            return _statements.Count;
        }

        /// <summary>
        ///     Executes the batch as one whose guard failed: the round trip is counted, and no statement
        ///     runs, so no result completes and no commit callback runs.
        /// </summary>
        /// <returns>A task that completes when the round trip was counted.</returns>
        /// <exception cref="InvalidOperationException">The batch already executed.</exception>
        public Task RefuseAsync()
        {
            if (IsExecuted)
            {
                throw new InvalidOperationException($"Batch {Number} already executed; a batch executes once.");
            }

            IsExecuted = true;
            IsRefused = true;
            _script.CountRoundTrip();
            return Task.CompletedTask;
        }

        /// <summary>Records a call.</summary>
        /// <param name="kind">What was called.</param>
        /// <param name="entity">The entity or row type; null for none.</param>
        /// <param name="statement">What it was called with.</param>
        /// <param name="arguments">The query arguments; null for none.</param>
        /// <param name="options">The options; null for none.</param>
        /// <param name="name">The name declared or named; null for none.</param>
        /// <returns>The recorded call.</returns>
        private ScriptedStatement Append(ScriptedKind kind, Type? entity, object? statement, QueryArguments? arguments, object? options, string? name)
        {
            EnsureComposing();
            ScriptedStatement recorded = new(this, _statements.Count, kind, entity, statement, arguments, options, name);
            _statements.Add(recorded);
            return recorded;
        }

        /// <summary>Registers the completion of a statement's result, run when the batch executes.</summary>
        /// <param name="statement">The statement.</param>
        /// <param name="complete">Completes its result from the script's response.</param>
        private void Expect(ScriptedStatement statement, Action<ScriptedStatement> complete)
        {
            _results.Add((statement, complete));
        }

        /// <summary>Refuses a call after the batch executed, as a real batch does.</summary>
        /// <exception cref="InvalidOperationException">The batch executed.</exception>
        private void EnsureComposing()
        {
            if (IsExecuted)
            {
                throw new InvalidOperationException($"Batch {Number} already executed and takes no more statements.");
            }
        }
    }
}
