// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Identity.Options;

namespace Tellma.Identity.Hosting
{
    /// <summary>
    ///     Configures the Data Protection key ring that protects cookies, WebAuthn state, and
    ///     Data-Protection-format tokens. Standalone mode only: an in-proc host owns its own
    ///     app-wide Data Protection stack and the engine rides it.
    /// </summary>
    internal static class DataProtectionConfigurator
    {
        /// <summary>Configures Data Protection for the standalone hosting shape.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="options">The registration-time options snapshot.</param>
        public static void Configure(IServiceCollection services, TellmaIdentityOptions options)
        {
            if (options.Mode != TellmaIdentityDeploymentMode.Standalone)
            {
                return;
            }

            IDataProtectionBuilder dataProtection = services.AddDataProtection();

            // A fixed application name so cookies and DP-format payloads survive scale-out.
            dataProtection.SetApplicationName(options.DataProtection.ApplicationName);

            // On-prem/scale-out file-system ring; the default per-machine store applies when
            // neither a directory nor Azure storage is configured (single-instance and dev).
            if (!string.IsNullOrWhiteSpace(options.DataProtection.FileSystemKeyRingPath))
            {
                dataProtection.PersistKeysToFileSystem(new DirectoryInfo(options.DataProtection.FileSystemKeyRingPath));
            }

            // Config-gated Azure path: a shared blob ring encrypted with a Key Vault key. The
            // options validator rejects a BlobUri without a KeyVaultKeyUri, so the blob is never
            // persisted unencrypted.
            if (options.DataProtection.BlobUri is { } blobUri && options.DataProtection.KeyVaultKeyUri is { } keyUri)
            {
                DefaultAzureCredential credential = new();
                dataProtection.PersistKeysToAzureBlobStorage(blobUri, credential);
                dataProtection.ProtectKeysWithAzureKeyVault(keyUri, credential);
            }
        }
    }
}
