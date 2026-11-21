// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Access
{
    // The holders are spec 0014 §5.1's AccessHolders; the endpoint that returns them is §10's access/holders.
    /// <summary>
    ///     Who holds one securable across the tenant, and through what: the public roles and bespoke
    ///     criteria that reach every user, each role's grants and current members, and the users whose
    ///     own roles allow the securable.
    /// </summary>
    /// <remarks>Construction copies every list and the map, and refuses a null part or item.</remarks>
    /// <param name="Resource">The securable's resource, as registered; as asked for a pair the registry does not hold.</param>
    /// <param name="Action">The securable's action, as registered; as asked for a pair the registry does not hold.</param>
    /// <param name="Everyone">
    ///     What the public roles and the bespoke criteria allow every user; <see cref="AccessOutcome.Denied" />
    ///     when nobody holds the securable through them.
    /// </param>
    /// <param name="Grants">
    ///     Every matching stored permission of an active role, public ones included, that reaches the
    ///     securable on its own resource or through its owner's; each with no window, since the window
    ///     belongs to a membership. A filtered permission never reaches an action that takes no filter.
    /// </param>
    /// <param name="MembersByRole">
    ///     The current members of each role behind <see cref="Grants" /> and <see cref="Problems" />,
    ///     by role id: the active users other than the system user, every one for a public role.
    /// </param>
    /// <param name="Holders">The holders the caller may read, by display name, at most the configured maximum.</param>
    /// <param name="Total">Every holder, the ones the caller may not read included.</param>
    /// <param name="Capped">Whether more holders the caller may read exist than are listed.</param>
    /// <param name="Criteria">The bespoke criteria that reach the securable, on its own resource or its owner's.</param>
    /// <param name="Problems">The stored permissions that would have matched but grant nothing, and why.</param>
    /// <param name="Related">The display projections of the listed users and of the roles behind <see cref="Grants" /> and <see cref="Problems" />.</param>
    public sealed record AccessHolders(
        string Resource,
        string Action,
        AccessOutcome Everyone,
        IReadOnlyList<RoleAccessGrant> Grants,
        IReadOnlyDictionary<int, int> MembersByRole,
        IReadOnlyList<AccessHolder> Holders,
        int Total,
        bool Capped,
        IReadOnlyList<BespokeAccessGrant> Criteria,
        IReadOnlyList<AccessProblem> Problems,
        RelatedEntities Related)
    {
        /// <summary>The securable's resource.</summary>
        public string Resource { get; } = Resource ?? throw new ArgumentNullException(nameof(Resource));

        /// <summary>The securable's action.</summary>
        public string Action { get; } = Action ?? throw new ArgumentNullException(nameof(Action));

        /// <summary>What the public roles and the bespoke criteria allow every user.</summary>
        public AccessOutcome Everyone { get; } = Enum.IsDefined(Everyone)
            ? Everyone
            : throw new ArgumentOutOfRangeException(nameof(Everyone), Everyone, "An access outcome is Denied, Filtered or Unrestricted.");

        /// <summary>Every matching stored permission of an active role, with no window.</summary>
        public IReadOnlyList<RoleAccessGrant> Grants { get; } = AccessLists.Copy(Grants, nameof(Grants));

        /// <summary>The current members of each role behind the grants and the problems, by role id.</summary>
        public IReadOnlyDictionary<int, int> MembersByRole { get; } = (MembersByRole ?? throw new ArgumentNullException(nameof(MembersByRole))).ToFrozenDictionary();

        /// <summary>The holders the caller may read, by display name.</summary>
        public IReadOnlyList<AccessHolder> Holders { get; } = AccessLists.Copy(Holders, nameof(Holders));

        /// <summary>Every holder, the ones the caller may not read included.</summary>
        public int Total { get; } = Total >= 0 ? Total : throw new ArgumentOutOfRangeException(nameof(Total), Total, "A count of holders is never negative.");

        /// <summary>The bespoke criteria that reach the securable.</summary>
        public IReadOnlyList<BespokeAccessGrant> Criteria { get; } = AccessLists.Copy(Criteria, nameof(Criteria));

        /// <summary>The stored permissions that would have matched but grant nothing.</summary>
        public IReadOnlyList<AccessProblem> Problems { get; } = AccessLists.Copy(Problems, nameof(Problems));

        /// <summary>The display projections of the listed users and of the roles behind the grants and the problems.</summary>
        public RelatedEntities Related { get; } = Related ?? throw new ArgumentNullException(nameof(Related));
    }
}
