// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     Endpoint metadata marking a tenant endpoint open to any connected active member, with no
    ///     securable. Stamped for a member-only action and by the host's <c>AllowMember()</c>
    ///     convention.
    /// </summary>
    /// <remarks>
    ///     Metadata for the startup audit, OpenAPI and step-up, never enforcement: the service
    ///     pipeline and the action invoker require the connected member. A member endpoint is never
    ///     sensitive. Every tenant endpoint carries exactly one of this record and
    ///     <see cref="SecurableEndpointMetadata" />.
    /// </remarks>
    public sealed record MemberEndpointMetadata;
}
