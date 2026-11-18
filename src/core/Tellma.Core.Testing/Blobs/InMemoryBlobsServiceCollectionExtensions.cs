// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Testing.Blobs
{
    /// <summary>Wires an <see cref="InMemoryBlobStore" /> and a <see cref="StubImageProcessor" /> into a test composition.</summary>
    public static class InMemoryBlobsServiceCollectionExtensions
    {
        /// <summary>
        ///     Registers an in-memory blob store and a stub image processor as the process's only ones,
        ///     replacing any registered before, and returns the store for assertions.
        /// </summary>
        /// <remarks>
        ///     The startup gate refuses a host without exactly one blob store, or without an image
        ///     processor when a kind normalises images; a test host that does not exercise real
        ///     storage satisfies both with this. The processor is also registered as
        ///     <see cref="StubImageProcessor" />, so a test can script its refusals.
        /// </remarks>
        /// <param name="services">The service collection.</param>
        /// <returns>The store the test asserts against; also resolvable from the container.</returns>
        public static InMemoryBlobStore AddInMemoryBlobs(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- The store, replacing any other: a process runs on exactly one ---
            InMemoryBlobStore store = new();
            services.RemoveAll<IBlobStore>();
            services.RemoveAll<InMemoryBlobStore>();
            services.AddSingleton(store);
            services.AddSingleton<IBlobStore>(store);

            // --- The processor, replacing any other: uploads use exactly one ---
            StubImageProcessor processor = new();
            services.RemoveAll<IImageProcessor>();
            services.RemoveAll<StubImageProcessor>();
            services.AddSingleton(processor);
            services.AddSingleton<IImageProcessor>(processor);
            return store;
        }
    }
}
