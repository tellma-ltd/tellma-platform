// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Access;

namespace Tellma.Core.Users
{
    // The me envelope of spec 0018 §3.5, in one place: the users' own actions compose it, and so does
    // spec 0025 §5.1's client/sync, which must compose it exactly as me does. Nothing here depends on
    // the distribution's users' leaf, so the service closed over any leaf and the sync share it.

    /// <summary>
    ///     The caller's own envelope: the read of the caller's preference bag, the profile view, the
    ///     caller's own tags a round trip reported, and the envelope composed from them.
    /// </summary>
    internal static class MeEnvelope
    {
        /// <summary>Appends the read of the caller's own preference bag, in key order.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="idempotent">Whether the batch may re-run the read: true in a Read batch.</param>
        /// <returns>The read: one result set of keys and values.</returns>
        public static BatchResult<RawResult> ComposeBag(IDataBatch batch, bool idempotent)
        {
            ArgumentNullException.ThrowIfNull(batch);
            return batch.Sql(
                $"SELECT [Key], [Value] FROM [core].[UserPreferences] WHERE [UserId] = @tm_UserId ORDER BY [Key];",
                new SqlOptions(idempotent: idempotent, resultSets: 1));
        }

        /// <summary>A bag read's keys and values, in the read's order.</summary>
        /// <param name="read">The read: one result set of keys and values.</param>
        /// <returns>The bag.</returns>
        public static IReadOnlyDictionary<string, string> Bag(RawResult read)
        {
            ArgumentNullException.ThrowIfNull(read);
            QueryRowSet set = read.ResultSets[0];
            string[] keys = (string[])set.GetBuffer(0);
            string[] values = (string[])set.GetBuffer(1);
            Dictionary<string, string> bag = new(set.RowCount, StringComparer.Ordinal);
            for (int index = 0; index < set.RowCount; index++)
            {
                bag[keys[index]] = values[index];
            }

            return bag;
        }

        /// <summary>The caller's profile, as the envelope carries it, from a user's row.</summary>
        /// <param name="user">The row.</param>
        /// <returns>The profile.</returns>
        public static UserProfileView View(User user)
        {
            ArgumentNullException.ThrowIfNull(user);
            return new UserProfileView(
                user.Id, user.Kind, user.State, user.Name, user.Name2, user.Name3, user.Email, user.ContactEmail, user.ContactMobile,
                user.ImageId, user.SignatureId, user.PreferredLanguage, user.PreferredCalendar, user.PreferredTimeZone, user.PreferredRegion,
                user.PreferredDigits, user.Gender, user.ModifiedAt);
        }

        /// <summary>The caller's profile, as the envelope carries it, from the connected user's cached profile.</summary>
        /// <param name="caller">The connected caller.</param>
        /// <returns>The profile.</returns>
        public static UserProfileView View(ConnectedUser caller)
        {
            ArgumentNullException.ThrowIfNull(caller);
            UserProfile profile = caller.Profile;
            return new UserProfileView(
                caller.UserId, caller.Kind, caller.State, profile.Name, profile.Name2, profile.Name3, profile.Email, profile.ContactEmail, profile.ContactMobile,
                profile.ImageId, profile.SignatureId, profile.PreferredLanguage, profile.PreferredCalendar, profile.PreferredTimeZone, profile.PreferredRegion,
                profile.PreferredDigits, profile.Gender, profile.ModifiedAt);
        }

        /// <summary>The caller's own tags a round trip reported.</summary>
        /// <param name="tags">The tags; null when the round trip carried no connected member.</param>
        /// <returns>The tags.</returns>
        /// <exception cref="InvalidOperationException">The round trip reported none, which a member's own round trip always does.</exception>
        public static UserVersionTagSnapshot CallerTags(UserVersionTagSnapshot? tags)
        {
            return tags ?? throw new InvalidOperationException("The caller's round trip reported no tags of the caller, which every round trip of a connected member reports.");
        }

        /// <summary>
        ///     The envelope of a profile, a bag and the tags a round trip left: the access summary
        ///     from every registered securable decided over the caller's set as that round trip
        ///     validated it, as the evaluator decides them all, with no round trip of its own.
        /// </summary>
        /// <param name="user">The caller's profile.</param>
        /// <param name="preferences">The caller's bag.</param>
        /// <param name="tenant">The tenant's tags after the round trip.</param>
        /// <param name="caller">The caller's own tags after the round trip.</param>
        /// <param name="access">The caller's set, as the round trip validated it.</param>
        /// <param name="securables">Every registered securable.</param>
        /// <param name="evaluator">Decides each securable over the caller's set.</param>
        /// <param name="now">The instant the summary is valid from.</param>
        /// <returns>The envelope.</returns>
        public static MeResult Of(
            UserProfileView user,
            IReadOnlyDictionary<string, string> preferences,
            VersionTagSnapshot tenant,
            UserVersionTagSnapshot caller,
            UserAccess access,
            ISecurableRegistry securables,
            AccessEvaluator evaluator,
            DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(securables);
            ArgumentNullException.ThrowIfNull(evaluator);
            AccessDecision[] decisions = [.. securables.All.Select(securable => evaluator.DecideFrom(access, securable, now))];
            return Compose(user, preferences, tenant, caller, decisions, access, now);
        }

        /// <summary>
        ///     Composes the envelope: the four client tags as the header carries them, the access
        ///     summary under the composite permissions tag with one entry per securable allowed, and
        ///     the caller's preferences tag.
        /// </summary>
        /// <param name="user">The caller's profile.</param>
        /// <param name="preferences">The caller's bag.</param>
        /// <param name="tenant">The tenant's tags.</param>
        /// <param name="caller">The caller's own tags.</param>
        /// <param name="decisions">The caller's decision on every registered securable, in the registry's order.</param>
        /// <param name="access">The caller's set.</param>
        /// <param name="now">The instant the summary is valid from.</param>
        /// <returns>The envelope.</returns>
        public static MeResult Compose(
            UserProfileView user,
            IReadOnlyDictionary<string, string> preferences,
            VersionTagSnapshot tenant,
            UserVersionTagSnapshot caller,
            IReadOnlyList<AccessDecision> decisions,
            UserAccess access,
            DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(preferences);
            ArgumentNullException.ThrowIfNull(tenant);
            ArgumentNullException.ThrowIfNull(caller);
            ArgumentNullException.ThrowIfNull(decisions);
            ArgumentNullException.ThrowIfNull(access);

            // --- The four client tags, as the version-tags header carries them ---
            Dictionary<string, string> tags = new(StringComparer.Ordinal);
            foreach ((string name, string value) in VersionTagWire.Items(tenant, caller))
            {
                tags[name] = value;
            }

            // --- The access summary: one entry per securable allowed, under the composite tag ---
            SecurableSummary[] allowed = [.. decisions
                .Where(static decision => decision.IsAllowed)
                .Select(static decision => new SecurableSummary(decision.Resource, decision.Action, decision.Outcome == AccessOutcome.Filtered))];
            AccessSummary summary = new(
                tags[VersionTagWire.PermissionsItem],
                MeResult.FormatVersion,
                access.IsSystem,
                allowed,
                access.Problems,
                access.NextBoundary(now));
            return new MeResult(user, tags[VersionTagWire.PreferencesItem], preferences, summary, tags);
        }
    }
}
