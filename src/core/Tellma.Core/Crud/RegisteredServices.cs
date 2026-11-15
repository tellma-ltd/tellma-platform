// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;

namespace Tellma.Core.Crud
{
    /// <summary>
    ///     The registrations of the container the composition built, read by a startup check that
    ///     judges what was registered rather than what resolves. It holds the collection itself, so
    ///     the check sees the registrations a host added after the composition too.
    /// </summary>
    /// <param name="services">The service collection the composition registered into.</param>
    internal sealed class RegisteredServices(IServiceCollection services)
    {
        /// <summary>The registrations, as the container was built from them.</summary>
        public IEnumerable<ServiceDescriptor> Descriptors => services;
    }
}
