// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Crud.Save;
using Tellma.Core.Data.Metadata;

namespace Tellma.Core.Crud
{
    /// <summary>
    ///     The composition's stacks, child query surfaces, routed services and enlistment pairs, built
    ///     once on first use from the settled stacks and the leaves' entity metadata, with no tenant
    ///     scope. A singleton; it also answers the link builder and the endpoint audit's question of
    ///     which segment a resource's stack sits at.
    /// </summary>
    /// <remarks>
    ///     Every problem the build finds is kept, sourced to the startup check that reports it, so the
    ///     realised gate fails startup on it. A stack whose service, shape or metadata does not
    ///     conform has no descriptor; the rest of the registry still builds. When the tenant model
    ///     itself fails, the registry is empty and <see cref="Failed" />; the entity-metadata check
    ///     reports why.
    /// </remarks>
    internal sealed class StackRegistry : IStackRegistry, IStackRouteSource
    {
        private readonly Lazy<Contents> _contents;

        /// <summary>Creates the registry; it builds on first use.</summary>
        /// <param name="composition">The settled stacks.</param>
        /// <param name="metadata">The source of the leaves' entity metadata.</param>
        /// <param name="root">The root service provider, from which each named filter contributor is created once.</param>
        public StackRegistry(StackComposition composition, EntityMetadataSource metadata, IServiceProvider root)
        {
            ArgumentNullException.ThrowIfNull(composition);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(root);
            _contents = new(() => Build(composition, metadata, root), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>Whether the registry could not be built because the tenant model failed; it is then empty.</summary>
        public bool Failed => _contents.Value.Failed;

        /// <summary>Every problem found, each with its startup check's name as its source.</summary>
        public IReadOnlyList<CompositionProblem> Problems => _contents.Value.Problems;

        /// <inheritdoc />
        public IReadOnlyList<StackDescriptor> Stacks => _contents.Value.Stacks;

        /// <inheritdoc />
        public IReadOnlyList<ChildQueryDescriptor> ChildQueries => _contents.Value.ChildQueries;

        /// <inheritdoc />
        public IReadOnlyList<ApiServiceDescriptor> Services => _contents.Value.Services;

        /// <inheritdoc />
        public IReadOnlyList<EnlistmentDescriptor> Enlistments => _contents.Value.Enlistments;

        /// <inheritdoc />
        IReadOnlyCollection<StackRoute> IStackRouteSource.Routes => _contents.Value.Routes;

        /// <inheritdoc />
        public StackDescriptor? Find(string resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return _contents.Value.ByResource.GetValueOrDefault(resource);
        }

        /// <summary>The stack whose leaf is an entity type: the stack a service of that entity runs.</summary>
        /// <param name="leaf">The leaf entity type.</param>
        /// <returns>The stack; null when no stack has the leaf.</returns>
        public StackDescriptor? FindByLeaf(Type leaf)
        {
            ArgumentNullException.ThrowIfNull(leaf);
            return _contents.Value.ByLeaf.GetValueOrDefault(leaf);
        }

        /// <inheritdoc />
        public ChildQueryDescriptor? FindChildQuery(string resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return _contents.Value.ChildByResource.GetValueOrDefault(resource);
        }

        /// <inheritdoc />
        public StackDescriptor? OwnerOf(TableName table)
        {
            ArgumentNullException.ThrowIfNull(table);
            return _contents.Value.ByTable.GetValueOrDefault((table.Schema, table.Name));
        }

        /// <inheritdoc />
        StackRoute? IStackRouteSource.Find(string resource)
        {
            return Find(resource) is StackDescriptor stack ? new StackRoute(stack.ResourceSegment, stack.Operations) : null;
        }

        /// <summary>
        ///     The method behind a declared action and the class it is reached through, found by the
        ///     descriptor instance this registry owns: a copy, even an equal one, finds nothing.
        /// </summary>
        /// <param name="action">A descriptor from <see cref="Stacks" /> or <see cref="Services" />.</param>
        /// <returns>The method, or null for a built-in action or a descriptor this registry does not own.</returns>
        public ActionMethod? MethodOf(ActionDescriptor action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return _contents.Value.Methods.GetValueOrDefault(action);
        }

        /// <summary>The stack a class of the composition is attached to: as the stack's service, a companion or a component.</summary>
        /// <param name="writer">The class.</param>
        /// <returns>The stack, or null for a class attached to none, such as a routed service or a job handler.</returns>
        public StackDescriptor? StackOfWriter(Type writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            return _contents.Value.WriterStacks.GetValueOrDefault(writer);
        }

        /// <summary>The classes attached to a stack, which take part in its writes: its service, companions and components.</summary>
        /// <param name="resource">The stack's resource.</param>
        /// <returns>The classes; empty for a resource no stack serves.</returns>
        public IReadOnlySet<Type> ParticipantsOf(string resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return _contents.Value.Participants.GetValueOrDefault(resource) ?? FrozenSet<Type>.Empty;
        }

        /// <summary>Builds the registry's contents.</summary>
        /// <param name="composition">The settled stacks.</param>
        /// <param name="source">The source of the entity metadata.</param>
        /// <param name="root">The root service provider.</param>
        /// <returns>The contents.</returns>
        private static Contents Build(StackComposition composition, EntityMetadataSource source, IServiceProvider root)
        {
            List<CompositionProblem> problems = [.. composition.Problems];
            if (source.Build.Provider is not IEntityMetadataProvider provider)
            {
                return Contents.Empty(problems);
            }

            StackDescriptorBuilder builder = new(root, problems, composition.Platform);
            List<StackDescriptor> stacks = [];
            List<ChildQueryDescriptor> children = [];
            Dictionary<(string Schema, string Name), StackDescriptor> byTable = new(TableComparer.Instance);
            Dictionary<Type, StackDescriptor> byLeaf = [];

            // --- The stacks, in registration order, each with its child query surfaces ---
            foreach (ComposedStack composed in composition.Stacks.Where(static stack => stack.ServiceType is not null))
            {
                if (provider.All.FirstOrDefault(entity => entity.ClrType == composed.Leaf) is not EntityMetadata metadata)
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.Stacks,
                        $"The stack of {TypeDisplay.Qualified(composed.Leaf)} is over an entity the tenant model does not map.",
                        "Contribute the entity with contribution.Entity<TEntity>() so the model maps its table."));
                    continue;
                }

                StackDescriptor stack = builder.Build(composed, metadata);
                CheckOperations(stack, composed, metadata, problems);
                stacks.Add(stack);
                byLeaf[composed.Leaf] = stack;
                children.AddRange(builder.ChildQueries(stack, metadata));
                MapTables(metadata, stack, byTable);
            }

