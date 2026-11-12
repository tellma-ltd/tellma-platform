// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Authentication
{
    /// <summary>
    ///     The claim names the host reads, as the identity server issues them: inbound claims are
    ///     never mapped, so <c>sub</c> stays <c>sub</c>.
    /// </summary>
    internal static class TellmaClaimTypes
    {
        /// <summary>The subject.</summary>
        public const string Subject = "sub";

        /// <summary>The authorized party: the client the token was issued to.</summary>
        public const string AuthorizedParty = "azp";

        /// <summary>The client the token was issued to, where the token names no authorized party.</summary>
        public const string ClientId = "client_id";

        /// <summary>The identity server's session.</summary>
        public const string SessionId = "sid";

        /// <summary>The authentication context class the session reached.</summary>
        public const string AuthenticationContextClass = "acr";

        /// <summary>When the user signed in, in seconds since the epoch.</summary>
        public const string AuthenticationTime = "auth_time";

        /// <summary>When the session reached its authentication context class, in seconds since the epoch.</summary>
        public const string AcrAuthenticationTime = "tellma_acr_auth_time";

        /// <summary>Whether the token's principal is an individual or a service.</summary>
        public const string Kind = "tellma_kind";

        /// <summary>The token's space-delimited scopes.</summary>
        public const string Scope = "scope";

        /// <summary>The token's audiences, one claim each.</summary>
        public const string Audience = "aud";

        /// <summary>The <see cref="Kind" /> of an individual.</summary>
        public const string IndividualKind = "individual";

        /// <summary>The <see cref="Kind" /> of a service.</summary>
        public const string ServiceKind = "service";

        /// <summary>The scope of the public API and MCP.</summary>
        public const string ApiScope = "tellma_api";

        /// <summary>The scope of the control plane.</summary>
        public const string ControlPlaneScope = "tellma_control_plane";
    }
}
