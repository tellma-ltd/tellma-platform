// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Buffers.Text;
using System.Security.Cryptography;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The secrets of the distribution's two clients at its identity server: the BFF client, which
    ///     signs users in, and the service client. Read once from configuration, so the in-proc engine's
    ///     seed and the sign-in agree on one value; a secret Development leaves unset in-proc is generated
    ///     for the boot, which only one instance shares. Never logged or shown: a class rather than a
    ///     record, so no generated member prints the secrets.
    /// </summary>
    /// <param name="clientSecret">The BFF client's secret, or null when none is configured.</param>
    /// <param name="serviceClientSecret">The service client's secret, or null when none is configured.</param>
    internal sealed class IdentityClientSecrets(string? clientSecret, string? serviceClientSecret)
    {
        /// <summary>The bytes of randomness in a generated secret.</summary>
        private const int GeneratedBytes = 32;

        /// <summary>The BFF client's secret, or null when none is configured.</summary>
        public string? ClientSecret { get; } = clientSecret;

        /// <summary>The service client's secret, or null when none is configured.</summary>
        public string? ServiceClientSecret { get; } = serviceClientSecret;

        /// <summary>The secrets the configuration names, with Development's in-proc gaps filled for the boot.</summary>
        /// <param name="configuration">The application's configuration.</param>
        /// <param name="environment">The host environment.</param>
        /// <returns>The secrets.</returns>
        public static IdentityClientSecrets From(IConfiguration configuration, IHostEnvironment environment)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(environment);
            IdentityHostOptions identity = HostSettings.Read<IdentityHostOptions>(configuration, IdentityHostOptions.SectionPath);
            string? client = NonEmpty(identity.ClientSecret);
            string? service = NonEmpty(identity.ServiceClientSecret);

            // Only the in-proc engine registers the clients with the secrets this host chose; a standalone
            // server holds the secrets onboarding gave it, which no generated value would match.
            return identity.Mode != IdentityMode.InProc || !environment.IsDevelopment()
                ? new IdentityClientSecrets(client, service)
                : new IdentityClientSecrets(client ?? Generate(), service ?? Generate());
        }

        /// <summary>A secret for the boot.</summary>
        /// <returns>32 random bytes in base64url.</returns>
        private static string Generate()
        {
            return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(GeneratedBytes));
        }

        /// <summary>A value, or null when it is empty.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The value, or null.</returns>
        private static string? NonEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
