// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     The codes a shareable stack refuses a save, a delete or a sharing action with, each a
    ///     resource key the client renders with the error's arguments.
    /// </summary>
    public static class SharingValidationCodes
    {
        /// <summary>
        ///     A standard row, one with no owner, is read-only outside a system scope: it cannot be
        ///     saved, deleted, activated, deactivated or transferred, though it can be shared. Argument:
        ///     <c>entity</c>, the entity's name.
        /// </summary>
        public const string StandardReadOnly = "Sharing.StandardReadOnly";

        /// <summary>The row a new row is based on does not exist or is hidden from the caller. Argument: <c>id</c>.</summary>
        public const string BasedOnInvisible = "Sharing.BasedOnInvisible";

        /// <summary>A user grantee is not an active individual member of the tenant. Argument: <c>userId</c>.</summary>
        public const string UserInvalid = "Sharing.UserInvalid";

        /// <summary>A role grantee is not an active role. Argument: <c>roleId</c>.</summary>
        public const string RoleInvalid = "Sharing.RoleInvalid";

        /// <summary>A user grant names a target's owner, who holds every ability already. Argument: <c>userId</c>.</summary>
        public const string OwnerIsImplicit = "Sharing.OwnerIsImplicit";

        /// <summary>
        ///     The grants would leave a target with more user or role grants than a row may hold.
        ///     Arguments: <c>kind</c> (<c>user</c> or <c>role</c>) and <c>max</c>.
        /// </summary>
        public const string TooManyGrants = "Sharing.TooManyGrants";

        /// <summary>A new owner is not an active individual member of the tenant. Argument: <c>userId</c>.</summary>
        public const string OwnerInvalid = "Sharing.OwnerInvalid";
    }
}
