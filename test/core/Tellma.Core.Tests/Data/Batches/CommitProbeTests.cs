// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Tests.Data.Batches
{
    /// <summary>
    ///     The commit probe: its verdicts, its one autocommit statement over the distinct ids, how it
    ///     recognises the re-run's collision with the probed rows' primary key, and which of its own
    ///     failures it runs again after.
    /// </summary>
    public sealed class CommitProbeTests : IDisposable
    {
        private static readonly TableName Jobs = new("core", "Jobs");

        /// <summary>Three re-runs after waits of 1, 4 and 16 ms before jitter.</summary>
        private static readonly DataOptions Retries = new() { RetryAttempts = 3, RetryBaseDelay = TimeSpan.FromMilliseconds(1) };

        /// <summary>An attempt that answered: nothing committed.</summary>
        private static readonly DataExecutor.ProbeAttempt NotCommitted = new(CommitVerdict.NotCommitted, Failure: null, FailureKind.Final, TimedOut: false);

        private readonly BatchHarness _harness = new();

        [Theory]
        [InlineData(3, 3, CommitVerdict.Committed)]
        [InlineData(0, 3, CommitVerdict.NotCommitted)]
        [InlineData(1, 3, CommitVerdict.Unknown)]
        [InlineData(4, 3, CommitVerdict.Unknown)]
        public void A_count_of_all_none_or_part_of_the_rows_gives_its_verdict(long found, int expected, CommitVerdict verdict)
        {
            Assert.Equal(verdict, CommitProbe.Verdict(found, expected));
        }

        [Fact]
        public void The_probe_counts_the_distinct_ids_under_a_locking_read_through_one_table_valued_parameter()
        {
            var probe = CommitProbe.ForIds(Jobs, [5, 5, 7], _harness.Executor.Binder);

            Assert.Equal(2, probe.Count);
            Assert.Equal("SELECT COUNT_BIG(*) FROM [core].[Jobs] WITH (READCOMMITTEDLOCK, FORCESEEK) WHERE [Id] IN (SELECT [Id] FROM @tb0_t0);", probe.Text);

            SqlParameter parameter = probe.Parameter();
            Assert.Equal(("@tb0_t0", SqlDbType.Structured), (parameter.ParameterName, parameter.SqlDbType));

            // --- The bigint overload binds the other id list ---
            var big = CommitProbe.ForIds(Jobs, [5L], _harness.Executor.Binder);
            Assert.NotEqual(parameter.TypeName, big.Parameter().TypeName);

            // --- An empty list declares nothing to probe for ---
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => CommitProbe.ForIds(Jobs, Array.Empty<int>(), _harness.Executor.Binder));
        }

        [Fact]
        public void Only_a_duplicate_of_the_probed_tables_primary_key_is_the_first_attempts_commit()
        {
            var probe = CommitProbe.ForIds(Jobs, [1001], _harness.Executor.Binder);

            Assert.True(probe.IsOwnPrimaryKeyViolation(Error(2627, "Violation of PRIMARY KEY constraint 'PK_Jobs'. Cannot insert duplicate key in object 'core.Jobs'. The duplicate key value is (1001).")));
            Assert.False(probe.IsOwnPrimaryKeyViolation(Error(2627, "Violation of PRIMARY KEY constraint 'PK_Widgets'. Cannot insert duplicate key in object 'fixture.Widgets'. The duplicate key value is (1).")));
            Assert.False(probe.IsOwnPrimaryKeyViolation(Error(2601, "Cannot insert duplicate key row in object 'core.Jobs' with unique index 'PK_Jobs'. The duplicate key value is (1001).")));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_probe_that_fails_transiently_or_loses_its_connection_runs_again_after_a_batchs_waits(bool connectionLost)
        {
            FailureKind kind = connectionLost ? FailureKind.Ambiguous : FailureKind.Reported;

            Probed probed = await ProbeAsync(attempt => attempt < 3 ? Failed(kind) : NotCommitted, TestContext.Current.CancellationToken);

            // --- The third attempt answers, after the waits of the first two re-runs ---
            Assert.Equal((CommitVerdict.NotCommitted, null, 3), (probed.Answer.Verdict, probed.Answer.Failure, probed.Attempts));
            Assert.Equal([RetryRules.Delay(1, Retries.RetryBaseDelay, 0.5), RetryRules.Delay(2, Retries.RetryBaseDelay, 0.5)], probed.Waits);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_probe_that_fails_finally_or_by_a_defect_answers_unknown_at_once(bool defect)
        {
            DataExecutor.ProbeAttempt failed = Failed(defect ? FailureKind.Defect : FailureKind.Final);

            Probed probed = await ProbeAsync(_ => failed, TestContext.Current.CancellationToken);

            Assert.Equal((CommitVerdict.Unknown, failed.Failure, 1), (probed.Answer.Verdict, probed.Answer.Failure, probed.Attempts));
            Assert.Empty(probed.Waits);
        }

        [Fact]
        public async Task A_probe_that_keeps_failing_transiently_answers_unknown_once_the_attempts_run_out()
        {
            Probed probed = await ProbeAsync(_ => Failed(FailureKind.Reported), TestContext.Current.CancellationToken);

            Assert.Equal((CommitVerdict.Unknown, Retries.RetryAttempts + 1), (probed.Answer.Verdict, probed.Attempts));
            Assert.Equal(Retries.RetryAttempts, probed.Waits.Count);
        }

        [Fact]
        public async Task A_probe_that_times_out_runs_again_once_as_a_read_does()
        {
            Probed probed = await ProbeAsync(_ => Failed(FailureKind.Ambiguous, timedOut: true), TestContext.Current.CancellationToken);

            Assert.Equal((CommitVerdict.Unknown, RetryRules.ReadTimeoutReruns + 1), (probed.Answer.Verdict, probed.Attempts));
        }

        [Fact]
        public async Task A_probe_whose_wait_the_caller_cancels_answers_unknown_without_running_again()
        {
            Probed probed = await ProbeAsync(_ => Failed(FailureKind.Reported), new CancellationToken(canceled: true));

            Assert.Equal((CommitVerdict.Unknown, 1), (probed.Answer.Verdict, probed.Attempts));
            Assert.NotNull(probed.Answer.Failure);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A failed probe attempt.</summary>
        /// <param name="kind">The failure's kind.</param>
        /// <param name="timedOut">Whether it was the command timeout.</param>
        /// <returns>The attempt.</returns>
        private static DataExecutor.ProbeAttempt Failed(FailureKind kind, bool timedOut = false)
        {
            return new DataExecutor.ProbeAttempt(CommitVerdict.Unknown, new InvalidOperationException("The probe's round trip failed."), kind, timedOut);
        }

        /// <summary>Runs the probe's retry loop over scripted attempts, with the jitter fixed at its midpoint.</summary>
        /// <param name="outcome">Each attempt's outcome, given the attempt from 1.</param>
        /// <param name="cancellationToken">The caller's token.</param>
        /// <returns>The answer, the attempts made and the waits between them.</returns>
        private static async Task<Probed> ProbeAsync(Func<int, DataExecutor.ProbeAttempt> outcome, CancellationToken cancellationToken)
        {
            int attempts = 0;
            List<TimeSpan> waits = [];
            DataExecutor.ProbeAttempt answer = await DataExecutor.ProbeUntilAnsweredAsync(
                attempt =>
                {
                    attempts = attempt;
                    return Task.FromResult(outcome(attempt));
                },
                Retries,
                TimeProvider.System,
                static () => 0.5,
                (_, _, wait) => waits.Add(wait),
                cancellationToken);
            return new Probed(answer, attempts, waits);
        }

        /// <summary>A server error on line 1 of no procedure.</summary>
        /// <param name="number">The number.</param>
        /// <param name="message">The message.</param>
        /// <returns>The error.</returns>
        private static SqlErrorInfo Error(int number, string message)
        {
            return new SqlErrorInfo(number, 14, message, null, 1);
        }

        /// <summary>What the probe's retry loop did.</summary>
        /// <param name="Answer">The attempt it returned.</param>
        /// <param name="Attempts">The attempts it made.</param>
        /// <param name="Waits">The waits before each re-run.</param>
        private sealed record Probed(DataExecutor.ProbeAttempt Answer, int Attempts, List<TimeSpan> Waits);
    }
}
