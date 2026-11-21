// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore
{
    /// <summary>The names of the web host's authentication schemes, cookies and the CSRF header.</summary>
    public static class TellmaAuthentication
    {
        /// <summary>The cookie session scheme of the SPA and every cookie-authenticated surface.</summary>
        public const string SessionScheme = "Tellma.Session";

        /// <summary>The OpenID Connect scheme that signs a user in to the session.</summary>
        public const string OidcScheme = "Tellma.OpenIdConnect";

        /// <summary>The JWT bearer scheme of the bearer surfaces: MCP, the public API and the control plane.</summary>
        public const string BearerScheme = "Tellma.Bearer";

        /// <summary>
        ///     The session cookie's name over HTTPS; the <c>__Host-</c> prefix pins it to the host,
        ///     <c>Secure</c> and <c>Path=/</c>.
        /// </summary>
        public const string SessionCookieName = "__Host-tellma.session";

        /// <summary>The SPA's readable signed-in signal, carrying no credential.</summary>
        public const string ProfileCookieName = "tellma.profile";

        /// <summary>The header every cookie-authenticated mutation carries: <c>&lt;client name&gt;/&lt;version&gt;</c>.</summary>
        public const string CsrfHeaderName = "Tellma-Client";

        /// <summary>
        ///     The session cookie's name when Development serves over plain HTTP, where a browser
        ///     refuses a <c>__Host-</c> cookie.
        /// </summary>
        internal const string DevelopmentSessionCookieName = "tellma.session";

        /// <summary>
        ///     The session ticket property that carries the session's handle, which the session store
        ///     stamps at creation and the tenant middleware binds as the request's session handle.
        /// </summary>
        internal const string SessionHandleProperty = ".tellma.session.handle";
    }
}
