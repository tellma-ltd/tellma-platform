// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Blobs
{
    /// <summary>The host's registration of the blob store it runs on.</summary>
    public static class BlobsComposition
    {
        /// <summary>
        ///     Registers the file-system <see cref="IBlobStore" /> as the process's one blob store,
        ///     with its options bound from <c>Tellma:Blobs:FileSystem</c> before
        ///     <paramref name="configure" /> runs.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         In Development an unset root defaults to <c>Tellma/{slug}/blobs</c> under the
        ///         user's local application data folder, the slug being the distribution's; the
        ///         resolved root is logged when the store is created. Outside Development the root
        ///         must be configured. The options are validated when the store is first resolved: an
        ///         unset, relative or (on Windows) over-long root fails it.
        ///     </para>
        ///     <para>
        ///         A process runs on exactly one blob store, so no other store is registered beside
        ///         this one. Calling it twice registers the store once and applies both
        ///         <paramref name="configure" /> actions in order.
        ///     </para>
        /// </remarks>
        /// <param name="services">The host's services.</param>
        /// <param name="configure">Adjusts the bound options, or null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="services" /> is null.</exception>
        public static void AddFileSystemBlobStore(
            this IServiceCollection services, Action<FileSystemBlobStoreOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(services);

            // The first call registers everything; a later one only adds its configure action, so
            // the store is never registered twice (two registrations would read as two stores).
            bool registered = services.Any(static descriptor =>
                descriptor.ServiceType == typeof(IBlobStore) && descriptor.ImplementationType == typeof(FileSystemBlobStore));
            if (!registered)
            {
                // Binding is registered before the caller's action, so the action overrides
                // configuration; the Development default and the validator run after both.
                services.AddOptions<FileSystemBlobStoreOptions>().BindConfiguration(FileSystemBlobStoreOptions.SectionName);
                services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<IPostConfigureOptions<FileSystemBlobStoreOptions>, FileSystemBlobStoreDefaults>());
                services.TryAddEnumerable(
                    ServiceDescriptor.Singleton<IValidateOptions<FileSystemBlobStoreOptions>, FileSystemBlobStoreOptionsValidator>());

                // AddMetrics is idempotent and supplies the IMeterFactory the store instruments are
                // created from; a meter built any other way is invisible to collectors.
                services.AddMetrics();
                services.TryAddSingleton<BlobStoreMetrics>();
                services.AddSingleton<IBlobStore, FileSystemBlobStore>();
            }

            if (configure is not null)
            {
                services.Configure(configure);
            }
        }
    }
}
