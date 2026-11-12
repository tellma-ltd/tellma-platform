// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Tests.Data.Batches
{
    /// <summary>
    ///     The retry rules: what each failure kind leads to before and after the send, in each
    ///     transaction mode, with and without a commit probe; and the jittered exponential waits.
    /// </summary>
    public sealed class RetryRulesTests
    {
        [Theory]

        // --- Before the send nothing ran: only a transient failure is repeated ---
        [InlineData(nameof(FailureKind.Reported), false, TransactionMode.Auto, false, false, false, false, nameof(RetryAction.Rerun))]
        [InlineData(nameof(FailureKind.Reported), false, TransactionMode.Explicit, false, false, false, true, nameof(RetryAction.Rerun))]
        [InlineData(nameof(FailureKind.Final), false, TransactionMode.Auto, false, false, true, true, nameof(RetryAction.Surface))]

        // --- An explicit transaction is never re-run ---
        [InlineData(nameof(FailureKind.Reported), true, TransactionMode.Explicit, false, true, true, true, nameof(RetryAction.GiveUp))]
        [InlineData(nameof(FailureKind.Ambiguous), true, TransactionMode.Explicit, false, true, true, true, nameof(RetryAction.GiveUp))]
        [InlineData(nameof(FailureKind.Final), true, TransactionMode.Explicit, false, true, true, true, nameof(RetryAction.Surface))]
        [InlineData(nameof(FailureKind.Defect), true, TransactionMode.Explicit, false, false, false, true, nameof(RetryAction.Surface))]
        [InlineData(nameof(FailureKind.Ambiguous), true, TransactionMode.Explicit, true, true, true, true, nameof(RetryAction.Unknown))]

        // --- A reported failure rolled back under XACT_ABORT: the whole round trip runs again ---
        [InlineData(nameof(FailureKind.Reported), true, TransactionMode.Auto, false, false, false, true, nameof(RetryAction.Rerun))]
        [InlineData(nameof(FailureKind.Reported), true, TransactionMode.None, false, false, false, true, nameof(RetryAction.Rerun))]

        // --- A lost connection: re-run what may run twice, else probe, else unknown ---
        [InlineData(nameof(FailureKind.Ambiguous), true, TransactionMode.Auto, false, true, false, false, nameof(RetryAction.Rerun))]
        [InlineData(nameof(FailureKind.Ambiguous), true, TransactionMode.Auto, false, false, true, true, nameof(RetryAction.Probe))]
        [InlineData(nameof(FailureKind.Ambiguous), true, TransactionMode.Auto, false, false, false, true, nameof(RetryAction.Unknown))]
        [InlineData(nameof(FailureKind.Ambiguous), true, TransactionMode.None, false, false, false, true, nameof(RetryAction.Unknown))]

        // --- A reader's defect after the send: never re-run; a writing batch may have committed ---
        [InlineData(nameof(FailureKind.Defect), true, TransactionMode.Auto, false, true, false, false, nameof(RetryAction.Surface))]
        [InlineData(nameof(FailureKind.Defect), true, TransactionMode.Auto, false, false, true, true, nameof(RetryAction.Probe))]
        [InlineData(nameof(FailureKind.Defect), true, TransactionMode.Auto, false, false, false, true, nameof(RetryAction.Unknown))]

        // --- A final error surfaces ---
        [InlineData(nameof(FailureKind.Final), true, TransactionMode.Auto, false, true, true, true, nameof(RetryAction.Surface))]
        public void Each_failure_leads_to_its_action(
            string kind, bool sent, TransactionMode mode, bool committing, bool mayRunTwice, bool probe, bool writes, string expected)
        {
            // The kinds and actions are named: the test method is public, they are not.
            Assert.Equal(Enum.Parse<RetryAction>(expected), RetryRules.Decide(Enum.Parse<FailureKind>(kind), sent, mode, committing, mayRunTwice, probe, writes));
        }

        [Fact]
        public void The_waits_grow_fourfold_from_the_base_and_vary_by_a_quarter_either_way()
        {
            var baseDelay = TimeSpan.FromMilliseconds(50);

            // --- The centre of the jitter: 50, 200 and 800 ms ---
            Assert.Equal(
                [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800)],
                Enumerable.Range(1, 3).Select(retry => RetryRules.Delay(retry, baseDelay, 0.5)));

            // --- The edges: three quarters at the low end, just under five quarters at the high ---
            Assert.Equal(TimeSpan.FromMilliseconds(37.5), RetryRules.Delay(1, baseDelay, 0));
            Assert.InRange(RetryRules.Delay(2, baseDelay, 0.999999), TimeSpan.FromMilliseconds(249.9), TimeSpan.FromMilliseconds(250));

            // --- Outside the domain ---
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => RetryRules.Delay(0, baseDelay, 0.5));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => RetryRules.Delay(1, baseDelay, 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => RetryRules.Delay(1, baseDelay, -0.1));
        }

        [Theory]
        [InlineData(BatchPurpose.Read, 0, true)]
        [InlineData(BatchPurpose.Read, 1, false)]
        [InlineData(BatchPurpose.Validate, 0, true)]
        [InlineData(BatchPurpose.Validate, 1, false)]
        [InlineData(BatchPurpose.Persist, 1, true)]
        [InlineData(BatchPurpose.Persist, 2, true)]
        [InlineData(BatchPurpose.Maintenance, 2, true)]
        public void A_timed_out_read_or_validation_is_re_run_once_and_other_purposes_keep_their_attempts(BatchPurpose purpose, int timeoutReruns, bool expected)
        {
            Assert.Equal(expected, RetryRules.MayRerunAfterTimeout(purpose, timeoutReruns));
        }
    }
}
