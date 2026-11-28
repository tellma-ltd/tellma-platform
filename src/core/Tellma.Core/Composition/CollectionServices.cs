// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     Which service types the platform treats as collections: each feature registration adds one
    ///     implementation and none replaces another.
    /// </summary>
    internal static class CollectionServices
    {
        // The framework's own multi-registration extension points, which cannot carry the attribute.
        // Each is resolved as a collection by the host or the options system, so replacing one
        // feature's registration with another's would silently drop a hosted service, a configure
        // step or a validator.
        private static readonly HashSet<Type> FrameworkTypes =
        [
            typeof(IHostedService),
            typeof(IHostedLifecycleService),
            typeof(IConfigureOptions<>),
            typeof(IConfigureNamedOptions<>),
            typeof(IPostConfigureOptions<>),
            typeof(IValidateOptions<>),
            typeof(IOptionsChangeTokenSource<>),
        ];

        // Framework types this assembly does not reference, matched by full name.
        private static readonly HashSet<string> FrameworkTypeNames =
        [
            "Microsoft.AspNetCore.Hosting.IStartupFilter",
        ];

        /// <summary>
        ///     Whether <paramref name="serviceType" /> is a collection service: marked
        ///     <see cref="CollectionServiceAttribute" /> (as <see cref="IStartupCheck" /> is), or one of
        ///     the framework's multi-registration extension points.
        /// </summary>
        /// <param name="serviceType">The service type, closed or an open generic definition.</param>
        /// <returns>True for a collection service.</returns>
        public static bool Contains(Type serviceType)
        {
            Type definition = serviceType.IsGenericType ? serviceType.GetGenericTypeDefinition() : serviceType;
            return definition.IsDefined(typeof(CollectionServiceAttribute), inherit: false)
                || FrameworkTypes.Contains(definition)
                || (definition.FullName is string name && FrameworkTypeNames.Contains(name));
        }
    }
}
