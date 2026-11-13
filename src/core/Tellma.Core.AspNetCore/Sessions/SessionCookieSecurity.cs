// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Tellma.Core.AspNetCore.Hosting;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>
    ///     Whether the host's session cookies are restricted to HTTPS: always, except when Development
    ///     serves over plain HTTP, where a browser would refuse a <c>Secure</c> cookie.
    /// </summary>
    /// <param name="Secure">Whether the cookies carry <c>Secure</c>.</param>
    internal sealed record SessionCookieSecurity(bool Secure)
    {
        /// <summary>The security the public origin calls for.</summary>
        /// <param name="configuration">The application's configuration.</param>
        /// <returns>The security.</returns>
        public static SessionCookieSecurity From(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            return new SessionCookieSecurity(!ServesPlainHttp(configuration));
        }

        /// <summary>Whether the public origin is plain HTTP.</summary>
        /// <param name="configuration">The application's configuration.</param>
        /// <returns>True for an <c>http</c> public origin.</returns>
        public static bool ServesPlainHttp(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            return Uri.TryCreate(configuration[HostConfigurationKeys.PublicOrigin], UriKind.Absolute, out Uri? origin)
                && origin.Scheme == Uri.UriSchemeHttp;
        }
    }
}
