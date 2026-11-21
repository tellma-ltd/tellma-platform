// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Cronos;
using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Tenancy;
using Cron = Cronos.CronExpression;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §7.3: five-field Unix cron in Cronos's standard format, evaluated in a zone the host
    // resolves; a skipped local time fires at the next valid instant and a repeated one fires once,
    // as Cronos's zone-aware overloads do.
    /// <summary>Parses a schedule's expression and resolves its zone, as the scheduler and the save rules evaluate them.</summary>
    internal static class ScheduleCron
    {
        /// <summary>Parses an expression as five-field Unix cron.</summary>
        /// <param name="expression">The expression.</param>
        /// <param name="cron">The parsed expression when the method returns true; otherwise null.</param>
        /// <returns>True when it parses.</returns>
        public static bool TryParse(string? expression, [NotNullWhen(true)] out Cron? cron)
        {
            cron = null;
            return expression is not null && Cron.TryParse(expression, CronFormat.Standard, out cron);
        }

        /// <summary>Resolves a zone id on this host.</summary>
        /// <param name="id">The id, an IANA name.</param>
        /// <param name="zone">The zone when the method returns true; otherwise null.</param>
        /// <returns>True when the host's time-zone data resolves the id.</returns>
        public static bool TryFindZone(string? id, [NotNullWhen(true)] out TimeZoneInfo? zone)
        {
            zone = null;
            return id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out zone);
        }

        // Spec 0020 §7.3: a schedule without its own zone follows the tenant's, read at fire time, so
        // a settings change takes effect at the next tick. A tenant scope negotiates the tenant's
        // settings; a zone this host cannot resolve falls back to UTC, as the startup check judges it.
        /// <summary>The tenant's zone, as a tenant scope bound it.</summary>
        /// <param name="context">The scope's request context.</param>
        /// <returns>The zone.</returns>
        public static TimeZoneInfo TenantZone(RequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            return context.TenantSettings?.TimeZone
                ?? (TryFindZone(context.TenantTimeZone, out TimeZoneInfo? zone) ? zone : TimeZoneInfo.Utc);
        }

        /// <summary>The zone a schedule's expression is evaluated in: its own, else the tenant's.</summary>
        /// <param name="timeZoneId">The schedule's zone; null for the tenant's.</param>
        /// <param name="tenantZone">The tenant's zone.</param>
        /// <param name="zone">The zone when the method returns true; otherwise null.</param>
        /// <returns>True unless the schedule names a zone this host cannot resolve.</returns>
        public static bool TryZone(string? timeZoneId, TimeZoneInfo tenantZone, [NotNullWhen(true)] out TimeZoneInfo? zone)
        {
            ArgumentNullException.ThrowIfNull(tenantZone);
            if (timeZoneId is null)
            {
                zone = tenantZone;
                return true;
            }

            return TryFindZone(timeZoneId, out zone);
        }

        /// <summary>
        ///     The next occurrence strictly after an instant, in UTC; null when the expression never
        ///     occurs again.
        /// </summary>
        /// <param name="cron">The expression.</param>
        /// <param name="after">The instant.</param>
        /// <param name="zone">The zone the expression is evaluated in.</param>
        /// <returns>The occurrence, or null.</returns>
        public static DateTimeOffset? Next(Cron cron, DateTimeOffset after, TimeZoneInfo zone)
        {
            ArgumentNullException.ThrowIfNull(cron);
            ArgumentNullException.ThrowIfNull(zone);
            return cron.GetNextOccurrence(after, zone, inclusive: false)?.ToUniversalTime();
        }
    }

    /// <summary>What one tick does with one schedule it leased.</summary>
    /// <param name="Pause">Why the schedule pauses; null when it does not.</param>
    /// <param name="Occurrences">The occurrences it fires, oldest first, in UTC; one job each.</param>
    /// <param name="Missed">The occurrences it dropped or coalesced, which the missed counter counts.</param>
    /// <param name="Next">When it is next due, in UTC; null when it pauses.</param>
    internal sealed record ScheduleFiring(SchedulePausedReason? Pause, IReadOnlyList<DateTimeOffset> Occurrences, int Missed, DateTimeOffset? Next)
    {
        /// <summary>The latest occurrence it fires; null when it fires none.</summary>
        public DateTimeOffset? LastScheduledFor => Occurrences.Count > 0 ? Occurrences[^1] : null;
    }

    // Spec 0020 §7.5: per leased schedule, with the database's clock. An inactive owner or a retired
    // key pauses it and nothing fires; an uninitialised built-in only takes its next due time; past
    // the gap threshold every policy degrades to firing the latest occurrence, found without
    // enumerating the ones in between; otherwise the due occurrences are enumerated, at most one per
    // minute of the threshold, and the missed policy picks what fires. The next due time is strictly
    // after the tick's instant, so no window is replayed twice; none left exhausts the schedule.
    /// <summary>Computes what one tick does with one leased schedule.</summary>
    internal static class ScheduleOccurrences
    {
        /// <summary>Computes what a tick does with a leased schedule.</summary>
        /// <param name="row">The schedule as tick step 1 returned it.</param>
        /// <param name="cron">Its parsed expression.</param>
        /// <param name="zone">The zone the expression is evaluated in.</param>
        /// <param name="retired">Whether a feature retired the schedule's key.</param>
        /// <param name="gapThreshold">How far behind a schedule may fall before it degrades to firing the latest occurrence alone.</param>
        /// <param name="maxPollInterval">The longest the worker waits between polls, which bounds how late a skipped schedule's firing may be.</param>
        /// <returns>What the tick does.</returns>
        public static ScheduleFiring Compute(ScheduleTickRow row, Cron cron, TimeZoneInfo zone, bool retired, TimeSpan gapThreshold, TimeSpan maxPollInterval)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(cron);
            ArgumentNullException.ThrowIfNull(zone);
            DateTimeOffset now = row.Now;

            // --- Step 1: an inactive owner or a retired key pauses the schedule; nothing fires ---
            if (!row.OwnerIsActive)
            {
                return new ScheduleFiring(SchedulePausedReason.OwnerInactive, [], 0, null);
            }

            if (retired)
            {
                return new ScheduleFiring(SchedulePausedReason.HandlerRetired, [], 0, null);
            }

            // --- Steps 2 to 4: what fires, and what the policy dropped ---
            (IReadOnlyList<DateTimeOffset> fired, int missed) = row.NextDueAt is DateTimeOffset due
                ? Due(row, cron, zone, due, gapThreshold, maxPollInterval)
                : ([], 0);

            // --- Step 5: the next due time, strictly after now; none left exhausts the schedule ---
            DateTimeOffset? next = ScheduleCron.Next(cron, now, zone);
            return next is null
                ? new ScheduleFiring(SchedulePausedReason.Exhausted, fired, missed, null)
                : new ScheduleFiring(null, fired, missed, next);
        }

        /// <summary>The occurrences a due schedule fires and the ones it drops, by its missed policy.</summary>
        /// <param name="row">The schedule.</param>
        /// <param name="cron">Its parsed expression.</param>
        /// <param name="zone">Its zone.</param>
        /// <param name="due">Its stored next due time.</param>
        /// <param name="gapThreshold">The gap threshold.</param>
        /// <param name="maxPollInterval">The longest poll interval.</param>
        /// <returns>The occurrences fired, oldest first, and the count dropped or coalesced.</returns>
        private static (IReadOnlyList<DateTimeOffset> Fired, int Missed) Due(
            ScheduleTickRow row, Cron cron, TimeZoneInfo zone, DateTimeOffset due, TimeSpan gapThreshold, TimeSpan maxPollInterval)
        {
            DateTimeOffset now = row.Now;

            // --- Step 3: past the gap threshold, the latest occurrence alone, never enumerating ---
            // The gap itself is counted by the poll's gap check, so nothing is counted as missed here.
            if (now - due > gapThreshold)
            {
                DateTimeOffset? latest = cron.GetPreviousOccurrence(now, zone, inclusive: true)?.ToUniversalTime();
                return latest is DateTimeOffset occurrence && occurrence >= due ? ([occurrence], 0) : ([], 0);
            }

            // --- Step 4: the due occurrences, bounded by the threshold, then the policy's pick ---
            List<DateTimeOffset> occurrences = [.. cron.GetOccurrences(due, now, zone, fromInclusive: true, toInclusive: true).Select(static occurrence => occurrence.ToUniversalTime())];
            if (occurrences.Count == 0)
            {
                return ([], 0);
            }

            DateTimeOffset newest = occurrences[^1];
            switch (row.MissedPolicy)
            {
                case MissedPolicy.Coalesce:
                    return ([newest], occurrences.Count - 1);

                case MissedPolicy.ReplayAll:
                    DateTimeOffset oldest = now - TimeSpan.FromMinutes(row.CatchUpWindowMinutes);
                    List<DateTimeOffset> replayed = [.. occurrences.Where(occurrence => occurrence >= oldest)];
                    return (replayed, occurrences.Count - replayed.Count);

                case MissedPolicy.Skip:
                    return now - newest <= 2 * maxPollInterval
                        ? ([newest], occurrences.Count - 1)
                        : ([], occurrences.Count);

                default:
                    throw new InvalidOperationException($"The missed policy {row.MissedPolicy} is not defined.");
            }
        }
    }
}
