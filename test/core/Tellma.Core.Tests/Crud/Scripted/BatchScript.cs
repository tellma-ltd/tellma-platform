// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Tests.Crud.Scripted
{
    /// <summary>
    ///     The canned answers of a unit test's database: a response per statement kind (and entity
    ///     type, for the typed statements), the batches it handed out, and the round trips they made.
    /// </summary>
    /// <remarks>
    ///     A response is a function of the recorded statement, so it can read the statement's
    ///     query, spec, text or key set and answer accordingly; registering a response for a kind
    ///     again replaces the earlier one. The script is safe to use from concurrent tasks.
    /// </remarks>
    internal sealed class BatchScript
    {
        /// <summary>Guards the batches and the round-trip count.</summary>
        private readonly Lock _gate = new();

        /// <summary>The responses, by statement kind and entity type.</summary>
        private readonly Dictionary<(ScriptedKind Kind, Type? Entity), Func<ScriptedStatement, object>> _responses = [];

        /// <summary>The batches handed out, in order.</summary>
        private readonly List<ScriptedBatch> _batches = [];

        /// <summary>Runs inside each execution, before any result is completed; none by default.</summary>
        private Func<ScriptedBatch, CancellationToken, Task>? _execute;

        /// <summary>The round trips the batches made.</summary>
        private int _roundTrips;

        /// <summary>The tenant the outcomes report.</summary>
        public int TenantId { get; init; } = 1;

        /// <summary>The stack-owned tables a caller statement may write; none by default.</summary>
        public IReadOnlySet<TableName> CallerAuthority { get; init; } = new HashSet<TableName>();

        /// <summary>The batches handed out, in order.</summary>
        public IReadOnlyList<ScriptedBatch> Batches
        {
            get
            {
                lock (_gate)
                {
                    return [.. _batches];
                }
            }
        }

        /// <summary>The round trips the batches made: one per execution.</summary>
        public int RoundTrips
        {
            get
            {
                lock (_gate)
                {
                    return _roundTrips;
                }
            }
        }

        /// <summary>Hands out a new composing batch answered by this script.</summary>
        /// <param name="purpose">What the batch is for.</param>
        /// <returns>The batch.</returns>
        public ScriptedBatch NewBatch(BatchPurpose purpose = BatchPurpose.Validate)
        {
            lock (_gate)
            {
                ScriptedBatch batch = new(this, _batches.Count + 1, purpose);
                _batches.Add(batch);
                return batch;
            }
        }

        /// <summary>Answers the entity queries of one entity type.</summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="respond">The entities a query reads.</param>
        /// <returns>This script, for chaining.</returns>
        public BatchScript OnQuery<T>(Func<ScriptedStatement, IReadOnlyList<T>> respond)
            where T : class
        {
            return On(ScriptedKind.Query, typeof(T), respond);
        }

        /// <summary>Answers the columnar queries.</summary>
        /// <param name="respond">The page a query reads.</param>
        /// <returns>This script, for chaining.</returns>
        public BatchScript OnRows(Func<ScriptedStatement, RowPage> respond)
        {
            return On(ScriptedKind.Rows, entity: null, respond);
        }

        /// <summary>Answers the capped counts.</summary>
        /// <param name="respond">The count a statement reads, the cap plus one meaning more.</param>
        /// <returns>This script, for chaining.</returns>
        public BatchScript OnCount(Func<ScriptedStatement, int> respond)
        {
            return On(ScriptedKind.Count, entity: null, respond);
        }

        /// <summary>Answers the rows of path-free expressions.</summary>
        /// <param name="respond">The row a statement reads.</param>
        /// <returns>This script, for chaining.</returns>
        public BatchScript OnConstant(Func<ScriptedStatement, QueryRowSet> respond)
        {
            return On(ScriptedKind.EvaluateConstant, entity: null, respond);
        }

        /// <summary>Answers the raw statements.</summary>
        /// <param name="respond">The result sets a statement reads.</param>
        /// <returns>This script, for chaining.</returns>
        public BatchScript OnSql(Func<ScriptedStatement, RawResult> respond)
        {
            return On(ScriptedKind.Sql, entity: null, respond);
        }

        /// <summary>Answers the saves of one entity type.</summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="respond">The receipt of a save.</param>
        /// <returns>This script, for chaining.</returns>
        public BatchScript OnSave<T>(Func<ScriptedStatement, SaveReceipt> respond)
            where T : class
        {
            return On(ScriptedKind.Save, typeof(T), respond);
        }

        /// <summary>Answers the updates of one entity type.</summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="respond">The receipt of an update.</param>
        /// <returns>This script, for chaining.</returns>
        public BatchScript OnUpdate<T>(Func<ScriptedStatement, UpdateReceipt> respond)
            where T : class
        {
            return On(ScriptedKind.Update, typeof(T), respond);
        }

        /// <summary>Answers the deletes of one entity type.</summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="respond">The receipt of a delete.</param>
        /// <returns>This script, for chaining.</returns>
        public BatchScript OnDelete<T>(Func<ScriptedStatement, DeleteReceipt> respond)
            where T : class
        {
            return On(ScriptedKind.Delete, typeof(T), respond);
        }

        /// <summary>Answers the cached-list reads of one entity type.</summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="respond">The cache's result.</param>
        /// <returns>This script, for chaining.</returns>
        public BatchScript OnFromCache<T>(Func<ScriptedStatement, CacheResult<CachedEntitySet<T>>> respond)
            where T : class
        {
            return On(ScriptedKind.FromCache, typeof(T), respond);
        }

        /// <summary>
        ///     Runs inside every execution, after the batch is sealed and before any result is
        ///     completed: to hold a round trip open, observe the cancellation, or fail the batch.
        /// </summary>
        /// <param name="execute">The hook; it receives the executing batch.</param>
        /// <returns>This script, for chaining.</returns>
        public BatchScript OnExecute(Func<ScriptedBatch, CancellationToken, Task> execute)
        {
            ArgumentNullException.ThrowIfNull(execute);
            lock (_gate)
            {
                _execute = execute;
            }

            return this;
        }

        /// <summary>Whether the script answers a statement.</summary>
        /// <param name="statement">The statement.</param>
        /// <returns>True when it does.</returns>
        internal bool Answers(ScriptedStatement statement)
        {
            lock (_gate)
            {
                return _responses.ContainsKey((statement.Kind, statement.Entity));
            }
        }

        /// <summary>The script's answer to a statement.</summary>
        /// <typeparam name="TResult">The answer's type.</typeparam>
        /// <param name="statement">The statement.</param>
        /// <returns>The answer.</returns>
        internal TResult Answer<TResult>(ScriptedStatement statement)
        {
            Func<ScriptedStatement, object> respond;
            lock (_gate)
            {
                respond = _responses[(statement.Kind, statement.Entity)];
            }

            return (TResult)respond(statement);
        }

        /// <summary>Counts one round trip.</summary>
        internal void CountRoundTrip()
        {
            lock (_gate)
            {
                _roundTrips++;
            }
        }

        /// <summary>Runs the execution hook, when there is one.</summary>
        /// <param name="batch">The executing batch.</param>
        /// <param name="cancellationToken">The execution's cancellation.</param>
        /// <returns>The hook's task.</returns>
        internal Task ExecuteHookAsync(ScriptedBatch batch, CancellationToken cancellationToken)
        {
            Func<ScriptedBatch, CancellationToken, Task>? execute;
            lock (_gate)
            {
                execute = _execute;
            }

            return execute?.Invoke(batch, cancellationToken) ?? Task.CompletedTask;
        }

        /// <summary>Registers a response.</summary>
        /// <typeparam name="TResult">The answer's type.</typeparam>
        /// <param name="kind">The statement kind.</param>
        /// <param name="entity">The entity type; null for an untyped kind.</param>
        /// <param name="respond">The response.</param>
        /// <returns>This script, for chaining.</returns>
        private BatchScript On<TResult>(ScriptedKind kind, Type? entity, Func<ScriptedStatement, TResult> respond)
        {
            ArgumentNullException.ThrowIfNull(respond);
            lock (_gate)
            {
                _responses[(kind, entity)] = statement => respond(statement)!;
            }

            return this;
        }
    }
}
