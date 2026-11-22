// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Blobs;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Data;

namespace Tellma.Core.Crud
{
    /// <summary>
    ///     The stacks of a composition at the level of types, settled once the features are realised:
    ///     one stack per leaf with its service, companions and components each closed over the leaf,
    ///     and the routed services. The stack registry builds the descriptors from it at startup.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every problem found here is a startup problem, kept for the check it belongs to, so the
    ///         realised gate reports it with every other: a stack whose service does not fit its leaf,
    ///         a component whose definition the leaf cannot close, two unrelated registrations of one
    ///         entity.
    ///     </para>
    ///     <para>
    ///         An entity registered again by a feature that depends on the one whose registration
    ///         holds replaces that registration's service and operations, as a dependent feature's
    ///         service registration replaces a required feature's; the stack stays the first
    ///         registrant's, so its securables keep their feature.
    ///     </para>
    /// </remarks>
    internal sealed class StackComposition
    {
        private readonly Dictionary<Type, ComposedStack> _byLeaf;
        private readonly Func<Type, Type> _leafOf;
        private readonly IReadOnlyList<Type> _otherWriters;

        /// <summary>Creates a composition from what was settled.</summary>
        /// <param name="stacks">The stacks, in registration order.</param>
        /// <param name="apiServices">The routed services, in registration order.</param>
        /// <param name="problems">The problems, each sourced to its check.</param>
        /// <param name="leafOf">Resolves an entity to the leaf registered for it.</param>
        /// <param name="otherWriters">The classes outside the stacks that may declare enlistments: the routed services, the job handlers, the provisioning steps and the contributed services.</param>
        /// <param name="platform">The platform's companions.</param>
        private StackComposition(
            List<ComposedStack> stacks,
            List<ComposedApiService> apiServices,
            List<CompositionProblem> problems,
            Func<Type, Type> leafOf,
            IReadOnlyList<Type> otherWriters,
            IPlatformCompanions platform)
        {
            Platform = platform;
            Stacks = stacks;
            ApiServices = apiServices;
            Problems = problems;
            _leafOf = leafOf;
            _otherWriters = otherWriters;
            _byLeaf = stacks.ToDictionary(static stack => stack.Leaf);
        }

        /// <summary>The platform's companions, which the registrations and the descriptors reach through here.</summary>
        public IPlatformCompanions Platform { get; }

        /// <summary>Every stack, in the order its entity was first registered.</summary>
        public IReadOnlyList<ComposedStack> Stacks { get; }

        /// <summary>Every routed service, in registration order.</summary>
        public IReadOnlyList<ComposedApiService> ApiServices { get; }

        /// <summary>Every problem found at the level of types, each with its check's name as its source.</summary>
        public IReadOnlyList<CompositionProblem> Problems { get; }

        /// <summary>The stack whose leaf an entity resolves to: the entity's own, or the one whose leaf substitutes it.</summary>
        /// <param name="entityType">The entity, a default or a leaf.</param>
        /// <returns>The stack, or null when no stack has that leaf.</returns>
        public ComposedStack? StackOf(Type entityType)
        {
            ArgumentNullException.ThrowIfNull(entityType);
            return _byLeaf.GetValueOrDefault(_leafOf(entityType)) ?? _byLeaf.GetValueOrDefault(entityType);
        }

        /// <summary>
        ///     Every class of the composition that may declare <see cref="IEnlists{TTarget}" />, once
        ///     each: a stack's service, companions and components with the stack's leaf, then the
        ///     routed services, the job handlers, the provisioning steps and the contributed services,
        ///     which belong to no stack.
        /// </summary>
        /// <returns>The classes, each with the leaf of the stack it belongs to, or null.</returns>
        public IEnumerable<(Type Writer, Type? Leaf)> Writers()
        {
            HashSet<Type> seen = [];
            foreach (ComposedStack stack in Stacks)
            {
                IEnumerable<Type?> attached = [stack.ServiceType, .. stack.Companions, .. stack.Components.Select(static component => component.ComponentType)];
                foreach (Type writer in attached.OfType<Type>().Where(seen.Add))
                {
                    yield return (writer, stack.Leaf);
                }
            }

            foreach (Type writer in _otherWriters.Where(seen.Add))
            {
                yield return (writer, null);
            }
        }

