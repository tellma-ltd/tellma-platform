// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The host's registrations of the blob store and the image processor, read from the service
    ///     collection itself when the startup gate runs, so the blobs check counts them without
    ///     creating any of them, and sees those a host registered after composing Tellma.
    /// </summary>
    /// <param name="services">The host's service collection, read only once the host is built.</param>
    internal sealed class BlobRegistrations(IServiceCollection services)
    {
        /// <summary>Every registration of the blob store.</summary>
        public IReadOnlyList<ServiceDescriptor> Stores => Of(typeof(IBlobStore));

        /// <summary>Every registration of the image processor.</summary>
        public IReadOnlyList<ServiceDescriptor> ImageProcessors => Of(typeof(IImageProcessor));

        /// <summary>What a registration registers, for a message: the implementation's type name, or how it is made.</summary>
        /// <param name="descriptor">The registration.</param>
        /// <returns>The description.</returns>
        public static string Describe(ServiceDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);

            return descriptor switch
            {
                { ImplementationType: Type type } => type.Name,
                { ImplementationInstance: object instance } => instance.GetType().Name,
                _ => "a factory registration",
            };
        }

        /// <summary>The unkeyed registrations of one service type, in registration order.</summary>
        /// <param name="serviceType">The service type.</param>
        /// <returns>The registrations.</returns>
        private List<ServiceDescriptor> Of(Type serviceType)
        {
            // A keyed registration is never resolved as the service itself, so it never counts.
            return [.. services.Where(descriptor => descriptor.ServiceType == serviceType && !descriptor.IsKeyedService)];
        }
    }
}
