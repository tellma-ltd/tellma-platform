// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>
    ///     Data Protection key-ring configuration. Applied only in standalone mode — an in-proc
    ///     host owns its app-wide Data Protection stack and the engine rides it.
    /// </summary>
    public sealed class TellmaIdentityDataProtectionOptions
    {
        /// <summary>
        ///     The application name that isolates this key ring. Fixed per authority so cookies
        ///     and Data-Protection-format payloads survive scale-out.
        /// </summary>
        public string ApplicationName { get; set; } = "tellma-identity";

        /// <summary>
        ///     A directory to persist the key ring to — the on-prem/scale-out path when Azure
        ///     Blob storage is not configured.
        /// </summary>
        public string? FileSystemKeyRingPath { get; set; }

        /// <summary>Azure Blob URI to persist the key ring to (config-gated Azure path).</summary>
        public Uri? BlobUri { get; set; }

        /// <summary>Azure Key Vault key URI used to encrypt the key ring at rest.</summary>
        public Uri? KeyVaultKeyUri { get; set; }
    }
}