        /// <summary>Settles the stacks of a realised composition.</summary>
        /// <param name="record">The realised composition.</param>
        /// <param name="platform">The platform's companions.</param>
        /// <returns>The stacks.</returns>
        public static StackComposition Build(TellmaCompositionRecord record, IPlatformCompanions platform)
        {
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(platform);
            List<CompositionProblem> problems = [];
            List<ComposedStack> stacks = Register(record, problems);
            foreach (ComposedStack stack in stacks)
            {
                CloseService(stack, record.LeafOf, problems);
            }

            var byLeaf = stacks.ToDictionary(static stack => stack.Leaf);
            AttachCompanions(record, byLeaf, problems);

            // --- The platform's companion, ahead of every feature's, on every stack whose operations it serves (spec 0019 §1.2) ---
            foreach (ComposedStack stack in stacks.Where(static stack => stack.ServiceType is not null))
            {
                if (platform.CompanionOf(stack.Leaf, stack.Operations) is Type companion)
                {
                    _ = stack.Companions.Remove(companion);
                    stack.Companions.Insert(0, companion);
                }
            }

            AttachComponents(record, stacks, problems);

            // --- The blob pair, ahead of every other component, on every stack whose aggregate references a blob (spec 0017 §4.2) ---
            BlobReferenceComponents.Attach(stacks, record);

            // --- Routed services: no leaf to close over, recorded once each ---
            List<ComposedApiService> apiServices = [.. record.ApiServices.Select(static service => new ComposedApiService(service.ServiceType, service.Owner))];
            return new StackComposition(stacks, apiServices, problems, record.LeafOf, OtherWriters(record, apiServices), platform);
        }

        /// <summary>
        ///     The closed classes outside the stacks that may write by enlistment: the routed services,
        ///     the job handlers closed over the leaves, the provisioning steps and the contributed
        ///     services' implementations.
        /// </summary>
        /// <param name="record">The realised composition.</param>
        /// <param name="apiServices">The routed services.</param>
        /// <returns>The classes, in that order.</returns>
        private static List<Type> OtherWriters(TellmaCompositionRecord record, List<ComposedApiService> apiServices)
        {
            List<Type> writers = [.. apiServices.Select(static service => service.ServiceType)];
            foreach (ContributedJobHandler handler in record.JobHandlers)
            {
                try
                {
                    writers.Add(LeafClosure.Close(handler.HandlerType, record.LeafOf));
                }
                catch (ArgumentException)
                {
                    // A handler the leaves cannot close is the job handlers check's to report.
                }
            }

            writers.AddRange(record.ProvisioningSteps.Select(static step => step.StepType));
            writers.AddRange(record.Services
                .Select(static service => service.ImplementationType)
                .OfType<Type>()
                .Where(static type => !type.ContainsGenericParameters));
            return writers;
        }

