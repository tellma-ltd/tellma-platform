// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.AspNetCore.Hosting;

namespace Tellma.Defaults.Azure.AspNetCore
{
    /// <summary>
    ///     The Data Protection key ring on Azure: kept in one blob that every instance and every slot
    ///     shares, each key wrapped by a Key Vault key, so neither storage nor the vault alone can
    ///     read a session or a protected setting.
    /// </summary>
    internal static class AzureKeyRing
    {
        /// <summary>
        ///     Keeps the key ring in Azure when the configuration names both the blob and the Key Vault
        ///     key; otherwise registers nothing.
        /// </summary>
        /// <remarks>
        ///     A ring configured by halves, or at a location that is not an absolute https URL, is
        ///     left to the host's options check, which refuses it at startup; outside Development a
        ///     host without a persistent ring fails its Data Protection check.
        /// </remarks>
        /// <param name="services">The services.</param>
        /// <param name="configuration">The application's configuration.</param>
        public static void Register(IServiceCollection services, IConfiguration configuration)
        {
            DataProtectionHostOptions options = HostSettings.Read<DataProtectionHostOptions>(configuration, DataProtectionHostOptions.SectionPath);
            if (!TryHttpsUri(options.BlobUri, out Uri? blobUri) || !TryHttpsUri(options.KeyId, out Uri? keyId))
            {
                return;
            }

            // The credential is resolved from the container, not created here: the host's own
            // identity reaches both resources. The application name stays the host's to set.
            _ = services.AddDataProtection()
                .PersistKeysToAzureBlobStorage(provider => new BlobClient(blobUri, provider.GetRequiredService<TokenCredential>()))
                .ProtectKeysWithAzureKeyVault(keyId, static provider => provider.GetRequiredService<TokenCredential>());
        }

        /// <summary>Parses a configured location that must be an absolute https URL.</summary>
        /// <param name="value">The configured value.</param>
        /// <param name="uri">The location, when it is one.</param>
        /// <returns>True when the value is an absolute https URL.</returns>
        private static bool TryHttpsUri(string? value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri)
        {
            uri = null;
            return !string.IsNullOrWhiteSpace(value)
                && Uri.TryCreate(value, UriKind.Absolute, out uri)
                && uri.Scheme == Uri.UriSchemeHttps;
        }
    }
}
