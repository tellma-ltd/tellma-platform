// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.AzureBlobs
{
    /// <summary>
    ///     The settings of the Azure blob store: the storage account, the container naming, and how
    ///     many requests one call runs at once.
    /// </summary>
    /// <remarks>
    ///     Exactly one of <see cref="ServiceUri" /> and <see cref="ConnectionString" /> is set. A
    ///     deployment addresses its account by <see cref="ServiceUri" /> with the host's managed
    ///     identity, so no storage secret exists; <see cref="ConnectionString" /> is for the Azurite
    ///     emulator and local development alone.
    /// </remarks>
    public sealed class AzureBlobStoreOptions
    {
        /// <summary>The configuration section the Azure defaults bind these settings from.</summary>
        public const string SectionName = "Tellma:Blobs:Azure";

        /// <summary>The default <see cref="ContainerPrefix" />.</summary>
        public const string DefaultContainerPrefix = "tellma-";

        /// <summary>The default <see cref="MaxParallelism" />.</summary>
        public const int DefaultMaxParallelism = 8;

        /// <summary>The default <see cref="ReadBufferSize" />: 1 MiB.</summary>
        public const int DefaultReadBufferSize = 1024 * 1024;

        /// <summary>
        ///     The storage account's blob endpoint, such as
        ///     <c>https://&lt;account&gt;.blob.core.windows.net</c>, reached with the host's
        ///     <c>TokenCredential</c>.
        /// </summary>
        public Uri? ServiceUri { get; set; }

        /// <summary>A storage connection string: for the Azurite emulator and local development only.</summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        ///     What every tenant's container name starts with; the name is the prefix, <c>t</c> and
        ///     the tenant id (<c>tellma-t42</c>). Lowercase letters, digits and single hyphens, short
        ///     enough that the largest tenant id still yields a valid container name.
        /// </summary>
        public string ContainerPrefix { get; set; } = DefaultContainerPrefix;

        /// <summary>How many requests one write or delete call runs at once.</summary>
        public int MaxParallelism { get; set; } = DefaultMaxParallelism;

        /// <summary>
        ///     The bytes a download stream fetches per ranged request; a stream fetches only the
        ///     ranges it is read through, one buffer at a time.
        /// </summary>
        public int ReadBufferSize { get; set; } = DefaultReadBufferSize;
    }
}
