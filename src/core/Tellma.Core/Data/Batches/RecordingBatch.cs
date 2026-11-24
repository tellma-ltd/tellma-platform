// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Batches
{
    // Spec 0015 §5.1: every participant of a persist appends through a recording view of the batch.
    // On the first entry each call reaches the batch at once and is recorded; on a re-entered compose
    // the pipeline replays the recording into the new batch at its original position, so no
    // participant runs twice. The append order is the first entry's, so every batch-local name a
    // participant holds is unchanged, and every result a participant holds reads the batch that
    // executes: the view hands out results of its own and completes them from that batch. An action's
    // method composes before its persist batch exists (spec 0015 §11.2): a deferred view records its
    // appends without a batch and composes them all on the first replay. A batch-local table it is
    // asked for is named by the view itself, in the once-per-batch namespace, and declared under that
    // name on every replay, so the statements after it name it alike wherever the recording lands.
    // An append that answers at compose time (a save's or a delete's handle, a cache read) has no
    // batch to answer it, so a deferred view refuses it.
    /// <summary>
    ///     A recording view of a persist batch for its participants: every append reaches the batch
    ///     and is recorded, so the recording can be replayed into a recomposed batch without calling
    ///     the participants again. Append-only: the participants neither execute the batch nor change
    ///     its mode or limits.
    /// </summary>
    internal sealed class RecordingBatch : IDataBatch, IBatchKeyTables, IBatchProvenCommit
    {
        /// <summary>The recorded appends, in order, each replaying itself onto a batch.</summary>
        private readonly List<Action<IDataBatch>> _replays = [];

        /// <summary>The results handed out, each completed from the result of the batch that executes.</summary>
        private readonly List<ILink> _links = [];

        /// <summary>The batch's own result behind each result handed out, for its key-capture table.</summary>
        private readonly Dictionary<object, object> _sources = new(ReferenceEqualityComparer.Instance);

        /// <summary>Receives a participant's commit callback, or proven-commit announcement, that failed; null to let the batch's runner log it.</summary>
        private readonly Action<Delegate, Exception>? _callbackFailed;

        /// <summary>Refuses an append from a flow that may compose no more; null refuses none.</summary>
        private readonly Action? _guard;

        /// <summary>The batch-local tables a deferred view named so far.</summary>
        private int _named;

        /// <summary>Whether the participants returned, so the view takes no more appends.</summary>
        private bool _sealed;

        /// <summary>The batch the appends reach; null for a deferred view before its first replay.</summary>
        private IDataBatch? _inner;

        /// <summary>Creates a view over a persist batch being composed.</summary>
        /// <param name="inner">The batch.</param>
        /// <param name="callbackFailed">
        ///     Receives a participant's commit callback or proven-commit announcement that failed, which
        ///     then fails no other; null to let the batch's runner log it.
        /// </param>
        public RecordingBatch(IDataBatch inner, Action<Delegate, Exception>? callbackFailed = null)
        {
            ArgumentNullException.ThrowIfNull(inner);
            _inner = inner;
            _callbackFailed = callbackFailed;
            CompleteOnCommit(inner);
        }

        /// <summary>Creates a deferred view, whose appends reach no batch until its first replay.</summary>
        /// <param name="callbackFailed">Receives a commit callback or proven-commit announcement that failed; null to let the batch's runner log it.</param>
        /// <param name="guard">Refuses an append from a flow that may compose no more; null refuses none.</param>
        private RecordingBatch(Action<Delegate, Exception>? callbackFailed, Action? guard)
        {
            _callbackFailed = callbackFailed;
            _guard = guard;
        }

        /// <summary>The batch the appends reach: the first entry's, then each replay's.</summary>
        /// <exception cref="InvalidOperationException">A deferred view that was never replayed.</exception>
        public IDataBatch Inner => _inner ?? throw new InvalidOperationException("The deferred recording reaches a batch only once it is replayed onto one.");

        /// <summary>Whether the view records without a batch until its first replay.</summary>
        public bool IsDeferred => _inner is null;

        /// <summary>
        ///     Creates a deferred view: its appends are recorded without reaching a batch, and composed
        ///     onto the batch of its first replay, then again onto each later one.
        /// </summary>
        /// <param name="callbackFailed">Receives a commit callback or proven-commit announcement that failed, which then fails no other; null to let the batch's runner log it.</param>
        /// <param name="guard">Runs before every append, and refuses one by throwing; null refuses none.</param>
        /// <returns>The view.</returns>
        public static RecordingBatch Deferred(Action<Delegate, Exception>? callbackFailed = null, Action? guard = null)
        {
            return new RecordingBatch(callbackFailed, guard);
        }

        /// <summary>The appends recorded.</summary>
        public int Recorded => _replays.Count;

        /// <inheritdoc />
        public BatchPurpose Purpose => _inner?.Purpose ?? BatchPurpose.Persist;

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">Set by a participant: the pipeline owns the mode.</exception>
        public TransactionMode TransactionMode
        {
            get => _inner?.TransactionMode ?? TransactionMode.Auto;
            set => throw new InvalidOperationException("A persist participant appends to the operation's batch; its transaction mode is the pipeline's.");
        }

        /// <inheritdoc />
        public bool IsComposing => !_sealed && (_inner?.IsComposing ?? true);

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">Set by a participant: the pipeline owns the limits.</exception>
        public QueryexLimits Limits
        {
            get => _inner?.Limits ?? QueryexProfiles.PipelineLimits;
            set => throw new InvalidOperationException("A persist participant appends to the operation's batch; its compilation limits are the pipeline's.");
        }

        /// <inheritdoc />
        public IReadOnlySet<TableName> WrittenTables => _inner?.WrittenTables ?? NoTables;

        /// <inheritdoc />
        public IReadOnlySet<TableName> CallerAuthority => _inner?.CallerAuthority ?? NoTables;

        /// <summary>The tables of a deferred view before its first replay: none written, none authorised yet.</summary>
        private static IReadOnlySet<TableName> NoTables { get; } = new HashSet<TableName>();

        /// <inheritdoc />
        public BatchResult<EntityQueryResult<TEntity>> Query<TEntity>(EntityQuery<TEntity> query)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(query);
            return RecordResult(inner => inner.Query(query));
        }

        /// <inheritdoc />
        public BatchResult<RowPage> Rows(QuerySpec spec, QueryArguments? arguments, RowQueryOptions? options)
        {
            ArgumentNullException.ThrowIfNull(spec);
            return RecordResult(inner => inner.Rows(spec, arguments, options));
        }

        /// <inheritdoc />
        public BatchResult<int> Count(QuerySpec spec, QueryArguments? arguments, int cap = 10000)
        {
            ArgumentNullException.ThrowIfNull(spec);
            return RecordResult(inner => inner.Count(spec, arguments, cap));
        }

        /// <inheritdoc />
        public BatchResult<QueryRowSet> EvaluateConstant(IReadOnlyList<ConstantItem> items, int queryexVersion)
        {
            ArgumentNullException.ThrowIfNull(items);
            return RecordResult(inner => inner.EvaluateConstant(items, queryexVersion));
        }

        /// <inheritdoc />
        public SaveHandle Save<TEntity>(IReadOnlyList<TEntity> rows, ConcurrencyMode concurrency = ConcurrencyMode.Check)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            EnsureOpen();
            EnsureBatch(nameof(Save));

            // --- The first entry's handle, its receipt replaced by one the view completes ---
            SaveHandle first = Inner.Save(rows, concurrency);
            Link<SaveReceipt> receipt = HandOut(first.Receipt);
            SaveHandle handle = first with { Receipt = receipt.Proxy };
            _replays.Add(inner =>
            {
                SaveHandle again = inner.Save(rows, concurrency);
                EnsureSame(first.SavedIds, again.SavedIds);
                EnsureSame(first.NewIds, again.NewIds);
                EnsureSame(first.TouchedIds, again.TouchedIds);
                EnsureSame(first.Captures, again.Captures);
                receipt.Source = again.Receipt;
            });
            return handle;
        }

        /// <inheritdoc />
        public BatchResult<UpdateReceipt> Update<TEntity>(UpdateSpec<TEntity> spec)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(spec);
            return RecordResult(inner => inner.Update(spec));
        }

        /// <inheritdoc />
        public DeleteHandle Delete<TEntity>(DeleteSpec<TEntity> spec)
            where TEntity : class
        {
            ArgumentNullException.ThrowIfNull(spec);
            EnsureOpen();
            EnsureBatch(nameof(Delete));

            // --- As a save: the first entry's handle with a receipt of the view's ---
            DeleteHandle first = Inner.Delete(spec);
            Link<DeleteReceipt> receipt = HandOut(first.Receipt);
            DeleteHandle handle = first with { Receipt = receipt.Proxy };
            _replays.Add(inner =>
            {
                DeleteHandle again = inner.Delete(spec);
                EnsureSame(first.DeletedIds, again.DeletedIds);
                EnsureSame(first.Captures, again.Captures);
                receipt.Source = again.Receipt;
            });
            return handle;
        }

        /// <inheritdoc />
        public void Assert(QuerySpec countSpec, QueryArguments? arguments, int expected, int errorNumber, string code)
        {
            ArgumentNullException.ThrowIfNull(countSpec);
            Record(inner => inner.Assert(countSpec, arguments, expected, errorNumber, code));
        }

        /// <inheritdoc />
        public BatchResult<RawResult> Sql(FormattableString sql, SqlOptions? options)
        {
            ArgumentNullException.ThrowIfNull(sql);

            // The text was checked where IDataBatch.Sql was called, and reaches the batch unchanged.
#pragma warning disable TELLMA0003
            return RecordResult(inner => inner.Sql(sql, options));
#pragma warning restore TELLMA0003
        }

        /// <inheritdoc />
        public SqlIdentifier Tvp<TRow>(IReadOnlyList<TRow> rows)
            where TRow : class
        {
            ArgumentNullException.ThrowIfNull(rows);
            return RecordName(inner => inner.Tvp(rows), (inner, name) => NamedTablesOf(inner).Tvp(rows, name), "t");
        }

        /// <inheritdoc />
        public SqlIdentifier DeclareIdTable()
        {
            return DeclareKeyTable(typeof(int));
        }

        /// <inheritdoc />
        public SqlIdentifier DeclareKeyTable(Type keyType)
        {
            ArgumentNullException.ThrowIfNull(keyType);
            return RecordName(inner => KeyTablesOf(inner).DeclareKeyTable(keyType), (inner, name) => NamedTablesOf(inner).DeclareKeyTable(keyType, name), "ids");
        }

        /// <inheritdoc />
        public SqlIdentifier? KeysOf(object handle)
        {
            ArgumentNullException.ThrowIfNull(handle);

            // A query, not an append: answered from the batch's own result behind the one handed out;
            // a deferred view holds no batch result before its first replay.
            return _inner is IDataBatch inner && _sources.TryGetValue(handle, out object? source) ? KeyTablesOf(inner).KeysOf(source) : null;
        }

        /// <inheritdoc />
        public void DependsOn(IReadOnlyList<VersionTagDependency> dependencies)
        {
            ArgumentNullException.ThrowIfNull(dependencies);
            IReadOnlyList<VersionTagDependency> held = [.. dependencies];
            Record(inner => inner.DependsOn(held));
        }

        /// <inheritdoc />
        public BatchResult<CacheResult<CachedEntitySet<TEntity>>> FromCache<TEntity>()
            where TEntity : class
        {
            // A hit answers at once, so the participant may have composed on it: a replay must hit the
            // same version, or what it composed rests on a list that moved. A deferred view answers
            // nothing at once, so it refuses the read rather than hand out a result that never hits.
            EnsureBatch(nameof(FromCache));
            return RecordResult(
                static inner => inner.FromCache<TEntity>(),
                static (first, again) => first.Tag == again.Tag && first.Outcome == again.Outcome);
        }

        /// <inheritdoc />
        public void BumpVersionTag(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            Record(inner => inner.BumpVersionTag(name));
        }

        /// <inheritdoc />
        public void OnCommitted(Action<BatchOutcome> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            if (_callbackFailed is not Action<Delegate, Exception> failed)
            {
                Record(inner => inner.OnCommitted(callback));
                return;
            }

            // --- A failure is the operation's to log and count, by the component that registered it ---
            Record(inner => inner.OnCommitted(outcome =>
            {
                try
                {
                    callback(outcome);
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    failed(callback, failure);
                }
            }));
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Recorded like a commit callback, so every replay registers it on the batch that executes.
        ///     A batch that takes no such announcement, such as a test's, ignores it.
        /// </remarks>
        public void OnCommitProven(Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            if (_callbackFailed is not Action<Delegate, Exception> failed)
            {
                Record(inner => (inner as IBatchProvenCommit)?.OnCommitProven(callback));
                return;
            }

            // --- A failure is the operation's to log and count, by the component that registered it ---
            Record(inner => (inner as IBatchProvenCommit)?.OnCommitProven(() =>
            {
                try
                {
                    callback();
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    failed(callback, failure);
                }
            }));
        }

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">Always: the pipeline executes the persist batch.</exception>
        public Task<BatchOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("A persist participant appends to the operation's batch; the pipeline executes it once every participant returned.");
        }

        /// <summary>
        ///     Records an append the platform composes itself on the view's batch, such as a job row or
        ///     a notification: it runs at once, and again on every replay.
        /// </summary>
        /// <param name="append">Appends to a batch.</param>
        /// <exception cref="InvalidOperationException">The participants already returned.</exception>
        internal void Record(Action<IDataBatch> append)
        {
            ArgumentNullException.ThrowIfNull(append);
            EnsureOpen();
            _guard?.Invoke();
            if (_inner is IDataBatch inner)
            {
                append(inner);
            }

            _replays.Add(append);
        }

        /// <summary>Records an append the platform composes itself, whose result the view hands out in its own.</summary>
        /// <typeparam name="T">The result's value.</typeparam>
        /// <param name="append">Appends to a batch and returns that batch's result.</param>
        /// <param name="same">
        ///     Whether a replay's result answered at compose time as the first entry's did; by default
        ///     the values are equal. Only a result answered at compose time is compared.
        /// </param>
        /// <returns>The view's result, completed from the result of the batch that executes.</returns>
        /// <exception cref="InvalidOperationException">The participants already returned.</exception>
        internal BatchResult<T> RecordResult<T>(Func<IDataBatch, BatchResult<T>> append, Func<T, T, bool>? same = null)
        {
            ArgumentNullException.ThrowIfNull(append);
            EnsureOpen();
            _guard?.Invoke();

            // A deferred view hands out a result no batch answers until the first replay.
            Link<T> link = _inner is IDataBatch inner ? HandOut(append(inner)) : Pending<T>();
            _replays.Add(inner =>
            {
                BatchResult<T> again = append(inner);
                if (link.Proxy.IsCompleted && !(again.IsCompleted && (same ?? EqualityComparer<T>.Default.Equals)(link.Proxy.Value, again.Value)))
                {
                    // The participant may have composed on what the first entry answered, which moved.
                    throw new StaleContextException([]);
                }

                link.Source = again;
                _sources[link.Proxy] = again;
            });
            if (link.Source is BatchResult<T> source)
            {
                _sources[link.Proxy] = source;
            }

            return link.Proxy;
        }

        /// <summary>Takes no more appends: the participants returned.</summary>
        internal void Seal()
        {
            _sealed = true;
        }

        /// <summary>Replays every recorded append onto a recomposed batch, in the first entry's order.</summary>
        /// <param name="inner">The recomposed batch, at the position the first entry recorded from.</param>
        /// <exception cref="InvalidOperationException">
        ///     A replayed append named a batch-local table differently than the first entry did, or the
        ///     batch took another deferred recording's named tables already.
        /// </exception>
        /// <exception cref="StaleContextException">A result the first entry answered at compose time answers otherwise now.</exception>
        internal void Replay(IDataBatch inner)
        {
            ArgumentNullException.ThrowIfNull(inner);
            _inner = inner;

            // --- A deferred view's table names count from zero in every recording, so one batch takes
            // the tables of one recording; a second is refused before it declares any ---
            if (_named > 0)
            {
                NamedTablesOf(inner).ClaimRecordingNames();
            }

            CompleteOnCommit(inner);
            foreach (Action<IDataBatch> replay in _replays)
            {
                replay(inner);
            }
        }

        /// <summary>Completes every result handed out from the batch's own, before the participants' commit callbacks read them.</summary>
        /// <param name="inner">The batch.</param>
        private void CompleteOnCommit(IDataBatch inner)
        {
            inner.OnCommitted(_ =>
            {
                foreach (ILink link in _links)
                {
                    link.Complete();
                }
            });
        }

        /// <summary>Records an append that names a batch-local table, which a replay must name alike.</summary>
        /// <param name="append">Appends to a batch and returns the name the batch chose.</param>
        /// <param name="named">Appends to a batch under a name the view chose, for a deferred view.</param>
        /// <param name="word">The table's word in the name a deferred view chooses.</param>
        /// <returns>The first entry's name.</returns>
        private SqlIdentifier RecordName(Func<IDataBatch, SqlIdentifier> append, Func<IDataBatch, string, SqlIdentifier> named, string word)
        {
            EnsureOpen();
            _guard?.Invoke();
            if (_inner is IDataBatch inner)
            {
                SqlIdentifier first = append(inner);
                _replays.Add(again => EnsureSame(first, append(again)));
                return first;
            }

            // --- A deferred view names the table itself, in the once-per-batch namespace no
            // statement's ordinal enters, and every replay declares it under that name ---
            string name = string.Create(CultureInfo.InvariantCulture, $"{ReservedSqlNames.RecordingPrefix}{_named++}_{word}");
            SqlIdentifier chosen = new(name);
            _replays.Add(again => EnsureSame(chosen, named(again, name)));
            return chosen;
        }

        /// <summary>Hands out a result of the view's own for one of the batch's, completed at once when the batch's already is.</summary>
        /// <typeparam name="T">The result's value.</typeparam>
        /// <param name="source">The batch's result.</param>
        /// <returns>The link.</returns>
        private Link<T> HandOut<T>(BatchResult<T> source)
        {
            Link<T> link = new(source.IsCompleted ? new BatchResult<T>(source.Value) : new BatchResult<T>()) { Source = source };
            _links.Add(link);
            return link;
        }

        /// <summary>Hands out a result of the view's own that no batch answers yet: a deferred view's, completed once a replayed batch commits.</summary>
        /// <typeparam name="T">The result's value.</typeparam>
        /// <returns>The link, its source set by the first replay.</returns>
        private Link<T> Pending<T>()
        {
            Link<T> link = new(new BatchResult<T>()) { Source = null };
            _links.Add(link);
            return link;
        }

        /// <summary>The platform's key-table channel of a batch.</summary>
        /// <param name="batch">The batch.</param>
        /// <returns>The channel.</returns>
        /// <exception cref="InvalidOperationException">The batch is not one the platform composes.</exception>
        private static IBatchKeyTables KeyTablesOf(IDataBatch batch)
        {
            return batch as IBatchKeyTables
                ?? throw new InvalidOperationException("The batch is not one the platform composes, so it declares no key tables of its own.");
        }

        /// <summary>The caller-named tables of a batch, which a deferred view declares its tables through.</summary>
        /// <param name="batch">The batch.</param>
        /// <returns>The channel.</returns>
        /// <exception cref="InvalidOperationException">The batch is not one the platform composes.</exception>
        private static IBatchNamedTables NamedTablesOf(IDataBatch batch)
        {
            return batch as IBatchNamedTables
                ?? throw new InvalidOperationException("The batch is not one the platform composes, so it declares no table under a name of the caller's.");
        }

        /// <summary>Refuses an append once the participants returned.</summary>
        /// <exception cref="InvalidOperationException">The participants already returned.</exception>
        private void EnsureOpen()
        {
            if (_sealed)
            {
                throw new InvalidOperationException(
                    "The persist batch takes a participant's statements only while the participant is called. Append them from ContributeAsync, the action method or OnPersist, never after it returned.");
            }
        }

        /// <summary>Refuses, on a deferred view, an append that answers at compose time: no batch exists to answer it.</summary>
        /// <param name="append">The append, for the message.</param>
        /// <exception cref="InvalidOperationException">The view is deferred.</exception>
        private void EnsureBatch(string append)
        {
            if (_inner is null)
            {
                throw new InvalidOperationException(
                    $"An action's batch takes {append} only once its persist batch exists, after the validation rounds. Write the stack's rows through the context's Save and Update.");
            }
        }

        /// <summary>Refuses a replay that names a batch-local table otherwise than the first entry: the recomposed batch drifted.</summary>
        /// <param name="first">The first entry's name.</param>
        /// <param name="again">The replay's name.</param>
        /// <exception cref="InvalidOperationException">The names differ.</exception>
        private static void EnsureSame(SqlIdentifier first, SqlIdentifier again)
        {
            if (first != again)
            {
                throw Drift(first.Name, again.Name);
            }
        }

        /// <summary>Refuses a replay whose captures differ from the first entry's.</summary>
        /// <param name="first">The first entry's captures.</param>
        /// <param name="again">The replay's captures.</param>
        /// <exception cref="InvalidOperationException">The captures differ.</exception>
        private static void EnsureSame(IReadOnlyList<ColumnCapture> first, IReadOnlyList<ColumnCapture> again)
        {
            if (!first.SequenceEqual(again))
            {
                throw Drift(string.Join(", ", first.Select(static capture => capture.Identifier.Name)), string.Join(", ", again.Select(static capture => capture.Identifier.Name)));
            }
        }

        /// <summary>The defect of a recomposed persist that appends its statements otherwise than its first entry.</summary>
        /// <param name="first">What the first entry named.</param>
        /// <param name="again">What the replay named.</param>
        /// <returns>The exception.</returns>
        private static InvalidOperationException Drift(string first, string again)
        {
            return new InvalidOperationException(
                $"The recomposed persist batch named '{again}' where its first entry named '{first}', so a participant's recorded statements would address the wrong tables. A persist's compose appends the same statements in the same order on every entry.");
        }

        /// <summary>A result handed out, completed from the batch that executes.</summary>
        private interface ILink
        {
            /// <summary>Completes the result handed out from the batch's own, when that one completed.</summary>
            void Complete();
        }

        /// <summary>A result of the view's own and the batch's result it is completed from.</summary>
        /// <typeparam name="T">The result's value.</typeparam>
        /// <param name="proxy">The result handed out.</param>
        private sealed class Link<T>(BatchResult<T> proxy) : ILink
        {
            /// <summary>The result handed out.</summary>
            public BatchResult<T> Proxy { get; } = proxy;

            /// <summary>The batch's own result: the first entry's, then each replay's; null for a deferred view's before its first replay.</summary>
            public required BatchResult<T>? Source { get; set; }

            /// <inheritdoc />
            public void Complete()
            {
                if (!Proxy.IsCompleted && Source is { IsCompleted: true } source)
                {
                    Proxy.Complete(source.Value);
                }
            }
        }
    }
}
