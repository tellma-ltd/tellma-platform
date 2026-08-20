// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity
{
    /// <summary>Fixed, well-known values used across the identity engine.</summary>
    public static class TellmaIdentityConstants
    {
        /// <summary>
        ///     The SQL schema that holds every identity table (Identity, OpenIddict, and the
        ///     engine's own tables), so the in-proc hosting shape can share a distribution's
        ///     database without collision. The name is baked into the committed migrations; a
        ///     per-deployment override is deliberately not supported.
        /// </summary>
        public const string Schema = "idsvr";

        /// <summary>The MVC area that contains every identity Razor Page.</summary>
        public const string AreaName = "Identity";

        /// <summary>The name of the SSO session cookie issued by the authority.</summary>
        public const string SsoCookieName = "tellma.identity.sso";

        /// <summary>The assembly that holds the EF Core migrations for the identity store.</summary>
        public const string MigrationsAssemblyName = "Tellma.Identity.Migrations";

        /// <summary>The scope requested to call a distribution API.</summary>
        public const string ApiScope = "tellma_api";

        /// <summary>The scope requested to call the identity server's management API.</summary>
        public const string IdentityScope = "tellma_identity";

        /// <summary>The scope requested to call the control-plane admin surface.</summary>
        public const string ControlPlaneScope = "tellma_control_plane";

        /// <summary>The fixed audience of tokens minted for the control-plane admin surface.</summary>
        public const string ControlPlaneAudience = "urn:tellma:control-plane";
    }
}
