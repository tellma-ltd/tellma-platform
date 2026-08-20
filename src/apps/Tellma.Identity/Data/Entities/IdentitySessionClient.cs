// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Data.Entities
{
    /// <summary>
    ///     A client (distribution) that obtained tokens under an SSO session — the fan-out target
    ///     list for back-channel logout. The recorded OpenIddict authorization id lets a global
    ///     logout also revoke the grants backing that client's refresh tokens.
    /// </summary>
    public sealed class IdentitySessionClient
    {
        /// <summary>The owning session identifier.</summary>
        public string Sid { get; set; } = string.Empty;

        /// <summary>The client identifier.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>The physical id of the OpenIddict authorization backing the client's grant.</summary>
        public string? AuthorizationId { get; set; }

        /// <summary>When the client first obtained tokens under this session.</summary>
        public DateTimeOffset CreatedUtc { get; set; }

        /// <summary>When the client last acknowledged a back-channel logout, if ever.</summary>
        public DateTimeOffset? NotifiedUtc { get; set; }
    }
}
