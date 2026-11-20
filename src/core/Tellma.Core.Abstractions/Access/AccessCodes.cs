// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    // The codes are spec 0014 Appendix A, and the preference rules spec 0018 §3.5.
    /// <summary>
    ///     The validation and guard codes of users, roles, memberships, permissions and the
    ///     preference bag: the dotted PascalCase resource keys the host localises, each raised at the
    ///     path of the value it concerns, or at the root for a guard.
    /// </summary>
    /// <remarks>
    ///     Codes are stable: a platform minor may add one, never rename one. The arguments listed
    ///     with a code are the only values its message renders from.
    /// </remarks>
    public static class AccessCodes
    {
        /// <summary>The system user, row 1, is written by a save, an action or a delete.</summary>
        public const string SystemUserImmutable = "Users.SystemUserImmutable";

        /// <summary>The caller deactivates their own user.</summary>
        public const string CannotDeactivateSelf = "Users.CannotDeactivateSelf";

        /// <summary>The caller deletes their own user.</summary>
        public const string CannotDeleteSelf = "Users.CannotDeleteSelf";

        /// <summary>
        ///     The caller removes their own membership of the administrator role, gives it an end or
        ///     moves its end, or moves its start past the save's instant.
        /// </summary>
        public const string CannotRemoveOwnAdministratorMembership = "Users.CannotRemoveOwnAdministratorMembership";

        /// <summary>A user's email changes once the user was invited.</summary>
        public const string EmailLockedAfterInvite = "Users.EmailLockedAfterInvite";

        /// <summary>A user is deleted after it was invited, or the system user is deleted.</summary>
        public const string OnlyNewUsersDeletable = "Users.OnlyNewUsersDeletable";

        /// <summary>A payload names the system kind, which only row 1 holds.</summary>
        public const string KindNotAllowed = "Users.KindNotAllowed";

        /// <summary>A user that is not an individual carries an email.</summary>
        public const string EmailNotAllowed = "Users.EmailNotAllowed";

        /// <summary>A user holds more memberships that have not ended than allowed. Argument: <c>max</c>.</summary>
        public const string TooManyRoles = "Users.TooManyRoles";

        /// <summary>
        ///     A membership granted, or widened, holds a permission the caller lacks unrestricted.
        ///     Arguments: <c>resource</c>, <c>action</c>, the first such permission's pair.
        /// </summary>
        public const string MembershipEscalation = "Users.MembershipEscalation";

        /// <summary>The user's preference bag would hold more keys than allowed.</summary>
        public const string TooManyPreferenceKeys = "Users.TooManyPreferenceKeys";

        /// <summary>A preference value is longer than allowed, in UTF-8 bytes. Argument: <c>maxBytes</c>.</summary>
        public const string PreferenceValueTooLarge = "Users.PreferenceValueTooLarge";

        /// <summary>
        ///     A preference key breaks the key grammar: dotted ASCII segments, the first a lower-case
        ///     letter then letters and digits, at most 128 characters.
        /// </summary>
        public const string PreferenceKeyInvalid = "Users.PreferenceKeyInvalid";

        /// <summary>A preference key repeats within one request.</summary>
        public const string PreferenceKeyDuplicate = "Users.PreferenceKeyDuplicate";

        /// <summary>A user's preferred language is not a bare language code the distribution offers.</summary>
        public const string LanguageNotOffered = "Users.LanguageNotOffered";

        /// <summary>A user's preferred calendar is not a calendar the platform composes.</summary>
        public const string CalendarUnknown = "Users.CalendarUnknown";

        /// <summary>A user's preferred time zone is not a zone the platform knows; an older alias of a zone is refused too.</summary>
        public const string TimeZoneUnknown = "Users.TimeZoneUnknown";

        /// <summary>A user's preferred region is not offered with any language the distribution offers.</summary>
        public const string RegionNotOffered = "Users.RegionNotOffered";

        /// <summary>A user's contact email is not a single valid address.</summary>
        public const string ContactEmailInvalid = "Users.ContactEmailInvalid";

        /// <summary>A user's contact mobile is not an international number: a plus sign and 7 to 15 digits, the first not zero.</summary>
        public const string MobileInvalid = "Users.MobileInvalid";

        /// <summary>An action that serves individuals alone, such as an invitation, targets a service account or the system user.</summary>
        public const string NotIndividual = "Users.NotIndividual";

        /// <summary>An action that serves service accounts alone, such as issuing credentials, targets another kind of user.</summary>
        public const string NotService = "Users.NotService";

        /// <summary>An action that needs an active user, such as an invitation or issuing credentials, targets a deactivated one.</summary>
        public const string Inactive = "Users.Inactive";

        /// <summary>A user who has signed in already is invited.</summary>
        public const string AlreadyJoined = "Users.AlreadyJoined";

        /// <summary>
        ///     The user's email now belongs to another identity account than the one the user is bound
        ///     to, so the invitation left the user as it was.
        /// </summary>
        public const string SubjectMismatch = "Users.SubjectMismatch";

        /// <summary>
        ///     A sandbox tenant invites only people who already hold a credential at the identity
        ///     server, since a sandbox never causes the identity server to email anyone.
        /// </summary>
        public const string SandboxRequiresExistingIdentity = "Users.SandboxRequiresExistingIdentity";

        /// <summary>Two windows of one user's memberships of one role overlap; a permanent one overlaps every other.</summary>
        public const string MembershipOverlap = "RoleMemberships.Overlap";

        /// <summary>A membership ends at or before it starts.</summary>
        public const string MembershipEndsBeforeStart = "RoleMemberships.EndsBeforeStart";

        /// <summary>A new membership, or one whose window changed, ended already.</summary>
        public const string MembershipEndsInPast = "RoleMemberships.EndsInPast";

        /// <summary>A role saved as public has members.</summary>
        public const string PublicRoleHasMembers = "Roles.PublicRoleHasMembers";

        /// <summary>
        ///     The administrator role is made public, deactivated or deleted, or its unrestricted
        ///     wildcard permission is removed or filtered.
        /// </summary>
        public const string AdministratorImmutable = "Roles.AdministratorImmutable";

        /// <summary>A role with members is deleted.</summary>
        public const string RoleHasMembers = "Roles.HasMembers";

        /// <summary>A role holds more permissions than allowed. Argument: <c>max</c>.</summary>
        public const string TooManyPermissions = "Roles.TooManyPermissions";

        /// <summary>A permission names a resource and action no securable registers. Arguments: <c>resource</c>, <c>action</c>.</summary>
        public const string UnknownSecurable = "Permissions.UnknownSecurable";

        /// <summary>A permission carries a filter on the wildcard resource, or on a securable that takes none.</summary>
        public const string FilterNotSupported = "Permissions.FilterNotSupported";

        /// <summary>
        ///     A permission's filter does not validate against the tenant's schema. Arguments:
        ///     <c>code</c>, the query diagnostic's code, and the diagnostic's own arguments.
        /// </summary>
        public const string FilterInvalid = "Permissions.FilterInvalid";

        /// <summary>A permission's filter is longer than allowed. Argument: <c>maxLength</c>.</summary>
        public const string FilterTooLong = "Permissions.FilterTooLong";

        /// <summary>
        ///     An unchanged filter's stored query-language version is below the oldest this
        ///     deployment compiles; re-entering the text affirms it. Arguments: <c>version</c>,
        ///     <c>minimum</c>.
        /// </summary>
        public const string FilterVersionUnsupported = "Permissions.FilterVersionUnsupported";

        /// <summary>A public role holds a permission on the wildcard resource.</summary>
        public const string WildcardResourceOnPublicRole = "Permissions.WildcardResourceOnPublicRole";

        /// <summary>A permission the caller lacks unrestricted is saved. Arguments: <c>resource</c>, <c>action</c>.</summary>
        public const string EscalationBeyondSelf = "Permissions.EscalationBeyondSelf";

        /// <summary>A write would leave no active, invited or joined individual with a started, permanent administrator membership.</summary>
        public const string LastAdministrator = "Access.LastAdministrator";

        /// <summary>A write would leave a public role with a member.</summary>
        public const string GuardPublicRoleHasMembers = "Access.PublicRoleHasMembers";

        /// <summary>A write would leave the administrator role inactive, public, or without its unrestricted wildcard grant.</summary>
        public const string AdministratorRoleDamaged = "Access.AdministratorRoleDamaged";

        /// <summary>The tenant's security lock was not obtained in time; the write may be retried.</summary>
        public const string LockTimeout = "Access.LockTimeout";

        /// <summary>The request for the tenant's security lock was chosen as a deadlock victim; the write may be retried.</summary>
        public const string LockDeadlock = "Access.LockDeadlock";
    }
}
