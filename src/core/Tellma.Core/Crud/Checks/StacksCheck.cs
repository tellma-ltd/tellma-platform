// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Crud.Checks
{
    /// <summary>
    ///     <c>core.stacks</c>: one stack per entity, over a concrete keyed aggregate root the model
    ///     maps, its service closed over the leaf; an operation set the pipeline can serve; ceilings
    ///     that are positive; resource segments that are kebab-case and taken once; and no service
    ///     class written for a stack's entity that no stack runs.
    /// </summary>
    /// <remarks>
    ///     A service class is looked for in the assemblies of the composed features and among the
    ///     contributed services. One the container registers outside a stack is reported, and so is
    ///     one written for an entity whose stack runs the platform's default service: the stack was
    ///     registered without naming it. A stack that names its service is not judged against the
    ///     other service classes of its entity, which a distribution may keep for another
    ///     composition.
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed class StacksCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.Stacks;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];
            _ = StackCheckSupport.Registry(services, Name, problems);
            if (services.GetService<StackComposition>() is StackComposition composition)
            {
                CheckUnregisteredServices(composition, problems);
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>Reports every service class written for a stack's entity that no stack runs.</summary>
        /// <param name="composition">The settled stacks.</param>
        /// <param name="problems">Receives the problems.</param>
        internal void CheckUnregisteredServices(StackComposition composition, List<CompositionProblem> problems)
        {
            ArgumentNullException.ThrowIfNull(composition);
            ArgumentNullException.ThrowIfNull(problems);
            HashSet<Type> running = [.. composition.Stacks.Select(static stack => stack.ServiceType).OfType<Type>()];
            HashSet<Type> reported = [];

            // --- Registered with the container, not by a stack ---
            foreach (ContributedService service in record.Services)
            {
                if (service.ImplementationType is Type type && IsService(type) && !Runs(type, running) && reported.Add(type))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The feature '{service.Owner.Name}' registers the entity service {TypeDisplay.Qualified(type)} as a service, but no stack runs it.",
                        $"Register it as the stack's service: contribution.Entity<TEntity, {TypeDisplay.Short(type)}>(), and remove the service registration."));
                }
            }

            // --- Written for an entity whose stack runs the default service ---
            Dictionary<Type, ComposedStack> defaulted = [];
            foreach (ComposedStack stack in composition.Stacks.Where(static stack => stack.DeclaredService is null))
            {
                defaulted[stack.EntityType] = stack;
                defaulted[stack.Leaf] = stack;
            }

            if (defaulted.Count == 0)
            {
                return;
            }

            foreach (Assembly assembly in record.Features.Select(static feature => feature.GetType().Assembly).Distinct())
            {
                foreach (Type type in Types(assembly).Where(static type => IsService(type)))
                {
                    if (EntityOf(type) is Type entity && defaulted.TryGetValue(entity, out ComposedStack? stack) && reported.Add(type))
                    {
                        problems.Add(new CompositionProblem(
                            Name,
                            $"The entity service {TypeDisplay.Qualified(type)} serves {TypeDisplay.Qualified(entity)}, whose stack the feature '{stack.Owner.Name}' registers with the platform's default service, so no stack runs it.",
                            $"Name it in the stack's registration: contribution.Entity<{TypeDisplay.Short(stack.EntityType)}, {TypeDisplay.Short(type.IsGenericTypeDefinition ? type.MakeGenericType(stack.EntityType) : type)}>()."));
                    }
                }
            }
        }

        /// <summary>Whether a class is a service a stack could run: concrete, deriving from the entity service base, not the platform's default.</summary>
        /// <param name="type">The class.</param>
        /// <returns>True when it is.</returns>
        private static bool IsService(Type type)
        {
            Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            return type.IsClass
                && !type.IsAbstract
                && definition != typeof(DefaultEntityService<,>)
                && LeafClosure.GenericBase(type, typeof(EntityService<,>)) is not null;
        }

        /// <summary>Whether a stack runs a class, or a construction of it when it is an open definition.</summary>
        /// <param name="type">The class.</param>
        /// <param name="running">The services the stacks run.</param>
        /// <returns>True when one does.</returns>
        private static bool Runs(Type type, HashSet<Type> running)
        {
            return type.IsGenericTypeDefinition
                ? running.Any(service => service.IsGenericType && service.GetGenericTypeDefinition() == type)
                : running.Contains(type);
        }

        /// <summary>
        ///     The entity a service class is written for: the entity argument of its entity service
        ///     base, or for a definition generic over the entity, the class its type parameter is
        ///     constrained to.
        /// </summary>
        /// <param name="type">The class.</param>
        /// <returns>The entity, or null when it names none.</returns>
        private static Type? EntityOf(Type type)
        {
            Type entity = LeafClosure.GenericBase(type, typeof(EntityService<,>))!.GetGenericArguments()[0];
            return entity.IsGenericParameter
                ? entity.GetGenericParameterConstraints().FirstOrDefault(static constraint => constraint.IsClass)
                : entity;
        }

        /// <summary>The types an assembly defines, those it can load when some cannot be.</summary>
        /// <param name="assembly">The assembly.</param>
        /// <returns>The types.</returns>
        private static IEnumerable<Type> Types(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.OfType<Type>();
            }
        }
    }
}
