// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Crud.Checks
{
    /// <summary>
    ///     <c>core.entity-pipeline</c>: no registration substitutes the platform's entity pipeline:
    ///     every registration of <see cref="IEntityPipelineFactory" /> and of any
    ///     <see cref="IEntityPipeline{TEntity, TKey}" /> is the platform's own, by type. The pipeline
    ///     carries the access guards, the validation rounds and the tenant-state verdict, so a
    ///     substitute would serve every stack without them.
    /// </summary>
    internal sealed class EntityPipelineCheck : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.EntityPipeline;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];
            if (services.GetService<RegisteredServices>() is RegisteredServices registered)
            {
                Check(registered.Descriptors, problems);
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>Reports every pipeline registration that is not the platform's own type.</summary>
        /// <param name="descriptors">The container's registrations.</param>
        /// <param name="problems">Receives the problems.</param>
        internal void Check(IEnumerable<ServiceDescriptor> descriptors, List<CompositionProblem> problems)
        {
            ArgumentNullException.ThrowIfNull(descriptors);
            ArgumentNullException.ThrowIfNull(problems);
            foreach (ServiceDescriptor descriptor in descriptors)
            {
                Type service = descriptor.ServiceType;
                bool pipeline = service == typeof(IEntityPipelineFactory)
                    || (service.IsGenericType && service.GetGenericTypeDefinition() == typeof(IEntityPipeline<,>));
                if (!pipeline)
                {
                    continue;
                }

                // Only a registration by type can be told apart: a factory or an instance could be anything.
                Type? implementation = descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType;
                if (implementation is not null && implementation.Assembly == typeof(EntityPipelineCheck).Assembly)
                {
                    continue;
                }

                string registered = implementation is not null
                    ? TypeDisplay.Qualified(implementation)
                    : descriptor.IsKeyedService
                        ? (descriptor.KeyedImplementationInstance is not null ? "an instance" : "a factory")
                        : (descriptor.ImplementationInstance is not null ? "an instance" : "a factory");
                problems.Add(new CompositionProblem(
                    Name,
                    $"{TypeDisplay.Short(service)} is registered with {registered}, which is not the platform's entity pipeline; every stack's operations run through the platform's pipeline, which carries the access guards and the validation rounds.",
                    "Remove the registration; extend a stack through its service's hooks and its components instead."));
            }
        }
    }
}
