// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    // The premises are spec 0014 §7.1; each case selects one variant of the prologue text (§7.3).
    /// <summary>
    ///     What one connect prologue assumes: whom it resolves, the cached tags it compares, and the
    ///     bookkeeping it may write. The case selects the prologue's variant.
    /// </summary>
    /// <remarks>
    ///     The set is closed by construction: a record's copy constructor is always reachable by a
    ///     derived type, but <see cref="Variant" /> is an internal abstract member, which a type outside
    ///     this assembly cannot implement. The connector can therefore switch on the three cases.
    /// </remarks>
    public abstract record ConnectPremises
    {
        /// <summary>Initialises a case.</summary>
        private protected ConnectPremises()
        {
        }

        /// <summary>The variant's name in telemetry: <c>subject</c>, <c>user</c> or <c>system</c>.</summary>
        internal abstract string Variant { get; }

        /// <summary>
        ///     A <see cref="ForSubject" /> that assumes nothing cached: the prologue returns the
        ///     permission rows and the profile unconditionally, and the settings when their tag
        ///     differs from <paramref name="expectedSettingsTag" />. Its guard never passes, since no
        ///     user id is expected, so the batch carries no body.
        /// </summary>
        /// <param name="subject">The caller's subject; a service account's client id.</param>
        /// <param name="expectedSettingsTag">The tag of the settings the scope holds; <see cref="Guid.Empty" /> for none.</param>
        /// <param name="stampActivity">Whether the prologue may stamp the user's activity.</param>
        /// <param name="allowStateFlip">Whether the prologue may record the user's first request.</param>
        /// <returns>The premises.</returns>
        /// <exception cref="ArgumentException"><paramref name="subject" /> is null or empty.</exception>
        public static ForSubject Cold(string subject, Guid expectedSettingsTag, bool stampActivity, bool allowStateFlip)
        {
            return new ForSubject(
                subject,
                ExpectedUserId: null,
                new ConnectExpectations(PermissionsTag: null, UserPermissionsTag: null, PreferencesTag: null, expectedSettingsTag),
                stampActivity,
                allowStateFlip,
                ReloadPermissions: false);
        }

        /// <summary>
        ///     A <see cref="ForSubject" /> that expects what a connected user's cache entry holds: its
        ///     user id, its permission set's two tags and its profile's tag.
        /// </summary>
        /// <param name="user">The connected user.</param>
        /// <param name="subject">The caller's subject; a service account's client id.</param>
        /// <param name="expectedSettingsTag">The tag of the settings the batch composes under; <see cref="Guid.Empty" /> for none.</param>
        /// <param name="stampActivity">Whether the prologue may stamp the user's activity.</param>
        /// <param name="allowStateFlip">Whether the prologue may record the user's first request.</param>
        /// <param name="reloadPermissions">Whether the permission set is old enough to be rebuilt from fresh rows even when its tags match.</param>
        /// <returns>The premises.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="user" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="subject" /> is null or empty.</exception>
        public static ForSubject For(
            ConnectedUser user, string subject, Guid expectedSettingsTag, bool stampActivity, bool allowStateFlip, bool reloadPermissions)
        {
            ArgumentNullException.ThrowIfNull(user);
            return new ForSubject(
                subject,
                user.UserId,
                new ConnectExpectations(user.Access.Tag, user.Access.UserTag, user.PreferencesTag, expectedSettingsTag),
                stampActivity,
                allowStateFlip,
                reloadPermissions);
        }

        /// <summary>
        ///     Resolves the caller by subject; the only case that may stamp the user's activity and
        ///     record their first request.
        /// </summary>
        /// <param name="Subject">The caller's subject; a service account's client id.</param>
        /// <param name="ExpectedUserId">The user id the cache holds for the subject; null on the cold path, whose guard never passes.</param>
        /// <param name="Expected">The cached tags.</param>
        /// <param name="StampActivity">Whether the prologue may stamp the user's activity.</param>
        /// <param name="AllowStateFlip">Whether the prologue may record the user's first request.</param>
        /// <param name="ReloadPermissions">Whether the permission rows are returned even when the set's tags match.</param>
        public sealed record ForSubject(
            string Subject,
            int? ExpectedUserId,
            ConnectExpectations Expected,
            bool StampActivity,
            bool AllowStateFlip,
            bool ReloadPermissions) : ConnectPremises
        {
            /// <inheritdoc cref="ForSubject(string, int?, ConnectExpectations, bool, bool, bool)" />
            public string Subject
            {
                get;
                init
                {
                    ArgumentException.ThrowIfNullOrEmpty(value, nameof(Subject));
                    field = value;
                }
            } = Subject is { Length: > 0 } ? Subject : throw new ArgumentException("The subject must not be null or empty.", nameof(Subject));

            /// <inheritdoc cref="ForSubject(string, int?, ConnectExpectations, bool, bool, bool)" />
            public ConnectExpectations Expected { get; init => field = value ?? throw new ArgumentNullException(nameof(Expected)); } =
                Expected ?? throw new ArgumentNullException(nameof(Expected));

            /// <inheritdoc />
            internal override string Variant => AccessTelemetryNames.VariantSubject;
        }

        /// <summary>
        ///     Resolves a user by id: a job scope running as the user, or a load of another user's
        ///     permissions. It never stamps activity and never records a first request.
        /// </summary>
        /// <param name="UserId">The user's id.</param>
        /// <param name="Expected">The cached tags.</param>
        /// <param name="ReloadPermissions">Whether the permission rows are returned even when the set's tags match.</param>
        public sealed record ForUser(int UserId, ConnectExpectations Expected, bool ReloadPermissions) : ConnectPremises
        {
            /// <inheritdoc cref="ForUser(int, ConnectExpectations, bool)" />
            public ConnectExpectations Expected { get; init => field = value ?? throw new ArgumentNullException(nameof(Expected)); } =
                Expected ?? throw new ArgumentNullException(nameof(Expected));

            /// <inheritdoc />
            internal override string Variant => AccessTelemetryNames.VariantUser;
        }

        /// <summary>
        ///     The platform's own work: no user is looked up, the set is the system's, and only a
        ///     stale <c>settings</c> tag on a validation or persist batch fails the guard.
        /// </summary>
        /// <remarks>Not named <c>System</c>, which would shadow the namespace inside this type.</remarks>
        /// <param name="ExpectedSettingsTag">The tag of the settings the batch composes under; <see cref="Guid.Empty" /> for none.</param>
        public sealed record ForSystem(Guid ExpectedSettingsTag) : ConnectPremises
        {
            /// <inheritdoc />
            internal override string Variant => AccessTelemetryNames.VariantSystem;
        }
    }
}
