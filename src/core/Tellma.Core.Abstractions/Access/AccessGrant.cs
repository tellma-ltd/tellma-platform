// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     One grant behind an access decision: a stored permission of a role
    ///     (<see cref="RoleAccessGrant" />), a bespoke criterion (<see cref="BespokeAccessGrant" />), or
    ///     the system short-circuit (<see cref="SystemAccessGrant" />).
    /// </summary>
    /// <remarks>
    ///     On the wire a grant carries a <c>kind</c> discriminator: <c>role</c>, <c>bespoke</c> or
    ///     <c>system</c>. A type outside the three fails to serialize rather than going out without its
    ///     kind.
    /// </remarks>
    /// <param name="Resource">The resource the grant names; for a stored permission, as stored, which may be the wildcard.</param>
    /// <param name="Action">The action the grant names; for a stored permission, as stored, which may be the wildcard.</param>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = KindProperty, UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
    [JsonDerivedType(typeof(RoleAccessGrant), RoleKind)]
    [JsonDerivedType(typeof(BespokeAccessGrant), BespokeKind)]
    [JsonDerivedType(typeof(SystemAccessGrant), SystemKind)]
    public abstract record AccessGrant(string Resource, string Action)
    {
        /// <summary>The name of the discriminator property a grant carries on the wire.</summary>
        public const string KindProperty = "kind";

        /// <summary>The kind of a <see cref="RoleAccessGrant" />.</summary>
        public const string RoleKind = "role";

        /// <summary>The kind of a <see cref="BespokeAccessGrant" />.</summary>
        public const string BespokeKind = "bespoke";

        /// <summary>The kind of a <see cref="SystemAccessGrant" />.</summary>
        public const string SystemKind = "system";

        /// <summary>The resource the grant names.</summary>
        public string Resource { get; } = Resource ?? throw new ArgumentNullException(nameof(Resource));

        /// <summary>The action the grant names.</summary>
        public string Action { get; } = Action ?? throw new ArgumentNullException(nameof(Action));
    }
}
