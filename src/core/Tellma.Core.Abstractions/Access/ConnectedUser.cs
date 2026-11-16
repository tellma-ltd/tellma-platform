// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;

namespace Tellma.Core.Abstractions.Access
{
    // The connected user is spec 0014 §7.1; the connect cache that holds it is §6.3.
    /// <summary>
    ///     A user as one connect prologue confirmed them in a tenant: who they are, their profile and
    ///     permission set, and the tenant-level tags the prologue read. Cached per tenant and subject,
    ///     and re-validated by the prologue of every batch.
    /// </summary>
    /// <param name="UserId">The user's id in the tenant.</param>
    /// <param name="Kind">The user's kind.</param>
    /// <param name="State">The user's state, after the prologue's first-request join.</param>
    /// <param name="Profile">The user's profile.</param>
    /// <param name="PreferencesTag">The user's preferences tag the profile was read under.</param>
    /// <param name="Access">The user's permission set.</param>
    /// <param name="TenantTags">Every tenant-level tag the prologue read, by name.</param>
    /// <param name="LastActivityStampedAt">
    ///     When this instance last asked the prologue to stamp the user's activity; null when it never
    ///     did. Within the stamp interval the next prologue does not try.
    /// </param>
    public sealed record ConnectedUser(
        int UserId,
        UserKind Kind,
        UserState State,
        UserProfile Profile,
        Guid PreferencesTag,
        UserAccess Access,
        IReadOnlyDictionary<string, Guid> TenantTags,
        DateTimeOffset? LastActivityStampedAt)
    {
        /// <inheritdoc cref="ConnectedUser(int, UserKind, UserState, UserProfile, Guid, UserAccess, IReadOnlyDictionary{string, Guid}, DateTimeOffset?)" />
        public UserProfile Profile { get; init => field = value ?? throw new ArgumentNullException(nameof(Profile)); } =
            Profile ?? throw new ArgumentNullException(nameof(Profile));

        /// <inheritdoc cref="ConnectedUser(int, UserKind, UserState, UserProfile, Guid, UserAccess, IReadOnlyDictionary{string, Guid}, DateTimeOffset?)" />
        public UserAccess Access { get; init => field = value ?? throw new ArgumentNullException(nameof(Access)); } =
            Access ?? throw new ArgumentNullException(nameof(Access));

        /// <inheritdoc cref="ConnectedUser(int, UserKind, UserState, UserProfile, Guid, UserAccess, IReadOnlyDictionary{string, Guid}, DateTimeOffset?)" />
        public IReadOnlyDictionary<string, Guid> TenantTags { get; init => field = Freeze(value); } = Freeze(TenantTags);

        /// <summary>An immutable copy of a tag map, keyed ordinally.</summary>
        /// <param name="tags">The tags.</param>
        /// <returns>The copy.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="tags" /> is null.</exception>
        private static FrozenDictionary<string, Guid> Freeze(IReadOnlyDictionary<string, Guid> tags)
        {
            ArgumentNullException.ThrowIfNull(tags, nameof(TenantTags));
            return tags is FrozenDictionary<string, Guid> frozen && ReferenceEquals(frozen.Comparer, StringComparer.Ordinal)
                ? frozen
                : tags.ToFrozenDictionary(StringComparer.Ordinal);
        }
    }
}
