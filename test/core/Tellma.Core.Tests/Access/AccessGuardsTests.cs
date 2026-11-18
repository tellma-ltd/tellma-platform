// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Access;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tests.Data.Batches;
using static Tellma.Core.Tests.Access.AccessRulesHarness;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The security guards' text and placement: the lock first in the transaction, bounded by the
    ///     options; the three invariants after the batch's writes; each given once per batch; and
    ///     each refusal among a failed attempt's errors counted and logged.
    /// </summary>
    public sealed class AccessGuardsTests : IDisposable
    {
        /// <summary>The instruments, logs and options.</summary>
        private readonly AccessRulesHarness _harness = new();

        /// <summary>The batches' executor over a connection that is never opened.</summary>
        private readonly BatchHarness _batches = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _batches.Dispose();
            _harness.Dispose();
        }

        // --- The text ---

        [Fact]
        public void The_lock_is_the_tenant_s_one_exclusive_transaction_lock_and_its_failures_are_transient()
        {
            Assert.Equal(
                string.Join(
                    "\n",
                    "DECLARE @tm_Lock int;",
                    "EXEC @tm_Lock = sp_getapplock @Resource = N'tellma.access', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;",
                    "IF @tm_Lock = -3 THROW 50503, N'Access.LockDeadlock', 1;",
                    "IF @tm_Lock < 0 THROW 50503, N'Access.LockTimeout', 1;"),
                AccessGuards.LockText(TimeSpan.FromSeconds(5)));
        }

        [Theory]
        [InlineData(1L, 1L)]
        [InlineData(15_000L, 2L)]
        [InlineData(long.MaxValue, int.MaxValue)]
        public void The_lock_waits_whole_milliseconds_rounded_up_and_never_more_than_the_server_takes(long ticks, long milliseconds)
        {
            Assert.Contains($"@LockTimeout = {milliseconds};", AccessGuards.LockText(TimeSpan.FromTicks(ticks)), StringComparison.Ordinal);
        }

        [Fact]
        public void The_invariants_refuse_with_their_own_codes_and_count_only_started_permanent_administrators()
        {
            string text = AccessGuards.InvariantsText;

            Assert.Contains("THROW 50422, N'Access.LastAdministrator', 1;", text, StringComparison.Ordinal);
            Assert.Contains("THROW 50422, N'Access.PublicRoleHasMembers', 1;", text, StringComparison.Ordinal);
            Assert.Contains("THROW 50422, N'Access.AdministratorRoleDamaged', 1;", text, StringComparison.Ordinal);
            Assert.Contains("(M.[StartsAt] IS NULL OR M.[StartsAt] <= SYSUTCDATETIME()) AND M.[EndsAt] IS NULL", text, StringComparison.Ordinal);
            Assert.Contains("U.[State] IN ('Invited', 'Joined')", text, StringComparison.Ordinal);
        }

        // --- The placement ---

        [Fact]
        public async Task The_lock_heads_the_transaction_and_the_invariants_follow_the_writes_each_once()
        {
            AccessGuards guards = Guards();
            DataBatch batch = _batches.Batch(BatchPurpose.Persist);
            batch.MarkConnectPrologue(GuardPremises.ForSystem(new VersionTag(Guid.Empty)));

            // The pipeline asks for the lock before its writes and for the invariants after them; a
            // second round of the same batch asks again.
            guards.ContributeLock(batch);
            _ = batch.Sql($"DELETE FROM [fx].[Scratch] WHERE 1 = 0;", new SqlOptions(writes: new HashSet<TableName> { new("fx", "Scratch") }));
            guards.ContributeInvariants(batch);
            guards.ContributeLock(batch);
            guards.ContributeInvariants(batch);

            string text = (await BatchHarness.SealAsync(batch)).Text;
            string locked = AccessGuards.LockText(_harness.Options.SecurityLockTimeout);
            Assert.Equal(1, Occurrences(text, locked));
            Assert.Equal(1, Occurrences(text, AccessGuards.InvariantsText));
            Assert.StartsWith(BatchAssembly.TransactionOpen + "\n" + locked, text[text.IndexOf(BatchAssembly.TransactionOpen, StringComparison.Ordinal)..], StringComparison.Ordinal);
            Assert.True(text.IndexOf("DELETE FROM [fx].[Scratch]", StringComparison.Ordinal) < text.IndexOf(AccessGuards.InvariantsText, StringComparison.Ordinal));
            Assert.Single(batch.ErrorObservers);
        }

        [Fact]
        public void A_batch_the_data_layer_did_not_create_is_refused()
        {
            AccessGuards guards = Guards();

            _ = Assert.Throws<ArgumentException>(() => guards.ContributeLock(Refusing.Of<IDataBatch>()));
            _ = Assert.Throws<ArgumentException>(() => guards.ContributeInvariants(Refusing.Of<IDataBatch>()));
            _ = Assert.Throws<ArgumentNullException>(() => guards.ContributeLock(null!));
        }

        // --- The refusals ---

        [Fact]
        public void Each_guard_s_refusal_among_an_attempt_s_errors_is_counted_and_logged_and_nothing_else_is()
        {
            AccessGuards guards = Guards();
            DataBatch batch = _batches.Batch(BatchPurpose.Persist);
            guards.ContributeInvariants(batch);

            Action<IReadOnlyList<SqlErrorInfo>> observe = Assert.Single(batch.ErrorObservers);
            observe(
            [
                Error(TellmaSqlErrors.Invariant, AccessCodes.LastAdministrator),
                Error(TellmaSqlErrors.Transient, AccessCodes.LockTimeout),
                Error(TellmaSqlErrors.Transient, AccessCodes.LockDeadlock),
                Error(TellmaSqlErrors.Invariant, AccessCodes.GuardPublicRoleHasMembers),
                Error(TellmaSqlErrors.Invariant, AccessCodes.AdministratorRoleDamaged),
                Error(TellmaSqlErrors.Invariant, "Users.TooManyPreferenceKeys"),
                Error(547, AccessCodes.LastAdministrator),
            ]);

            Assert.Equal(
                [
                    AccessTelemetryNames.GuardLastAdministrator,
                    AccessTelemetryNames.GuardLockTimeout,
                    AccessTelemetryNames.GuardLockDeadlock,
                    AccessTelemetryNames.GuardPublicRoleMembers,
                    AccessTelemetryNames.GuardAdministratorDamaged,
                ],
                _harness.GuardTags);
            Assert.Equal(5, _harness.Logs(AccessEvents.GuardTriggered).Count);
        }

        // --- Fixtures ---

        /// <summary>A server error.</summary>
        /// <param name="number">The error number.</param>
        /// <param name="message">The message.</param>
        /// <returns>The error.</returns>
        private static SqlErrorInfo Error(int number, string message)
        {
            return new SqlErrorInfo(number, 16, message, null, 1);
        }

        /// <summary>How many times a text occurs in another.</summary>
        /// <param name="text">The text searched.</param>
        /// <param name="part">The text sought.</param>
        /// <returns>The count.</returns>
        private static int Occurrences(string text, string part)
        {
            int count = 0;
            for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        /// <summary>The guards of a user scope.</summary>
        /// <returns>The guards.</returns>
        private AccessGuards Guards()
        {
            return new AccessGuards(_harness.AccessOptions(), _harness.Metrics, new FixedContextAccessor(Context(50)), _harness.LoggerFactory);
        }
    }
}
