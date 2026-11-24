// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Data.Entities
{
    /// <summary>
    ///     A client (distribution) that obtained tokens under an SSO session — the fan-out target
    ///     list for back-channel logout. The OpenIddict authorizations its grants used under the
    ///     session (<see cref="Authorizations" />) let a logout also revoke the grants backing the
    ///     client's refresh tokens.
    /// </summary>
    public sealed class IdentitySessionClient
    {
        /// <summary>The owning session identifier.</summary>
        public string Sid { get; set; } = string.Empty;

        /// <summary>The client identifier.</summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        ///     The physical id of the OpenIddict authorization the client's latest grant under the
        ///     session used. It is still written beside <see cref="Authorizations" /> because the
        ///     previous engine release reads only this column, and a logout revokes it too, since a
        ///     registration that release made has nothing else.
        /// </summary>
        public string? AuthorizationId { get; set; }

        /// <summary>When the client first obtained tokens under this session.</summary>
        public DateTimeOffset CreatedUtc { get; set; }

        /// <summary>When the client last acknowledged a back-channel logout, if ever.</summary>
        public DateTimeOffset? NotifiedUtc { get; set; }

        /// <summary>Every OpenIddict authorization the client's grants used under the session.</summary>
        public ICollection<IdentitySessionClientAuthorization> Authorizations { get; } = [];
    }
}
