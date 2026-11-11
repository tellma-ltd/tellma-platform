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
    ///     The commit probe: its verdicts, its one autocommit statement over the distinct ids, and how
    ///     it recognises the re-run's collision with the probed rows' primary key.
    /// </summary>
    public sealed class CommitProbeTests : IDisposable
    {
        private static readonly TableName Jobs = new("core", "Jobs");

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
            Assert.Equal("SELECT COUNT_BIG(*) FROM [core].[Jobs] WITH (READCOMMITTEDLOCK) WHERE [Id] IN (SELECT [Id] FROM @tb0_t0);", probe.Text);

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

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A server error on line 1 of no procedure.</summary>
        /// <param name="number">The number.</param>
        /// <param name="message">The message.</param>
        /// <returns>The error.</returns>
        private static SqlErrorInfo Error(int number, string message)
        {
            return new SqlErrorInfo(number, 14, message, null, 1);
        }
    }
}
