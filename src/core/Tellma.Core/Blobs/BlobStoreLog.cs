// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Blobs
{
    /// <summary>Source-generated log messages of the file-system blob store.</summary>
    /// <remarks>
    ///     Messages carry tenant ids and paths, never file contents or user-supplied file names:
    ///     object names are random keys under a kind.
    /// </remarks>
    internal static partial class BlobStoreLog
    {
        /// <summary>The store's resolved root, written once when the store is created.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="rootPath">The absolute root directory.</param>
        [LoggerMessage(
            EventName = "BlobFileSystemRoot",
            Level = LogLevel.Information,
            Message = "The file-system blob store keeps blobs under {RootPath}.")]
        public static partial void Root(ILogger logger, string rootPath);

        /// <summary>A tenant's storage was ensured: by provisioning, or lazily by a write that found it missing.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="container">The tenant's directory.</param>
        [LoggerMessage(
            EventName = "BlobTenantEnsured",
            Level = LogLevel.Information,
            Message = "Blob storage for tenant {TenantId} is ensured at {Container}.")]
        public static partial void TenantEnsured(ILogger logger, int tenantId, string container);

        /// <summary>A write found the tenant's directory missing, created it and retried.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "BlobTenantMissingOnWrite",
            Level = LogLevel.Warning,
            Message = "Blob storage for tenant {TenantId} was missing on write; it was created and the write retried.")]
        public static partial void TenantMissingOnWrite(ILogger logger, int tenantId);

        /// <summary>A published object's part file could not be unlinked; it stays for the purge, and the write succeeded.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">Why the unlink failed.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="partPath">The part file left behind.</param>
        [LoggerMessage(
            EventName = "BlobPartFileLeft",
            Level = LogLevel.Warning,
            Message = "A blob of tenant {TenantId} was published, but its temporary file {PartPath} could not be removed; the purge will remove it.")]
        public static partial void PartFileLeft(ILogger logger, Exception exception, int tenantId, string partPath);

        /// <summary>The root cannot publish objects create-only, so every write fails.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="rootPath">The root.</param>
        /// <param name="problem">What the probe found.</param>
        [LoggerMessage(
            EventName = "BlobPublishUnsupported",
            Level = LogLevel.Error,
            Message = "The file-system blob store cannot publish objects create-only under {RootPath}: {Problem}")]
        public static partial void PublishUnsupported(ILogger logger, string rootPath, string problem);
    }
}
