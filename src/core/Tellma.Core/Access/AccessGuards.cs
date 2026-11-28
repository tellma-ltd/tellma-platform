// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Access
{
    // The guards are spec 0014 §8.3: the lock text, the three invariants L1-L3 and how a triggered
    // guard is counted and logged. The pipeline (spec 0015) decides which batches they join.
    /// <summary>
    ///     The security tables' in-transaction invariants: the tenant's security lock taken first in
    ///     the transaction, and after every write the three assertions that keep a way back in: an
    ///     active, invited or joined individual with a started, permanent administrator membership;
    ///     public roles without members; the administrator role active, not public, and holding its
    ///     unrestricted wildcard grant. One per scope.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The lock serialises every security write of a tenant, so the assertions, which read
    ///         under the transaction's snapshot, can never pass for two writes that together break
    ///         them. A lock not obtained within <see cref="AccessOptions.SecurityLockTimeout" /> is
    ///         transient: the executor retries the round trip.
    ///     </para>
    ///     <para>
    ///         Each refusal that reaches the client, a retried lock timeout included, is counted on
    ///         <see cref="AccessTelemetryNames.GuardsTriggered" /> and logged as
    ///         <see cref="AccessEvents.GuardTriggered" />.
    ///     </para>
    /// </remarks>
    /// <param name="options">The access options, which bound the lock wait.</param>
    /// <param name="metrics">The access instruments.</param>
    /// <param name="accessor">The scope's request context, which names the tenant and user a refusal is logged for.</param>
    /// <param name="loggerFactory">Creates the access logger.</param>
    internal sealed class AccessGuards(
        IOptions<AccessOptions> options,
        AccessMetrics metrics,
        IRequestContextAccessor accessor,
        ILoggerFactory loggerFactory) : IAccessGuards
    {
        /// <summary>The application lock every security write of a tenant database takes.</summary>
        public const string LockResource = "tellma.access";

        /// <summary>The lock's return code of a deadlock victim; any other negative code is a timeout.</summary>
        private const int DeadlockVictim = -3;

        /// <summary>
        ///     The three invariants, asserted after every write of the batch. L1 counts started,
        ///     permanent memberships only: an administrator whose membership ends later cannot stand
        ///     in for the last permanent one.
        /// </summary>
        internal static readonly string InvariantsText = string.Join(
            "\n",
            "IF NOT EXISTS (SELECT 1 FROM [core].[Users] AS U",
            "               JOIN [core].[RoleMemberships] AS M ON M.[UserId] = U.[Id]",
            "               WHERE M.[RoleId] = 1 AND U.[Kind] = 'Individual' AND U.[IsActive] = 1 AND U.[State] IN ('Invited', 'Joined')",
            "                 AND (M.[StartsAt] IS NULL OR M.[StartsAt] <= SYSUTCDATETIME()) AND M.[EndsAt] IS NULL)",
            string.Create(CultureInfo.InvariantCulture, $"    THROW {TellmaSqlErrors.Invariant}, N'{AccessCodes.LastAdministrator}', 1;"),
            "IF EXISTS (SELECT 1 FROM [core].[RoleMemberships] AS M JOIN [core].[Roles] AS R ON R.[Id] = M.[RoleId] WHERE R.[IsPublic] = 1)",
            string.Create(CultureInfo.InvariantCulture, $"    THROW {TellmaSqlErrors.Invariant}, N'{AccessCodes.GuardPublicRoleHasMembers}', 1;"),
            "IF NOT EXISTS (SELECT 1 FROM [core].[Roles] AS R JOIN [core].[Permissions] AS P ON P.[RoleId] = R.[Id]",
            "               WHERE R.[Id] = 1 AND R.[IsActive] = 1 AND R.[IsPublic] = 0 AND P.[Resource] = '*' AND P.[Action] = '*' AND P.[Filter] IS NULL)",
            string.Create(CultureInfo.InvariantCulture, $"    THROW {TellmaSqlErrors.Invariant}, N'{AccessCodes.AdministratorRoleDamaged}', 1;"));

        /// <summary>The guards' tag values, by the code each one's refusal carries.</summary>
        private static readonly Dictionary<string, string> GuardsByCode = new(StringComparer.Ordinal)
        {
            [AccessCodes.LastAdministrator] = AccessTelemetryNames.GuardLastAdministrator,
            [AccessCodes.GuardPublicRoleHasMembers] = AccessTelemetryNames.GuardPublicRoleMembers,
            [AccessCodes.AdministratorRoleDamaged] = AccessTelemetryNames.GuardAdministratorDamaged,
            [AccessCodes.LockTimeout] = AccessTelemetryNames.GuardLockTimeout,
            [AccessCodes.LockDeadlock] = AccessTelemetryNames.GuardLockDeadlock,
        };

        /// <summary>What each batch of the scope was given so far, so a second call adds nothing twice.</summary>
        private readonly ConditionalWeakTable<DataBatch, Marks> _marks = [];

        /// <summary>The access logger.</summary>
        private readonly ILogger _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger(AccessEvents.Category);

        /// <summary>The security lock's text, waiting at most the given time.</summary>
        /// <param name="timeout">How long the lock may be waited for; capped at the largest wait the server takes.</param>
        /// <returns>The text: the lock taken, then a deadlock victim and a timeout each refused as transient.</returns>
        internal static string LockText(TimeSpan timeout)
        {
            // sp_getapplock takes the wait in whole milliseconds as an int; the options' own
            // validation keeps it positive, so the cap only guards an absurd configuration.
            long milliseconds = Math.Clamp((long)Math.Ceiling(timeout.TotalMilliseconds), 0, int.MaxValue);
            string variable = ReservedSqlNames.SecurityLock;
            return string.Join(
                "\n",
                string.Create(CultureInfo.InvariantCulture, $"DECLARE {variable} int;"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"EXEC {variable} = sp_getapplock @Resource = N'{LockResource}', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = {milliseconds};"),
                string.Create(CultureInfo.InvariantCulture, $"IF {variable} = {DeadlockVictim} THROW {TellmaSqlErrors.Transient}, N'{AccessCodes.LockDeadlock}', 1;"),
                string.Create(CultureInfo.InvariantCulture, $"IF {variable} < 0 THROW {TellmaSqlErrors.Transient}, N'{AccessCodes.LockTimeout}', 1;"));
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="batch" /> is null.</exception>
        /// <exception cref="ArgumentException">The batch is not one the platform's data layer created.</exception>
        public void ContributeLock(IDataBatch batch)
        {
            DataBatch data = Platform(batch);
            Marks marks = MarksOf(data);
            if (marks.Locked)
            {
                return;
            }

            // First in the transaction, ahead of the version-tag guard and the caller re-check, so
            // the tag rows are only ever locked by a holder of the security lock.
            data.Append(data.Begin(BatchSection.TransactionHead), LockText(options.Value.SecurityLockTimeout));
            marks.Locked = true;
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException"><paramref name="batch" /> is null.</exception>
        /// <exception cref="ArgumentException">The batch is not one the platform's data layer created.</exception>
        public void ContributeInvariants(IDataBatch batch)
        {
            DataBatch data = Platform(batch);
            Marks marks = MarksOf(data);
            if (marks.Asserted)
            {
                return;
            }

            // A read of the security tables as the batch's own writes left them; it writes nothing.
            data.Append(data.Begin(), InvariantsText);
            marks.Asserted = true;
        }

        /// <summary>The batch as the data layer's own, which takes the internal channel's statements.</summary>
        /// <param name="batch">The batch.</param>
        /// <returns>The data layer's batch.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="batch" /> is null.</exception>
        /// <exception cref="ArgumentException">The batch is another implementation.</exception>
        private static DataBatch Platform(IDataBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            return batch as DataBatch
                ?? throw new ArgumentException("The security guards join a persist batch the tenant database created, never a recording or another implementation.", nameof(batch));
        }

        /// <summary>The marks of a batch, created with the batch's observer of the guards' refusals on first use.</summary>
        /// <param name="batch">The batch.</param>
        /// <returns>The marks.</returns>
        private Marks MarksOf(DataBatch batch)
        {
            return _marks.GetValue(batch, data =>
            {
                data.OnServerErrors(Observe);
                return new Marks();
            });
        }

        /// <summary>Counts and logs the guards' refusals among a failed attempt's server errors.</summary>
        /// <param name="errors">The attempt's server errors.</param>
        private void Observe(IReadOnlyList<SqlErrorInfo> errors)
        {
            foreach (SqlErrorInfo error in errors)
            {
                if (error.Number is not (TellmaSqlErrors.Invariant or TellmaSqlErrors.Transient) || !GuardsByCode.TryGetValue(error.Message, out string? guard))
                {
                    continue;
                }

                RequestContext context = accessor.Current;
                metrics.CountGuard(guard);
                AccessLog.GuardTriggered(_logger, context.Tenant?.Id ?? 0, context.UserId, guard, error.Message);
            }
        }

        /// <summary>What one batch was given.</summary>
        private sealed class Marks
        {
            /// <summary>Whether the lock is appended.</summary>
            public bool Locked { get; set; }

            /// <summary>Whether the invariants are appended.</summary>
            public bool Asserted { get; set; }
        }
    }
}
