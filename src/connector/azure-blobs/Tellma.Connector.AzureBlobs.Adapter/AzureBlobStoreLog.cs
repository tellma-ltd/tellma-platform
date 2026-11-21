// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Connector.AzureBlobs
{
    /// <summary>Source-generated log messages of the Azure blob store.</summary>
    /// <remarks>
    ///     Messages carry tenant ids and container names, never file contents or user-supplied file
    ///     names: object names are random keys under a kind. The event names are the file-system
    ///     store's, so one query reads both stores.
    /// </remarks>
    internal static partial class AzureBlobStoreLog
    {
        /// <summary>A tenant's container was ensured: by provisioning, or lazily by the first write this process made for it.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="container">The tenant's container.</param>
        [LoggerMessage(
            EventName = "BlobTenantEnsured",
            Level = LogLevel.Information,
            Message = "Blob storage for tenant {TenantId} is ensured at {Container}.")]
        public static partial void TenantEnsured(ILogger logger, int tenantId, string container);

        /// <summary>A write found the tenant's container missing, created it and retried.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "BlobTenantMissingOnWrite",
            Level = LogLevel.Warning,
            Message = "Blob storage for tenant {TenantId} was missing on write; it was created and the write retried.")]
        public static partial void TenantMissingOnWrite(ILogger logger, int tenantId);
    }
}
