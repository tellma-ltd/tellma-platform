// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Data.Batches;
using Tellma.Core.Notifications;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Notifications
{
    /// <summary>
    ///     The inbox's statement text and the retention handler's declaration, with no database:
    ///     the summary reads the caller's version and seen stamp before it reads any notification,
    ///     so the version it answers is never newer than the rows; and retention runs on a read-only
    ///     tenant under its long lease.
    /// </summary>
    public sealed class InboxStatementsTests : IDisposable
    {
        /// <summary>The harness whose batches the statements are appended to.</summary>
        private readonly BatchHarness _harness = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        [Fact]
        public void The_summary_reads_the_callers_stamps_in_its_first_statement_before_any_notification()
        {
            DataBatch batch = _harness.Batch();

            _ = InboxStatements.Summary(batch, countCap: 100, latestCount: 10, out _);

            // --- One statement whose first SELECT reads the stamps, and every notification read follows it ---
            string text = Assert.Single(batch.Statements).Text;
            int firstSelect = text.IndexOf("SELECT", StringComparison.Ordinal);
            int stamps = text.IndexOf("[core].[UserStamps]", StringComparison.Ordinal);
            int notifications = text.IndexOf("[core].[Notifications]", StringComparison.Ordinal);
            Assert.True(firstSelect >= 0 && stamps > firstSelect && notifications > stamps, text);
            Assert.DoesNotContain("SELECT", text[(firstSelect + "SELECT".Length)..stamps], StringComparison.Ordinal);
        }

        [Fact]
        public void The_retention_handler_runs_on_a_read_only_tenant_under_a_ten_minute_lease()
        {
            JobHandlerAttribute declared = Assert.Single(typeof(NotificationRetentionHandler).GetCustomAttributes(typeof(JobHandlerAttribute), inherit: false).Cast<JobHandlerAttribute>());

            Assert.Equal(("core.notification-retention", 600, true), (declared.Key, declared.LeaseSeconds, declared.RunsWhenReadOnly));
        }
    }
}
