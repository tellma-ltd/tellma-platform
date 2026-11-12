// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Migrator
{
    /// <summary>What a command did to one tenant, as its report line states it.</summary>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="Outcome">The outcome's word: one of the constants of this record.</param>
    /// <param name="Detail">What was applied, or why it failed; null when there is nothing to add.</param>
    /// <param name="Drift">The drift report's lines, printed under the tenant's line.</param>
    internal sealed record TenantOutcome(int TenantId, string Outcome, string? Detail, IReadOnlyList<string> Drift)
    {
        /// <summary>Migrated, with its steps and scan: the tenant is current.</summary>
        public const string Migrated = "migrated";

        /// <summary>Provisioned and turned active.</summary>
        public const string Provisioned = "provisioned";

        /// <summary>Its database is ahead of this migrator: nothing ran after the migration, and that is not a failure.</summary>
        public const string Ahead = "ahead";

        /// <summary>Frozen, so skipped, even when named.</summary>
        public const string Frozen = "frozen";

        /// <summary>A provisioning row other than a seed's, left to <c>provision --tenant</c>.</summary>
        public const string Provisioning = "provisioning";

        /// <summary>Suspended: migrated and seeded, its steps and scan deferred to the first run after reactivation.</summary>
        public const string SuspendedStepsDeferred = "suspended-steps-deferred";

        /// <summary>Retired, so skipped when named.</summary>
        public const string Retired = "retired";

        /// <summary>The command failed on the tenant; a re-run retries it.</summary>
        public const string Failed = "failed";

        /// <summary>Whether the outcome is a failure.</summary>
        public bool IsFailure => Outcome == Failed;

        /// <summary>A failure.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="reason">Why.</param>
        /// <returns>The outcome.</returns>
        public static TenantOutcome Failure(int tenantId, string reason)
        {
            return new TenantOutcome(tenantId, Failed, reason, []);
        }

        /// <summary>An outcome with no detail.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="outcome">The outcome's word.</param>
        /// <returns>The outcome.</returns>
        public static TenantOutcome Of(int tenantId, string outcome)
        {
            return new TenantOutcome(tenantId, outcome, null, []);
        }

        /// <summary>
        ///     The text a failure prints: the message alone for the failures the migrator and the
        ///     tenancy raise for an operator to read, else the exception's type and message.
        /// </summary>
        /// <param name="exception">The failure.</param>
        /// <returns>The text.</returns>
        public static string Describe(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            return exception is MigratorFailureException or TenantStateException or TenantUnavailableException or TenantNotFoundException
                ? exception.Message
                : $"{exception.GetType().Name}: {exception.Message}";
        }

        /// <summary>The report's lines for this tenant: its outcome, then each drift line.</summary>
        /// <returns>The lines.</returns>
        public IEnumerable<string> Lines()
        {
            string head = string.Create(CultureInfo.InvariantCulture, $"tenant {TenantId}: {Outcome}");
            yield return Detail is null ? head : $"{head} ({Detail})";
            foreach (string line in Drift)
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"tenant {TenantId}: drift: {line}");
            }
        }
    }
}
