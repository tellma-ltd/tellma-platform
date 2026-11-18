// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Data.Batches
{
    /// <summary>
    ///     The recording view a persist's participants append through: every append reaches the batch
    ///     at once and is recorded, a replay appends the recording onto a recomposed batch in order
    ///     and completes the results handed out from it, a replay that names a table otherwise or
    ///     whose compose-time answer moved is refused, and the view neither executes the batch nor
    ///     changes its mode or limits.
    /// </summary>
    public sealed class RecordingBatchTests
    {
        /// <summary>The script that answers the batches.</summary>
        private readonly BatchScript _script = new();

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task An_append_reaches_the_batch_at_once_and_its_result_completes_when_the_batch_commits()
        {
            RawResult answered = new([]);
            _ = _script.OnSql(_ => answered);
            ScriptedBatch batch = _script.NewBatch(BatchPurpose.Persist);
            RecordingBatch view = new(batch);

            BatchResult<RawResult> result = view.Sql($"SELECT 1", SqlOptions.None);
            view.BumpVersionTag("entity:ps.PsCategory");

            Assert.Equal([ScriptedKind.Sql, ScriptedKind.BumpVersionTag], batch.Statements.Select(static statement => statement.Kind));
            Assert.Equal(2, view.Recorded);
            Assert.False(result.IsCompleted);

            _ = await batch.ExecuteAsync(Token);

            Assert.Same(answered, result.Value);
        }

        [Fact]
        public async Task A_replay_appends_the_recording_onto_the_recomposed_batch_in_order_and_completes_the_results_from_it()
        {
            Dictionary<int, RawResult> answers = new() { [1] = new([]), [2] = new([]) };
            _ = _script
                .OnSql(statement => answers[statement.Batch.Number])
                .OnSave<Row>(static statement => new SaveReceipt(DateTimeOffset.UnixEpoch.AddDays(statement.Batch.Number), 1, 0, 0));
            ScriptedBatch first = _script.NewBatch(BatchPurpose.Persist);
            RecordingBatch view = new(first);
            SaveHandle saved = view.Save([new Row()]);
            SqlIdentifier ids = view.DeclareIdTable();
            BatchResult<RawResult> read = view.Sql($"SELECT Id FROM {ids}", SqlOptions.None);
            view.Seal();

            // --- The first batch's guard fails, so nothing it held ran ---
            await first.RefuseAsync();
            ScriptedBatch second = _script.NewBatch(BatchPurpose.Persist);
            view.Replay(second);
            _ = await second.ExecuteAsync(Token);

            Assert.Equal(first.Statements.Select(static statement => statement.Kind), second.Statements.Select(static statement => statement.Kind));
            Assert.Same(second, view.Inner);
            Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(2), saved.Receipt.Value.Stamp);
            Assert.Same(answers[2], read.Value);
        }

        [Fact]
        public void A_replay_that_names_a_batch_local_table_otherwise_than_the_first_entry_is_refused()
        {
            ScriptedBatch first = _script.NewBatch(BatchPurpose.Persist);
            RecordingBatch view = new(first);
            _ = view.DeclareIdTable();
            view.Seal();

            // The recomposed batch appends one statement more ahead of the replay, so the names shift.
            ScriptedBatch second = _script.NewBatch(BatchPurpose.Persist);
            _ = second.DeclareIdTable();
            InvalidOperationException drifted = Assert.Throws<InvalidOperationException>(() => view.Replay(second));

            Assert.Contains("recomposed persist batch named", drifted.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_result_answered_at_compose_time_must_answer_alike_on_a_replay_or_the_replay_is_stale()
        {
            ScriptedBatch first = _script.NewBatch(BatchPurpose.Persist);
            int version = 1;
            RecordingBatch view = new(first);
            BatchResult<int> answered = view.RecordResult(_ => new BatchResult<int>(version));
            BatchResult<int> pending = view.RecordResult(static _ => new BatchResult<int>());
            view.Seal();

            // --- The same answer replays; the result handed out was completed at once ---
            Assert.Equal((true, 1, false), (answered.IsCompleted, answered.Value, pending.IsCompleted));
            view.Replay(_script.NewBatch(BatchPurpose.Persist));

            // --- A moved answer: what the participant composed on rests on a value that changed ---
            version = 2;
            StaleContextException stale = Assert.Throws<StaleContextException>(() => view.Replay(_script.NewBatch(BatchPurpose.Persist)));
            Assert.Empty(stale.Dependencies);
        }

        [Fact]
        public async Task The_view_takes_no_append_once_sealed_and_never_executes_the_batch_or_changes_its_mode_or_limits()
        {
            ScriptedBatch batch = _script.NewBatch(BatchPurpose.Persist);
            RecordingBatch view = new(batch);

            _ = Assert.Throws<InvalidOperationException>(() => view.TransactionMode = TransactionMode.None);
            _ = Assert.Throws<InvalidOperationException>(() => view.Limits = QueryexLimits.Default);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => view.ExecuteAsync(Token));
            Assert.True(view.IsComposing);

            view.Seal();

            Assert.False(view.IsComposing);
            InvalidOperationException late = Assert.Throws<InvalidOperationException>(() => view.BumpVersionTag("entity:ps.PsCategory"));
            Assert.Contains("only while the participant is called", late.Message, StringComparison.Ordinal);
            _ = Assert.Throws<InvalidOperationException>(() => view.Record(static inner => inner.BumpVersionTag("entity:ps.PsCategory")));
            Assert.Empty(batch.Statements);
        }

        [Fact]
        public async Task A_commit_callback_that_fails_is_reported_with_the_callback_and_fails_no_other()
        {
            List<(Action<BatchOutcome> Callback, Exception Failure)> failed = [];
            ScriptedBatch batch = _script.NewBatch(BatchPurpose.Persist);
            RecordingBatch view = new(batch, (callback, failure) => failed.Add((callback, failure)));
            InvalidOperationException thrown = new("The callback failed.");
            bool ran = false;
            Action<BatchOutcome> failing = _ => throw thrown;

            view.OnCommitted(failing);
            view.OnCommitted(_ => ran = true);
            _ = await batch.ExecuteAsync(Token);

            (Action<BatchOutcome> callback, Exception failure) = Assert.Single(failed);
            Assert.Same(failing, callback);
            Assert.Same(thrown, failure);
            Assert.True(ran);
        }

        // --- A deferred view: an action's method composes before its persist batch exists ---

        [Fact]
        public async Task A_deferred_view_records_without_a_batch_and_composes_the_recording_onto_its_first_replay_whose_commit_completes_the_results()
        {
            RawResult answered = new([]);
            _ = _script.OnSql(_ => answered);
            var view = RecordingBatch.Deferred();

            // --- Before any batch: the appends are recorded, their results pending ---
            BatchResult<RawResult> result = view.Sql($"SELECT 1", SqlOptions.None);
            view.BumpVersionTag("entity:ps.PsCategory");
            Assert.True(view.IsDeferred);
            Assert.Equal((2, BatchPurpose.Persist, true), (view.Recorded, view.Purpose, view.IsComposing));
            Assert.Same(QueryexProfiles.PipelineLimits, view.Limits);
            Assert.Empty(view.WrittenTables);
            _ = Assert.Throws<InvalidOperationException>(() => view.Inner);
            Assert.False(result.IsCompleted);
            view.Seal();

            // --- The first replay composes every append, in order; its commit completes the result ---
            ScriptedBatch batch = _script.NewBatch(BatchPurpose.Persist);
            view.Replay(batch);
            Assert.False(view.IsDeferred);
            Assert.Equal([ScriptedKind.Sql, ScriptedKind.BumpVersionTag], batch.Statements.Select(static statement => statement.Kind));
            Assert.False(result.IsCompleted);

            _ = await batch.ExecuteAsync(Token);

            Assert.Same(answered, result.Value);
        }

        [Fact]
        public void A_deferred_view_refuses_an_append_that_answers_a_handle_or_a_result_at_compose_time()
        {
            var view = RecordingBatch.Deferred();

            InvalidOperationException save = Assert.Throws<InvalidOperationException>(() => view.Save([new Row()]));
            _ = Assert.Throws<InvalidOperationException>(() => view.Delete(new DeleteSpec<Row>.ByIds([1], null, null, null)));
            _ = Assert.Throws<InvalidOperationException>(view.FromCache<Row>);

            Assert.Contains("persist batch exists", save.Message, StringComparison.Ordinal);
            Assert.Equal(0, view.Recorded);
        }

        [Fact]
        public void A_deferred_view_names_its_tables_itself_and_every_replay_declares_them_under_those_names_wherever_the_recording_lands()
        {
            var view = RecordingBatch.Deferred();
            SqlIdentifier rows = view.Tvp([new Row { Id = 1 }]);
            SqlIdentifier ids = view.DeclareIdTable();
            SqlIdentifier keys = view.DeclareKeyTable(typeof(long));
            _ = view.Sql($"UPDATE [x] SET [y] = 1 WHERE [Id] IN (SELECT [Id] FROM {rows});", new SqlOptions(userIds: rows));
            view.Seal();

            // --- The names are the view's, in the once-per-batch namespace ---
            Assert.Equal(["@tm_rec0_t", "@tm_rec1_ids", "@tm_rec2_ids"], new[] { rows, ids, keys }.Select(static name => name.Name));

            // --- Two replays, each landing after a different number of statements, declare the same names ---
            ScriptedBatch first = _script.NewBatch(BatchPurpose.Persist);
            view.Replay(first);
            ScriptedBatch second = _script.NewBatch(BatchPurpose.Persist);
            _ = second.DeclareIdTable();
            view.Replay(second);

            static string[] Names(ScriptedBatch batch)
            {
                return [.. batch.Statements.Where(static statement => statement.Kind is ScriptedKind.Tvp or ScriptedKind.DeclareIdTable).Select(static statement => statement.Name!).TakeLast(3)];
            }

            Assert.Equal(["@tm_rec0_t", "@tm_rec1_ids", "@tm_rec2_ids"], Names(first));
            Assert.Equal(Names(first), Names(second));
        }

        [Fact]
        public void A_deferred_views_guard_runs_before_every_append_and_refuses_one_by_throwing()
        {
            bool refusing = false;
            var view = RecordingBatch.Deferred(guard: () =>
            {
                if (refusing)
                {
                    throw new InvalidOperationException("Refused.");
                }
            });
            _ = view.Sql($"SELECT 1", SqlOptions.None);
            refusing = true;

            _ = Assert.Throws<InvalidOperationException>(() => view.Sql($"SELECT 2", SqlOptions.None));
            _ = Assert.Throws<InvalidOperationException>(view.DeclareIdTable);
            _ = Assert.Throws<InvalidOperationException>(() => view.BumpVersionTag("entity:ps.PsCategory"));
            Assert.Equal(1, view.Recorded);
        }

        /// <summary>A row the tests save.</summary>
        private sealed class Row
        {
            /// <summary>The key.</summary>
            public int Id { get; set; }
        }
    }
}
