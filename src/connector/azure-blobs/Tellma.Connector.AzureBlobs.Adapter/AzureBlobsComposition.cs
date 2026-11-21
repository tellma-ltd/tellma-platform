// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Connector.AzureBlobs
{
    /// <summary>The activation entry point of the Azure blob store.</summary>
    public static class AzureBlobsComposition
    {
        /// <summary>
        ///     Registers the Azure blob store as the process's one <see cref="IBlobStore" />, with its
        ///     settings, their validator and its startup check. A second call adds only its settings
        ///     action, and its credential when it passes one.
        /// </summary>
        /// <remarks>
        ///     The store never constructs a credential. An endpoint (<see cref="AzureBlobStoreOptions.ServiceUri" />)
        ///     is reached with <paramref name="credential" /> when passed, else with the host's
        ///     registered <see cref="TokenCredential" />, the deployment's managed identity; a
        ///     connection string (Azurite, local development) needs neither.
        /// </remarks>
        /// <param name="services">The host's services.</param>
        /// <param name="configure">Sets the store's settings, from configuration or in code.</param>
        /// <param name="credential">The identity to reach the endpoint with; null for the host's registered one.</param>
        public static void AddAzureBlobStore(this IServiceCollection services, Action<AzureBlobStoreOptions> configure, TokenCredential? credential)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            // --- The first call registers everything; two registrations would read as two stores ---
            bool registered = services.Any(static descriptor =>
                descriptor.ServiceType == typeof(IBlobStore) && descriptor.ImplementationType == typeof(AzureBlobStore));
            if (!registered)
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AzureBlobStoreOptions>, AzureBlobStoreOptionsValidator>());

                // AddMetrics is idempotent and supplies the IMeterFactory the store instruments are
                // created from; a meter built any other way is invisible to collectors.
                services.AddMetrics();
                services.TryAddSingleton<AzureBlobStoreMetrics>();
                services.AddSingleton<IBlobStore, AzureBlobStore>();
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupCheck, AzureBlobStoreCheck>());
            }

            // --- The credential: the one passed, else the host's, resolved when the store is built ---
            if (credential is not null || !registered)
            {
                services.RemoveAll<AzureBlobStoreCredential>();
                services.AddSingleton(provider => new AzureBlobStoreCredential(credential ?? provider.GetService<TokenCredential>()));
            }

            services.Configure(configure);
        }
    }
}