            // --- The routed services ---
            List<ApiServiceDescriptor> services = [];
            foreach (ComposedApiService composed in composition.ApiServices)
            {
                if (ApiService(composed, builder.Methods, problems) is ApiServiceDescriptor service)
                {
                    services.Add(service);
                }
            }

            // --- What the descriptors must agree on together: segments, entity references, enlistments ---
            CheckSegments(stacks, children, services, problems);
            Dictionary<string, StackDescriptor> byResource = new(StringComparer.Ordinal);
            foreach (StackDescriptor stack in stacks)
            {
                _ = byResource.TryAdd(stack.Resource, stack);
            }

            CheckEntityReferences(stacks, byResource, problems);
            List<EnlistmentDescriptor> enlistments = DeclaredEnlistments(composition, byLeaf, problems);
            Dictionary<string, ChildQueryDescriptor> childByResource = new(StringComparer.Ordinal);
            foreach (ChildQueryDescriptor child in children)
            {
                _ = childByResource.TryAdd(child.Resource, child);
            }

            // --- The classes attached to each stack, which take part in its writes ---
            Dictionary<Type, StackDescriptor> writerStacks = [];
            Dictionary<string, HashSet<Type>> participants = new(StringComparer.Ordinal);
            foreach ((Type writer, Type? leaf) in composition.Writers())
            {
                if (leaf is not null && byLeaf.TryGetValue(leaf, out StackDescriptor? owner))
                {
                    writerStacks[writer] = owner;
                    if (!participants.TryGetValue(owner.Resource, out HashSet<Type>? attached))
                    {
                        participants[owner.Resource] = attached = [];
                    }

                    _ = attached.Add(writer);
                }
            }

