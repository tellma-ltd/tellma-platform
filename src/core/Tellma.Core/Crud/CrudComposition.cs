// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Composition;
using Tellma.Core.Crud.Checks;
using Tellma.Core.Crud.Details;
using Tellma.Core.Crud.Query;
using Tellma.Core.Crud.Save;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Crud
{
    /// <summary>
    ///     Registers the stacks of a realised composition: each stack's service, companions and
    ///     components closed over its leaf, the routed services, the stack registry, its securables,
    ///     the validation stage's services, the pipeline and its query path, and the startup checks
    ///     that judge them.
    /// </summary>
    internal static class CrudComposition
    {
        /// <summary>Settles the stacks and registers them.</summary>
        /// <param name="services">The service collection.</param>
        /// <param name="record">The realised composition.</param>
        public static void Register(IServiceCollection services, TellmaCompositionRecord record)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(record);
            var composition = StackComposition.Build(record);
            services.AddSingleton(composition);

            // --- Each stack's classes, scoped: the service, its companions and its components ---
            foreach (ComposedStack stack in composition.Stacks.Where(static stack => stack.ServiceType is not null))
            {
                RegisterService(services, stack);
                foreach (Type companion in stack.Companions)
                {
                    services.TryAddScoped(companion);
                }

                // A named filter contributor is created once by the registry from the root provider,
                // so it is never resolved from the container.
                foreach (ComposedComponent component in stack.Components.Where(static component => component.Kind != ComponentKind.NamedFilterContributor))
                {
                    services.TryAddEnumerable(ServiceDescriptor.Scoped(ComponentContracts.For(component.Kind, stack.Leaf), component.ComponentType));
                }
            }

            // --- The routed services, scoped, each resolved by its class ---
            foreach (ComposedApiService service in composition.ApiServices)
            {
                services.TryAddScoped(service.ServiceType);
            }

            // --- The registry, built once on first use, and what reads it ---
            // A route source registered before the composition is kept: a host or a test answers the
            // link builder itself.
            services.AddSingleton(static provider => new StackRegistry(
                provider.GetRequiredService<StackComposition>(),
                provider.GetRequiredService<EntityMetadataSource>(),
                provider));
            services.AddSingleton<IStackRegistry>(static provider => provider.GetRequiredService<StackRegistry>());
            services.TryAddSingleton<IStackRouteSource>(static provider => provider.GetRequiredService<StackRegistry>());
            services.AddSingleton<ISecurableContributor>(static provider => new StackSecurableContributor(provider.GetRequiredService<IStackRegistry>()));
            services.AddSingleton(new RegisteredServices(services));

            // --- The validation stage: its instruments, the attribute walker's cached plans, and the
            // scope's confirmations over the keys the features declared ---
            _ = services.AddMetrics();
            services.TryAddSingleton<CrudMetrics>();
            services.TryAddSingleton<AttributeWalker>();
            services.AddSingleton(new DeclaredConfirmations(record.Confirmations.Select(static confirmation => confirmation.Key)));
            services.TryAddScoped<Confirmations>();
            services.TryAddScoped<IConfirmations>(static provider => provider.GetRequiredService<Confirmations>());

            // --- The pipeline: its factory, the operation scope, the query path's planner, rules and
            // child surfaces, the details path's projections, planner and reads, and the save; the
            // singletons hold no caller state ---
            services.TryAddScoped<IEntityPipelineFactory, EntityPipelineFactory>();
            services.TryAddScoped<CrudOperations>();
            services.TryAddSingleton<QuerySurfaces>();
            services.TryAddSingleton<QueryDiscoveries>();
            services.TryAddSingleton<QueryPlanner>();
            services.TryAddScoped<QueryTraversalRule>();
            services.TryAddScoped<IQueryTraversalRule>(static provider => provider.GetRequiredService<QueryTraversalRule>());
            services.TryAddScoped<QueryOperation>();
            services.TryAddScoped<IChildQueryService, ChildQueryService>();
            services.TryAddSingleton<DetailsProjections>();
            services.TryAddScoped<DetailsPlanner>();
            services.TryAddScoped<DetailsOperation>();
            services.TryAddScoped<SaveOperation>();

            // --- The checks, in the order they report, after the built-in checks of the record ---
            IStartupCheck[] checks =
            [
                new StacksCheck(record),
                new StackActionsCheck(),
                new StackComponentsCheck(),
                new StackProjectionsCheck(),
                new ConfirmationsCheck(record),
                new EntityPipelineCheck(),
                new EnlistmentsCheck(),
                new StackSecurablesCheck(),
            ];
            record.BuiltInChecks.AddRange(checks);
            foreach (IStartupCheck check in checks)
            {
                services.AddSingleton(check);
            }
        }

        /// <summary>
        ///     Registers a stack's service, scoped, by its class and by the entity service base of its
        ///     leaf, through a factory: the container's build-time validation then leaves the service's
        ///     dependencies to the first resolution, while the pipeline factory every service takes is
        ///     composed.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="stack">The stack, its service closed.</param>
        private static void RegisterService(IServiceCollection services, ComposedStack stack)
        {
            Type service = stack.ServiceType!;
            ObjectFactory create = ActivatorUtilities.CreateFactory(service, Type.EmptyTypes);
            services.TryAdd(ServiceDescriptor.Scoped(service, provider => create(provider, null)));
            services.TryAdd(ServiceDescriptor.Scoped(typeof(EntityService<,>).MakeGenericType(stack.Leaf, stack.KeyType!), provider => provider.GetRequiredService(service)));
        }
    }
}
