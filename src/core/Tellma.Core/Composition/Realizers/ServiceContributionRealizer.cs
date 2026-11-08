// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Realizers
{
    /// <summary>
    ///     Registers <see cref="ServiceContributionItem" />s. A later feature's registration of a
    ///     service type replaces every earlier feature's, so a dependent feature overrides a service
    ///     of a feature it requires; a <see cref="CollectionServiceAttribute" /> type only ever adds.
    /// </summary>
    /// <remarks>
    ///     Only registrations this realizer made are replaced. A host-level registration made directly
    ///     on the builder's services stays in the collection; the feature's registration, added after
    ///     it, is still the one a single resolution returns.
    /// </remarks>
    internal sealed class ServiceContributionRealizer : IContributionRealizer<ServiceContributionItem>
    {
        // Per composition: the descriptors earlier features registered for each replaceable type.
        private readonly Dictionary<Type, ServiceDescriptor> _registered = [];

        /// <inheritdoc />
        public void Realize(ServiceContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            ServiceDescriptor? descriptor = CreateDescriptor(item, owner, context);
            if (descriptor is null)
            {
                return;
            }

            if (IsCollectionService(item.ServiceType))
            {
                context.Services.Add(descriptor);
                return;
            }

            // Topological realisation puts every required feature first, so the registration being
            // realised now belongs to the most dependent feature seen so far and wins.
            if (_registered.Remove(item.ServiceType, out ServiceDescriptor? replaced))
            {
                _ = context.Services.Remove(replaced);
            }

            context.Services.Add(descriptor);
            _registered.Add(item.ServiceType, descriptor);
        }

        /// <summary>Validates a service item and creates its descriptor.</summary>
        /// <param name="item">The item.</param>
        /// <param name="owner">The feature that contributed it.</param>
        /// <param name="context">Where problems are reported.</param>
        /// <returns>The descriptor, or null when the item has a problem.</returns>
        private static ServiceDescriptor? CreateDescriptor(ServiceContributionItem item, ITellmaFeature owner, RealizationContext context)
        {
            string feature = $"'{owner.Name}'";

            // --- Shape of the item ---
            if (item.ServiceType is null)
            {
                return Report(context, $"Feature {feature} contributes a service with no service type.", "Name the service type of every service the feature registers.");
            }

            string service = TypeDisplay.Qualified(item.ServiceType);
            if ((item.ImplementationType is null) == (item.Factory is null))
            {
                return Report(
                    context,
                    $"Feature {feature} contributes the service {service} with {(item.Factory is null ? "neither" : "both")} an implementation type {(item.Factory is null ? "nor" : "and")} a factory.",
                    "Give a service contribution exactly one of an implementation type and a factory.");
            }

            ServiceLifetime lifetime = item.Lifetime switch
            {
                ServiceLifetimeKind.Singleton => ServiceLifetime.Singleton,
                ServiceLifetimeKind.Scoped => ServiceLifetime.Scoped,
                ServiceLifetimeKind.Transient => ServiceLifetime.Transient,
                _ => (ServiceLifetime)(-1),
            };

            if (lifetime == (ServiceLifetime)(-1))
            {
                return Report(context, $"Feature {feature} contributes the service {service} with the undefined lifetime {(int)item.Lifetime}.", "Use Singleton, Scoped or Transient.");
            }

            // --- Factory registrations ---
            if (item.Factory is not null)
            {
                return item.ServiceType.IsGenericTypeDefinition
                    ? Report(context, $"Feature {feature} registers the open generic service {service} through a factory.", "Register an open generic service by its open generic implementation type.")
                    : new ServiceDescriptor(item.ServiceType, item.Factory, lifetime);
            }

            // --- Type registrations ---
            Type implementation = item.ImplementationType!;
            string implementationName = TypeDisplay.Qualified(implementation);
            if (!implementation.IsClass || implementation.IsAbstract)
            {
                return Report(context, $"Feature {feature} registers {implementationName} for {service}, but it is not a concrete class.", $"Register a concrete class that implements {TypeDisplay.Short(item.ServiceType)}.");
            }

            bool assignable = item.ServiceType.IsGenericTypeDefinition
                ? implementation.IsGenericTypeDefinition && ImplementsOpenGeneric(implementation, item.ServiceType)
                : !implementation.IsGenericTypeDefinition && item.ServiceType.IsAssignableFrom(implementation);

            return assignable
                ? new ServiceDescriptor(item.ServiceType, implementation, lifetime)
                : Report(context, $"Feature {feature} registers {implementationName} for {service}, which it does not implement.", $"Register a class that implements {TypeDisplay.Short(item.ServiceType)}.");
        }

        /// <summary>Whether the service type, or its generic definition, is marked <see cref="CollectionServiceAttribute" />.</summary>
        /// <param name="serviceType">The service type.</param>
        /// <returns>True for a collection service.</returns>
        private static bool IsCollectionService(Type serviceType)
        {
            Type definition = serviceType.IsGenericType ? serviceType.GetGenericTypeDefinition() : serviceType;
            return definition.IsDefined(typeof(CollectionServiceAttribute), inherit: false);
        }

        /// <summary>Whether an open generic implementation derives from or implements a construction of an open generic service.</summary>
        /// <param name="implementation">The open implementation.</param>
        /// <param name="openService">The open service definition.</param>
        /// <returns>True when it does.</returns>
        private static bool ImplementsOpenGeneric(Type implementation, Type openService)
        {
            // An open implementation implements an open service when one of its base types or
            // interfaces is a construction of that service's definition.
            for (Type? type = implementation; type is not null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == openService)
                {
                    return true;
                }
            }

            return implementation.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openService);
        }

        /// <summary>Reports a problem of a service item.</summary>
        /// <param name="context">Where the problem is reported.</param>
        /// <param name="problem">What is wrong.</param>
        /// <param name="fix">The remedy.</param>
        /// <returns>Null, so the caller can return it as the missing descriptor.</returns>
        private static ServiceDescriptor? Report(RealizationContext context, string problem, string fix)
        {
            context.Report(new CompositionProblem(CompositionNames.Source, problem, fix));
            return null;
        }
    }
}