            return new Contents(
                Failed: false,
                problems,
                stacks.AsReadOnly(),
                children.AsReadOnly(),
                services.AsReadOnly(),
                enlistments.AsReadOnly(),
                byResource,
                childByResource,
                byTable,
                byLeaf,
                builder.Methods,
                [.. stacks.Select(static stack => new StackRoute(stack.ResourceSegment, stack.Operations))],
                writerStacks,
                participants.ToDictionary(static entry => entry.Key, static entry => (IReadOnlySet<Type>)entry.Value, StringComparer.Ordinal));
        }

        /// <summary>
        ///     Reports an operation set the pipeline cannot serve: a save without the details plan it
        ///     reads back through, an import without what it reads and writes, an export without a query,
        ///     a delete by query without the delete, over a security table, or on a stack whose deletes
        ///     are judged.
        /// </summary>
        /// <param name="stack">The stack.</param>
        /// <param name="composed">The stack as composed, whose service and validators are judged.</param>
        /// <param name="metadata">The stack's entity, whose tables are judged.</param>
        /// <param name="problems">Receives the problems.</param>
        private static void CheckOperations(StackDescriptor stack, ComposedStack composed, EntityMetadata metadata, List<CompositionProblem> problems)
        {
            StackOperations operations = stack.Operations;
            if (operations.HasFlag(StackOperations.Save) && !operations.HasFlag(StackOperations.Details))
            {
                Report(stack, "keeps Save but omits Details; a save reads its rows back through the details plan.", "Add Details to the operations, or remove Save.");
            }

            const StackOperations ImportNeeds = StackOperations.Query | StackOperations.Details | StackOperations.Save;
            if (operations.HasFlag(StackOperations.Import) && (operations & ImportNeeds) != ImportNeeds)
            {
                Report(stack, "keeps Import without all of Query, Details and Save; an import reads the rows it matches and saves them.", "Add Query, Details and Save to the operations, or remove Import.");
            }

            if (operations.HasFlag(StackOperations.Export) && !operations.HasFlag(StackOperations.Query))
            {
                Report(stack, "keeps Export but omits Query; an export runs the stack's query.", "Add Query to the operations, or remove Export.");
            }

            if (operations.HasFlag(StackOperations.DeleteByQuery) && !operations.HasFlag(StackOperations.Delete))
            {
                Report(stack, "keeps DeleteByQuery but omits Delete; a delete by query is a delete, under its securable action.", "Add Delete to the operations, or remove DeleteByQuery.");
            }

            // A delete by query runs no validation hook, so a stack over a security table would let
            // it delete past the rules that keep the tenant's administrators (spec 0014 §3.9).
            if (operations.HasFlag(StackOperations.DeleteByQuery) && StackTableSet.WritesSecurityTable(StackTableSet.Of(metadata)))
            {
                Report(stack, "keeps DeleteByQuery over a security table; its rows are deleted by their ids, so each deletion meets the rules that keep the tenant's administrators.", "Remove DeleteByQuery from the operations.");
            }

            // A delete by query names no ids a validator could see, so it runs no delete validation;
            // a stack whose deletes are judged would let it delete past its own rules. Until the
            // pipeline judges a delete by query, such a stack omits it.
            else if (operations.HasFlag(StackOperations.DeleteByQuery) && composed.ServiceType is Type service
                && HookOverrides.ValidatesDeletes(service, composed.Components.Where(static component => component.Kind == ComponentKind.Validator).Select(static component => component.ComponentType)))
            {
                Report(stack, "keeps DeleteByQuery while its service or a validator judges its deletes; a delete by query names no ids those rules could see, so it would delete past them.", "Remove DeleteByQuery from the operations, so every delete of the stack's rows is by ids and meets its rules.");
            }

            void Report(StackDescriptor stack, string problem, string fix)
            {
                problems.Add(new CompositionProblem(CoreCheckNames.Stacks, $"The stack of {stack.Resource} declares the operations {stack.Operations}, which {problem}", fix));
            }
        }

