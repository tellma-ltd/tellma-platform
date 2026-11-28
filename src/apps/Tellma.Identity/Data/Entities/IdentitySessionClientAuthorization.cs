// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Data.Entities
{
    /// <summary>
    ///     An OpenIddict authorization a client's grants used under an SSO session. A client may
    ///     hold several under one session: grants asking for different scope sets each find or
    ///     create their own, and two first grants made at once each create one. Logout revokes
    ///     every one recorded, so none of the refresh tokens they back can renew.
    /// </summary>
    public sealed class IdentitySessionClientAuthorization
    {
        /// <summary>The owning session identifier.</summary>
        public string Sid { get; set; } = string.Empty;

        /// <summary>The client identifier.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>The physical id of the OpenIddict authorization.</summary>
        public string AuthorizationId { get; set; } = string.Empty;

        /// <summary>When a grant under this session first used the authorization.</summary>
        public DateTimeOffset CreatedUtc { get; set; }
    }
}