        /// <summary>
        ///     One stack per leaf: the first registration of each, replaced by a later one from a
        ///     feature that depends on it, and refused from an unrelated feature.
        /// </summary>
        /// <param name="record">The realised composition.</param>
        /// <param name="problems">Receives the problems.</param>
        /// <returns>The stacks, in registration order.</returns>
        private static List<ComposedStack> Register(TellmaCompositionRecord record, List<CompositionProblem> problems)
        {
            List<ComposedStack> stacks = [];
            Dictionary<Type, ComposedStack> byLeaf = [];
            foreach (ContributedEntity contributed in record.Entities)
            {
                Type leaf = record.LeafOf(contributed.EntityType);
                if (!byLeaf.TryGetValue(leaf, out ComposedStack? stack))
                {
                    stack = new ComposedStack(contributed.EntityType, leaf, contributed.Owner)
                    {
                        DeclaredService = contributed.ServiceType,
                        DeclaredOperations = contributed.Operations,
                        HeldBy = contributed.Owner,
                    };
                    byLeaf.Add(leaf, stack);
                    stacks.Add(stack);
                    continue;
                }

                // --- Registered again: by the holder itself, by a dependent, or by an unrelated feature ---
                string entity = TypeDisplay.Qualified(leaf);
                if (ReferenceEquals(contributed.Owner, stack.HeldBy))
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.Stacks,
                        $"The feature '{contributed.Owner.Name}' registers the stack of {entity} twice, with different services or operations.",
                        "Register each entity once per feature: contribution.Entity<TEntity, TService>()."));
                }
                else if (record.Requires(contributed.Owner, stack.HeldBy))
                {
                    stack.DeclaredService = contributed.ServiceType ?? stack.DeclaredService;
                    stack.DeclaredOperations = contributed.Operations ?? stack.DeclaredOperations;
                    stack.HeldBy = contributed.Owner;
                }
                else
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.Stacks,
                        $"The features '{stack.HeldBy.Name}' and '{contributed.Owner.Name}' both register a stack over {entity}, and neither requires the other; an entity has one stack.",
                        $"Make the feature that replaces the stack's service require '{stack.HeldBy.Name}', or remove one registration."));
                }
            }

            return stacks;
        }

        /// <summary>
        ///     Closes a stack's service over its leaf, the platform's default service when none is
        ///     named, and reports a key, a shape or a service that does not fit.
        /// </summary>
        /// <param name="stack">The stack.</param>
        /// <param name="leafOf">Resolves an entity to the leaf registered for it.</param>
        /// <param name="problems">Receives the problems.</param>
        private static void CloseService(ComposedStack stack, Func<Type, Type> leafOf, List<CompositionProblem> problems)
        {
            string entity = TypeDisplay.Qualified(stack.Leaf);

            // --- The leaf: a keyed aggregate root ---
            stack.KeyType = EntityShapes.KeyTypeOf(stack.Leaf);
            if (stack.KeyType is null || stack.Leaf.IsAbstract)
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.Stacks,
                    $"The stack of {entity} is over a class that is not a concrete keyed entity, so no service can serve it.",
                    "Register a concrete entity deriving from TopLevelEntity, or another Entity<TKey> base."));
                return;
            }

            if (EntityShapes.IsChild(stack.Leaf))
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.Stacks,
                    $"The stack of {entity} is over a child entity; a child is saved with its aggregate and served by its owner's stack and its child query surface.",
                    $"Remove the stack of {TypeDisplay.Short(stack.Leaf)}, and carry it in its parent's collection."));
                return;
            }

            // --- The service: the default, or the named one closed over the leaves ---
            Type service;
            try
            {
                service = stack.DeclaredService is Type declared
                    ? LeafClosure.Close(declared, leafOf)
                    : typeof(DefaultEntityService<,>).MakeGenericType(stack.Leaf, stack.KeyType);
            }
            catch (ArgumentException exception)
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.Stacks,
                    $"The service {TypeDisplay.Qualified(stack.DeclaredService ?? typeof(DefaultEntityService<,>))} of the stack of {entity} cannot be closed over the leaf: {exception.Message}",
                    "Constrain the service's type parameter by the entity it is written against, so every leaf satisfies it."));
                return;
            }

            Type expected = typeof(EntityService<,>).MakeGenericType(stack.Leaf, stack.KeyType);
            if (!expected.IsAssignableFrom(service))
            {
                bool forDefault = LeafClosure.GenericBase(service, typeof(EntityService<,>))?.GetGenericArguments()[0] == stack.EntityType
                    && stack.EntityType != stack.Leaf;
                problems.Add(new CompositionProblem(
                    CoreCheckNames.Stacks,
                    $"The service {TypeDisplay.Qualified(service)} of the stack of {entity} does not derive from {TypeDisplay.Short(expected)}.",
                    forDefault
                        ? $"Make the service generic over the entity ({TypeDisplay.Short(service)}<TEntity> where TEntity : {TypeDisplay.Short(stack.EntityType)}), or derive it from {TypeDisplay.Short(expected)}."
                        : $"Derive the service from {TypeDisplay.Short(expected)}."));
                return;
            }

            stack.ServiceType = service;
        }

        /// <summary>Closes each companion over its stack's leaf, in registration order.</summary>
        /// <param name="record">The realised composition.</param>
        /// <param name="byLeaf">The stacks by leaf.</param>
        /// <param name="problems">Receives the problems.</param>
        private static void AttachCompanions(TellmaCompositionRecord record, Dictionary<Type, ComposedStack> byLeaf, List<CompositionProblem> problems)
        {
            foreach (ContributedCompanion companion in record.Companions)
            {
                string named = TypeDisplay.Qualified(companion.CompanionType);
                Type leaf = record.LeafOf(companion.EntityType);
                if (!byLeaf.TryGetValue(leaf, out ComposedStack? stack))
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.StackActions,
                        $"The feature '{companion.Owner.Name}' attaches the stack companion {named} to {TypeDisplay.Qualified(companion.EntityType)}, which no stack owns.",
                        $"Register the entity's stack, or attach the companion to an entity a stack owns."));
                    continue;
                }

                if (Close(companion.CompanionType, companion.EntityType, leaf, out string? refusal) is not Type closed)
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.StackActions,
                        $"The stack companion {named} of {TypeDisplay.Qualified(leaf)}, attached by the feature '{companion.Owner.Name}', {refusal}",
                        $"Make the companion generic over the entity (Operations<TEntity> where TEntity : {TypeDisplay.Short(companion.EntityType)}) and attach it closed over {TypeDisplay.Short(companion.EntityType)}."));
                    continue;
                }

                if (!stack.Companions.Contains(closed))
                {
                    stack.Companions.Add(closed);
                }
            }
        }

        /// <summary>
        ///     Closes each component over the leaf of every stack it applies to, in registration order:
        ///     the stacks whose leaf is the component's entity or derives from it. A component that
        ///     reaches no stack is a problem, since it would never run.
        /// </summary>
        /// <param name="record">The realised composition.</param>
        /// <param name="stacks">The stacks.</param>
        /// <param name="problems">Receives the problems.</param>
        private static void AttachComponents(TellmaCompositionRecord record, List<ComposedStack> stacks, List<CompositionProblem> problems)
        {
            foreach (ContributedComponent component in record.Components)
            {
                string kind = ComponentContracts.Describe(component.Kind);
                string named = TypeDisplay.Qualified(component.ComponentType);
                List<ComposedStack> applicable = [.. stacks.Where(stack => component.EntityType.IsAssignableFrom(stack.Leaf))];

                // --- A component over no stack would never run ---
                if (applicable.Count == 0)
                {
                    string never = component.Kind == ComponentKind.PersistEffect ? "its ContributeAsync never runs" : "it never runs";
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.StackComponents,
                        $"The feature '{component.Owner.Name}' attaches the {kind} {named} to {TypeDisplay.Qualified(component.EntityType)}, which no stack owns, so {never}.",
                        "Register the entity's stack, or attach the component to an entity a stack owns."));
                    continue;
                }

                // Whether the component reached a stack, closed or refused with a problem of its own.
                bool reached = false;
                foreach (ComposedStack stack in applicable)
                {
                    // A component that is not generic runs on its own entity's stack alone; on a
                    // stack whose leaf substitutes that entity it cannot run, and on a stack of a
                    // derived entity it does not apply.
                    bool generic = component.ComponentType.IsGenericTypeDefinition
                        || (component.ComponentType.IsConstructedGenericType && component.ComponentType.GetGenericArguments() is [Type only] && only == component.EntityType);
                    if (!generic && stack.Leaf != component.EntityType && record.LeafOf(component.EntityType) != stack.Leaf)
                    {
                        continue;
                    }

                    reached = true;

                    if (Close(component.ComponentType, component.EntityType, stack.Leaf, out string? refusal) is not Type closed)
                    {
                        problems.Add(new CompositionProblem(
                            CoreCheckNames.StackComponents,
                            $"The {kind} {named}, attached by the feature '{component.Owner.Name}' to {TypeDisplay.Qualified(component.EntityType)}, {refusal}",
                            $"Make the component generic over one type parameter, the entity, constrained to {TypeDisplay.Short(component.EntityType)} (Rules<TEntity> where TEntity : {TypeDisplay.Short(component.EntityType)}), or constrain it to what every leaf has."));
                        continue;
                    }

                    Type contract = ComponentContracts.For(component.Kind, stack.Leaf);
                    if (!contract.IsAssignableFrom(closed))
                    {
                        problems.Add(new CompositionProblem(
                            CoreCheckNames.StackComponents,
                            $"The {kind} {TypeDisplay.Qualified(closed)}, attached by the feature '{component.Owner.Name}', does not implement {TypeDisplay.Short(contract)}, so it cannot run on the stack of {TypeDisplay.Qualified(stack.Leaf)}.",
                            $"Implement {TypeDisplay.Short(ComponentContracts.Definition(component.Kind))} over the component's type parameter."));
                        continue;
                    }

                    if (!stack.Components.Exists(existing => existing.ComponentType == closed && existing.Kind == component.Kind))
                    {
                        stack.Components.Add(new ComposedComponent(component.Kind, closed, component.Owner));
                    }
                }

                // --- A non-generic component whose entity only derived entities' stacks own ---
                if (!reached)
                {
                    string stacked = string.Join(", ", applicable.Select(static stack => TypeDisplay.Qualified(stack.Leaf)));
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.StackComponents,
                        $"The {kind} {named}, attached by the feature '{component.Owner.Name}' to {TypeDisplay.Qualified(component.EntityType)}, is not generic and no stack runs that entity, so it runs on none of the stacks of its derived entities ({stacked}).",
                        $"Attach the component to the entity of the stack it is for, or make it generic over one type parameter constrained to {TypeDisplay.Short(component.EntityType)}."));
                }
            }
        }

        /// <summary>
        ///     Closes a class written against an entity over a stack's leaf: an open definition over the
        ///     leaf, a construction over the entity alone re-closed over the leaf, any other class kept
        ///     when the leaf is the entity.
        /// </summary>
        /// <param name="type">The class as contributed.</param>
        /// <param name="entity">The entity it is written against.</param>
        /// <param name="leaf">The stack's leaf.</param>
        /// <param name="refusal">Why it cannot be closed, completing a sentence that names it; null when it can.</param>
        /// <returns>The closed class, or null.</returns>
        private static Type? Close(Type type, Type entity, Type leaf, out string? refusal)
        {
            refusal = null;
            Type? definition = type.IsGenericTypeDefinition
                ? type
                : type.IsConstructedGenericType && type.GetGenericArguments() is [Type only] && only == entity ? type.GetGenericTypeDefinition() : null;

            // --- Not generic over the entity: it runs only where the leaf is the entity ---
            if (definition is null)
            {
                if (leaf == entity)
                {
                    return type;
                }

                refusal = $"is not generic over one type parameter, the entity, so it cannot follow the leaf {TypeDisplay.Qualified(leaf)} that substitutes {TypeDisplay.Qualified(entity)}.";
                return null;
            }

            // --- Generic over the entity: closed over the leaf, which must meet its constraints ---
            try
            {
                return definition.MakeGenericType(leaf);
            }
            catch (ArgumentException)
            {
                refusal = $"has constraints the leaf {TypeDisplay.Qualified(leaf)} does not meet, so it cannot be closed over it.";
                return null;
            }
        }
    }

    /// <summary>One stack at the level of types: its entity, leaf and key, and the classes closed over its leaf.</summary>
    /// <param name="entityType">The entity as first registered, before leaf substitution.</param>
    /// <param name="leaf">The leaf the stack serves.</param>
    /// <param name="owner">The feature that first registered the entity, which the stack belongs to.</param>
    internal sealed class ComposedStack(Type entityType, Type leaf, ITellmaFeature owner)
    {
        /// <summary>The entity as first registered, before leaf substitution.</summary>
        public Type EntityType { get; } = entityType;

        /// <summary>The leaf the stack serves.</summary>
        public Type Leaf { get; } = leaf;

        /// <summary>The feature that first registered the entity; the stack and its securables belong to it.</summary>
        public ITellmaFeature Owner { get; } = owner;

        /// <summary>The feature whose registration holds: the owner, or a dependent that replaced its service or operations.</summary>
        public ITellmaFeature HeldBy { get; set; } = owner;

        /// <summary>The service class the holding registration names, before closing; null for the default.</summary>
        public Type? DeclaredService { get; set; }

        /// <summary>The operations the holding registration names, overriding the entity's declaration; null to keep it.</summary>
        public StackOperations? DeclaredOperations { get; set; }

        /// <summary>The operations the stack serves: the holding registration's, else the leaf's <see cref="StackAttribute" />'s, else all.</summary>
        public StackOperations Operations => DeclaredOperations ?? EntityShapes.GetAttribute<StackAttribute>(Leaf)?.Operations ?? StackOperations.All;

        /// <summary>The key type; null when the leaf is not a keyed entity.</summary>
        public Type? KeyType { get; set; }

        /// <summary>The service closed over the leaf; null when it could not be closed, which is a problem.</summary>
        public Type? ServiceType { get; set; }

        /// <summary>The companions closed over the leaf, in registration order.</summary>
        public List<Type> Companions { get; } = [];

        /// <summary>The components closed over the leaf, in registration order.</summary>
        public List<ComposedComponent> Components { get; } = [];
    }

    /// <summary>A component closed over a stack's leaf.</summary>
    /// <param name="Kind">The contract it implements.</param>
    /// <param name="ComponentType">The closed class.</param>
    /// <param name="Owner">The feature that attached it.</param>
    internal sealed record ComposedComponent(ComponentKind Kind, Type ComponentType, ITellmaFeature Owner);

    /// <summary>A routed service of the composition.</summary>
    /// <param name="ServiceType">The service class.</param>
    /// <param name="Owner">The feature that contributed it.</param>
    internal sealed record ComposedApiService(Type ServiceType, ITellmaFeature Owner);
}