        /// <summary>Maps the stack's root table and every child table at any depth to the stack.</summary>
        /// <param name="metadata">The entity whose tables are mapped.</param>
        /// <param name="stack">The owning stack.</param>
        /// <param name="byTable">Receives the tables.</param>
        private static void MapTables(EntityMetadata metadata, StackDescriptor stack, Dictionary<(string Schema, string Name), StackDescriptor> byTable)
        {
            _ = byTable.TryAdd((metadata.Table.Schema, metadata.Table.Name), stack);
            foreach (ChildCollectionMetadata child in metadata.Children)
            {
                MapTables(child.Child, stack, byTable);
            }
        }

        /// <summary>
        ///     The descriptor of a routed service: its route from <see cref="ApiRouteAttribute" />, and
        ///     each <see cref="ApiActionAttribute" /> method, secured by the resource and action the
        ///     attribute names, or member-only.
        /// </summary>
        /// <param name="composed">The service.</param>
        /// <param name="methods">Receives the method behind each action.</param>
        /// <param name="problems">Receives the problems.</param>
        /// <returns>The descriptor, or null when the service names no route.</returns>
        private static ApiServiceDescriptor? ApiService(ComposedApiService composed, Dictionary<ActionDescriptor, ActionMethod> methods, List<CompositionProblem> problems)
        {
            Type type = composed.ServiceType;
            string service = TypeDisplay.Qualified(type);
            if (type.GetCustomAttribute<ApiRouteAttribute>(inherit: true) is not ApiRouteAttribute route)
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.StackActions,
                    $"The routed service {service}, contributed by the feature '{composed.Owner.Name}', carries no [ApiRoute], so no route reaches it.",
                    "Mark the service [ApiRoute(\"route\")] with its kebab-case route, such as 'inbox'."));
                return null;
            }

            if (!StackNames.IsActionName(route.Route))
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.StackActions,
                    $"The routed service {service} takes the route '{route.Route}', which is not one or more kebab-case segments joined by '/'.",
                    "Name the route like 'inbox' or 'settings'."));
                return null;
            }

            List<ApiActionDescriptor> actions = [];
            HashSet<string> taken = new(StringComparer.Ordinal);
            foreach (MethodInfo method in ActionMethods.Declared(type))
            {
                string described = ActionMethods.Describe(type, method);
                if (method.GetCustomAttribute<ApiActionAttribute>(inherit: true) is not ApiActionAttribute attribute)
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.StackActions,
                        $"The method {described} declares [EntityAction], but {TypeDisplay.Short(type)} is a routed service; an entity action is declared on a stack's service.",
                        "Move the action to the stack's service, or declare it as an [ApiAction]."));
                    continue;
                }

                // --- The name: well formed and unique on the service ---
                if (!StackNames.IsActionName(attribute.Name))
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.StackActions,
                        $"The action of {described} is named '{attribute.Name}', which is not one or more kebab-case segments joined by '/'.",
                        "Name the action like 'mark-read' or 'me/save'."));
                    continue;
                }

                if (!taken.Add(attribute.Name))
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.StackActions,
                        $"The action of {described} is named '{attribute.Name}', which another action of {TypeDisplay.Short(type)} already takes.",
                        "Give each action of the service its own name."));
                    continue;
                }

                // --- The securable: the resource the attribute names, or none for a member-only action ---
                SecurableRef? securable;
                if (attribute.MemberOnly)
                {
                    if (attribute.Resource is not null || attribute.Action is not null)
                    {
                        problems.Add(new CompositionProblem(
                            CoreCheckNames.StackActions,
                            $"The API action '{attribute.Name}' of {described} is member-only but names a securable; a member-only action requires none.",
                            "Remove Resource and Action, or remove MemberOnly."));
                        continue;
                    }

                    securable = null;
                }
                else if (attribute.Resource is null)
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.StackActions,
                        $"The API action '{attribute.Name}' of {described} names neither a Resource nor MemberOnly; an action of a routed service has no stack whose resource secures it.",
                        "Name the securable with Resource (and Action, when it is not the name's last segment), or mark the action MemberOnly."));
                    continue;
                }
                else
                {
                    securable = new SecurableRef(attribute.Resource, attribute.Action ?? StackNames.ActionOf(attribute.Name));
                }

                if (ActionMethods.Api(type, method, attribute, securable, problems) is ApiActionDescriptor action)
                {
                    actions.Add(action);
                    methods[action] = new ActionMethod(type, method, route.Route);
                }
            }

            return new ApiServiceDescriptor(route.Route, type, actions.AsReadOnly(), composed.Owner.Name);
        }

        /// <summary>
        ///     Reports a resource segment outside the grammar, and one segment taken twice across the
        ///     stacks, the child query surfaces and the routed services' first segments.
        /// </summary>
        /// <param name="stacks">The stacks.</param>
        /// <param name="children">The child query surfaces.</param>
        /// <param name="services">The routed services.</param>
        /// <param name="problems">Receives the problems.</param>
        private static void CheckSegments(List<StackDescriptor> stacks, List<ChildQueryDescriptor> children, List<ApiServiceDescriptor> services, List<CompositionProblem> problems)
        {
            // Segments route ignoring case, so two that differ only in case collide.
            Dictionary<string, string> taken = new(StringComparer.OrdinalIgnoreCase);
            IEnumerable<(string Segment, string Owner)> segments = stacks.Select(static stack => (stack.ResourceSegment, $"the stack of {stack.Resource}"))
                .Concat(children.Select(static child => (child.ResourceSegment, $"the child query surface of {child.Resource}")))
                .Concat(services.Select(static service => (service.Route.Split('/')[0], $"the routed service {TypeDisplay.Qualified(service.ServiceType)}")));
            foreach ((string segment, string owner) in segments)
            {
                if (!StackNames.IsSegment(segment))
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.Stacks,
                        $"{Capitalise(owner)} takes the resource segment '{segment}', which is not kebab-case.",
                        "Name the segment in lowercase letters and digits joined by hyphens, with [ApiResource(Segment = \"...\")] when the table's name does not give one."));
                    continue;
                }

                if (taken.TryGetValue(segment, out string? earlier))
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.Stacks,
                        $"{Capitalise(owner)} takes the resource segment '{segment}', which {earlier} already takes.",
                        "Give one of them another segment with [ApiResource(Segment = \"...\")] or [ApiRoute]."));
                    continue;
                }

                taken[segment] = owner;
            }

            static string Capitalise(string text)
            {
                return string.Concat(text[..1].ToUpperInvariant(), text[1..]);
            }
        }

        /// <summary>
        ///     Reports an <see cref="EntityRefAttribute" /> on an entity action's arguments that names a
        ///     resource no stack with <see cref="StackOperations.Query" /> owns: the reference is
        ///     resolved through that stack's query.
        /// </summary>
        /// <param name="stacks">The stacks.</param>
        /// <param name="byResource">The stacks by resource.</param>
        /// <param name="problems">Receives the problems.</param>
        private static void CheckEntityReferences(List<StackDescriptor> stacks, Dictionary<string, StackDescriptor> byResource, List<CompositionProblem> problems)
        {
            foreach (StackDescriptor stack in stacks)
            {
                foreach (EntityActionDescriptor action in stack.Actions.OfType<EntityActionDescriptor>().Where(static action => action.ArgumentsType is not null))
                {
                    foreach (PropertyInfo property in action.ArgumentsType!.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (property.GetCustomAttribute<EntityRefAttribute>(inherit: true) is not EntityRefAttribute reference)
                        {
                            continue;
                        }

                        if (!byResource.TryGetValue(reference.Resource, out StackDescriptor? target) || !target.Operations.HasFlag(StackOperations.Query))
                        {
                            problems.Add(new CompositionProblem(
                                CoreCheckNames.StackActions,
                                $"The argument {TypeDisplay.Short(action.ArgumentsType)}.{property.Name} of the action '{action.Name}' of {stack.Resource} references '{reference.Resource}', which no stack with Query owns.",
                                "Name the resource of a stack that serves queries, as its entity is named, such as 'gl.Center'."));
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     The declared writer → target pairs: every <see cref="IEnlists{TTarget}" /> on a class of
        ///     the composition, each target a stack that accepts enlisted writes. Reports a target
        ///     that is no stack's or omits <see cref="StackOperations.Enlist" />, and a cycle among the
        ///     stacks other than a self-pair.
        /// </summary>
        /// <param name="composition">The settled stacks.</param>
        /// <param name="byLeaf">The stacks by leaf.</param>
        /// <param name="problems">Receives the problems.</param>
        /// <returns>The pairs, in writer order.</returns>
        private static List<EnlistmentDescriptor> DeclaredEnlistments(StackComposition composition, Dictionary<Type, StackDescriptor> byLeaf, List<CompositionProblem> problems)
        {
            List<EnlistmentDescriptor> pairs = [];
            Dictionary<string, HashSet<string>> edges = new(StringComparer.Ordinal);
            foreach ((Type writer, Type? leaf) in composition.Writers())
            {
                StackDescriptor? source = leaf is null ? null : byLeaf.GetValueOrDefault(leaf);
                foreach (Type declared in writer.GetInterfaces().Where(static type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnlists<>)))
                {
                    Type target = declared.GetGenericArguments()[0];
                    string named = TypeDisplay.Qualified(writer);
                    if (composition.StackOf(target) is not ComposedStack stack || !byLeaf.TryGetValue(stack.Leaf, out StackDescriptor? descriptor))
                    {
                        problems.Add(new CompositionProblem(
                            CoreCheckNames.Enlistments,
                            $"The writer {named} declares IEnlists<{TypeDisplay.Short(target)}>, but no stack serves {TypeDisplay.Qualified(target)}.",
                            "Declare IEnlists<> of an entity a stack serves, or register that entity's stack."));
                        continue;
                    }

                    if (!descriptor.Operations.HasFlag(StackOperations.Enlist))
                    {
                        problems.Add(new CompositionProblem(
                            CoreCheckNames.Enlistments,
                            $"The writer {named} declares IEnlists<{TypeDisplay.Short(target)}>, but the stack of {descriptor.Resource} does not accept enlisted writes.",
                            $"Add StackOperations.Enlist to the operations of {descriptor.Resource}, or remove the declaration."));
                        continue;
                    }

                    pairs.Add(new EnlistmentDescriptor(writer, named, descriptor.Resource));

                    // A self-pair writes the writer's own stack from an open frame only, so it is no edge.
                    if (source is not null && !string.Equals(source.Resource, descriptor.Resource, StringComparison.Ordinal))
                    {
                        if (!edges.TryGetValue(source.Resource, out HashSet<string>? targets))
                        {
                            edges[source.Resource] = targets = new(StringComparer.Ordinal);
                        }

                        _ = targets.Add(descriptor.Resource);
                    }
                }
            }

            if (Cycle(edges) is IReadOnlyList<string> cycle)
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.Enlistments,
                    $"The declared enlistments form a cycle: {string.Join(" → ", cycle)}; a stack's hooks would re-enter a stack whose hooks enlisted them.",
                    "Remove one declaration of the cycle; a write-back belongs to the source document's own action, never to the stack it wrote."));
            }

            return pairs;
        }

        /// <summary>The first cycle of a directed graph, found depth first in key order, its first node repeated at its end.</summary>
        /// <param name="edges">The edges by source.</param>
        /// <returns>The cycle, or null when the graph is acyclic.</returns>
        private static List<string>? Cycle(Dictionary<string, HashSet<string>> edges)
        {
            Dictionary<string, int> state = new(StringComparer.Ordinal);
            List<string> path = [];
            foreach (string start in edges.Keys.Order(StringComparer.Ordinal))
            {
                if (Visit(start) is List<string> cycle)
                {
                    return cycle;
                }
            }

            return null;

            // 0 unvisited, 1 on the path, 2 done.
            List<string>? Visit(string node)
            {
                int at = state.GetValueOrDefault(node);
                if (at == 1)
                {
                    return [.. path[path.IndexOf(node)..], node];
                }

                if (at == 2)
                {
                    return null;
                }

                state[node] = 1;
                path.Add(node);
                foreach (string next in edges.GetValueOrDefault(node)?.Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>())
                {
                    if (Visit(next) is List<string> cycle)
                    {
                        return cycle;
                    }
                }

                path.RemoveAt(path.Count - 1);
                state[node] = 2;
                return null;
            }
        }

        /// <summary>Compares table names ignoring case, as the database does.</summary>
        private sealed class TableComparer : IEqualityComparer<(string Schema, string Name)>
        {
            /// <summary>The one instance.</summary>
            public static TableComparer Instance { get; } = new();

            /// <inheritdoc />
            public bool Equals((string Schema, string Name) x, (string Schema, string Name) y)
            {
                return StringComparer.OrdinalIgnoreCase.Equals(x.Schema, y.Schema) && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);
            }

            /// <inheritdoc />
            public int GetHashCode((string Schema, string Name) obj)
            {
                return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Schema), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
            }
        }

        /// <summary>What the registry holds once built.</summary>
        /// <param name="Failed">Whether the tenant model failed, leaving the registry empty.</param>
        /// <param name="Problems">Every problem found.</param>
        /// <param name="Stacks">The stacks.</param>
        /// <param name="ChildQueries">The child query surfaces.</param>
        /// <param name="Services">The routed services.</param>
        /// <param name="Enlistments">The declared enlistment pairs.</param>
        /// <param name="ByResource">The stacks by resource.</param>
        /// <param name="ChildByResource">The child query surfaces by resource.</param>
        /// <param name="ByTable">The owning stack of every root and child table.</param>
        /// <param name="ByLeaf">The stacks by leaf entity type.</param>
        /// <param name="Methods">The method behind each declared action, by descriptor instance.</param>
        /// <param name="Routes">Every stack's route.</param>
        /// <param name="WriterStacks">The stack each attached class belongs to: its service, companions and components.</param>
        /// <param name="Participants">The classes attached to each stack, by resource.</param>
        private sealed record Contents(
            bool Failed,
            IReadOnlyList<CompositionProblem> Problems,
            IReadOnlyList<StackDescriptor> Stacks,
            IReadOnlyList<ChildQueryDescriptor> ChildQueries,
            IReadOnlyList<ApiServiceDescriptor> Services,
            IReadOnlyList<EnlistmentDescriptor> Enlistments,
            Dictionary<string, StackDescriptor> ByResource,
            Dictionary<string, ChildQueryDescriptor> ChildByResource,
            Dictionary<(string Schema, string Name), StackDescriptor> ByTable,
            Dictionary<Type, StackDescriptor> ByLeaf,
            Dictionary<ActionDescriptor, ActionMethod> Methods,
            IReadOnlyCollection<StackRoute> Routes,
            Dictionary<Type, StackDescriptor> WriterStacks,
            Dictionary<string, IReadOnlySet<Type>> Participants)
        {
            /// <summary>The contents of a registry whose tenant model failed.</summary>
            /// <param name="problems">The problems found before the model was needed.</param>
            /// <returns>The contents.</returns>
            public static Contents Empty(List<CompositionProblem> problems)
            {
                return new Contents(true, problems, [], [], [], [], [], [], new(TableComparer.Instance), [], new(ReferenceEqualityComparer.Instance), [], [], new(StringComparer.Ordinal));
            }
        }
    }
}
