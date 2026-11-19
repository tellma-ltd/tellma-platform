// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data;

namespace Tellma.Core.Api
{
    // The routes follow spec 0016 §2.3 (stack operations), §2.4 (routed services) and spec 0033
    // §10's child query surfaces; the route facts follow spec 0015 §2.3.

    /// <summary>
    ///     The route model, derived once on first use from the stack registry: each stack's standard
    ///     operations, as its operations and capabilities imply them, then its actions; each child
    ///     query surface's query, then its actions; then each routed service's actions.
    /// </summary>
    /// <remarks>
    ///     <para>A standard operation's body, result and securable:</para>
    ///     <list type="table">
    ///         <item><term><c>query</c></term><description><see cref="QueryRequest" /> → <see cref="RowPage" />, <c>Read</c></description></item>
    ///         <item><term><c>get</c>, <c>get-by-ids</c>, <c>all</c></term><description><see cref="GetRequest" />, <see cref="GetByIdsRequest" />, <see cref="AllRequest" /> → the stack's <see cref="EntitiesResult{TEntity}" />, <c>Read</c></description></item>
    ///         <item><term><c>get-by-parent-ids</c></term><description><see cref="ParentIdsRequest" /> → a <see cref="RowPage" /> of rows alone, <c>Read</c></description></item>
    ///         <item><term><c>save</c></term><description><see cref="SaveRequest{TEntity}" /> → the stack's <see cref="EntitiesResult{TEntity}" />, <c>Save</c></description></item>
    ///         <item><term><c>delete</c>, <c>delete-with-descendants</c>, <c>delete-by-query</c></term><description><see cref="IdsRequest" /> or <see cref="DeleteByQueryRequest" /> → <see cref="AffectedResult" />, <c>Delete</c></description></item>
    ///     </list>
    ///     <para>
    ///         The reads are idempotent and not mutations; the writes are mutations and not
    ///         idempotent; none runs long or is admitted on a read-only tenant. An action's facts are
    ///         its descriptor's. An entity action's body is an <see cref="EntityActionRequest" /> when
    ///         it answers the stack's entities, an <see cref="IdRequest" /> when it targets one row
    ///         and answers its own result, else an <see cref="IdsRequest" />, each closed over its
    ///         arguments record when it takes one.
    ///     </para>
    /// </remarks>
    internal sealed class RouteContracts : IRouteContracts
    {
        /// <summary>The prefix of every projected route's template: the verb and the web surface's path.</summary>
        public const string TemplatePrefix = "POST /{tenantId}/api/web/";

        private readonly Lazy<Contents> _contents;

        /// <summary>Creates the model; it derives the routes on first use.</summary>
        /// <param name="registry">The stack registry.</param>
        /// <param name="metadata">The entity metadata, which the related projections are described from.</param>
        public RouteContracts(IStackRegistry registry, IEntityMetadataProvider metadata)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(metadata);
            _contents = new(() => Build(registry, metadata), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <inheritdoc />
        public IReadOnlyList<RouteContract> Routes => _contents.Value.Routes;

        /// <inheritdoc />
        public RouteContract? For(string routeTemplate)
        {
            ArgumentNullException.ThrowIfNull(routeTemplate);
            return _contents.Value.ByTemplate.GetValueOrDefault(routeTemplate);
        }

        /// <summary>The canonical description a route's fingerprint hashes, for review and tests.</summary>
        /// <param name="routeTemplate">The route's template.</param>
        /// <returns>The description, or null for a route the projection does not map.</returns>
        public string? DescriptionOf(string routeTemplate)
        {
            ArgumentNullException.ThrowIfNull(routeTemplate);
            return _contents.Value.Descriptions.GetValueOrDefault(routeTemplate);
        }

        /// <summary>Derives every route.</summary>
        /// <param name="registry">The stack registry.</param>
        /// <param name="metadata">The entity metadata.</param>
        /// <returns>The routes.</returns>
        private static Contents Build(IStackRegistry registry, IEntityMetadataProvider metadata)
        {
            Contents contents = new(new ContractDescriber(registry, metadata, TellmaJson.Options));

            // --- Each stack: its standard operations, then its actions ---
            foreach (StackDescriptor stack in registry.Stacks)
            {
                Revisions revisions = new(stack.ContractRevision, Surface: 0);
                foreach (Standard operation in StandardOperations(stack))
                {
                    contents.Add(stack.ResourceSegment, operation.Name, stack.Resource, action: null, operation.Request, operation.Response,
                        new SecurableRef(stack.Resource, operation.Securable), operation.IsMutation, allowWhenReadOnly: false, isLong: false, operation.Idempotent, revisions);
                }

                foreach (ActionDescriptor action in stack.Actions)
                {
                    contents.Add(stack.ResourceSegment, stack.Resource, action, RequestOf(action, stack.EntityType), SecurableOf(action, stack.Resource), revisions);
                }
            }

            // --- Each child query surface: its query, then its actions, under the root stack's revision and its own ---
            foreach (ChildQueryDescriptor child in registry.ChildQueries)
            {
                Revisions revisions = new(RootOf(registry, child)?.ContractRevision ?? 0, EntityShapes.GetAttribute<ApiResourceAttribute>(child.EntityType)?.ContractRevision ?? 0);
                contents.Add(child.ResourceSegment, "query", child.Resource, action: null, typeof(QueryRequest), typeof(RowPage),
                    new SecurableRef(child.Resource, AccessActions.Read), isMutation: false, allowWhenReadOnly: false, isLong: false, idempotent: true, revisions);
                foreach (ApiActionDescriptor action in child.Actions)
                {
                    contents.Add(child.ResourceSegment, child.Resource, action, action.RequestType, action.Securable, revisions);
                }
            }

            // --- Each routed service: its actions, under no stack ---
            foreach (ApiServiceDescriptor service in registry.Services)
            {
                foreach (ApiActionDescriptor action in service.Actions)
                {
                    contents.Add(service.Route, resource: null, action, action.RequestType, action.Securable, new Revisions(0, 0));
                }
            }

            return contents;
        }

        /// <summary>The standard operations a stack's operations and capabilities imply, in mapping order.</summary>
        /// <param name="stack">The stack.</param>
        /// <returns>The operations.</returns>
        private static IEnumerable<Standard> StandardOperations(StackDescriptor stack)
        {
            bool tree = stack.Capabilities.Contains("tree");
            Type entities = typeof(EntitiesResult<>).MakeGenericType(stack.EntityType);

            // --- The reads ---
            if (stack.Operations.HasFlag(StackOperations.Query))
            {
                yield return Standard.Read("query", typeof(QueryRequest), typeof(RowPage));
                if (tree)
                {
                    // The service answers a bare row set, which the wire wraps as a page of rows alone.
                    yield return Standard.Read("get-by-parent-ids", typeof(ParentIdsRequest), typeof(RowPage));
                }
            }

            if (stack.Operations.HasFlag(StackOperations.Details))
            {
                yield return Standard.Read("get", typeof(GetRequest), entities);
                yield return Standard.Read("get-by-ids", typeof(GetByIdsRequest), entities);
                if (stack.Capabilities.Contains("cacheable"))
                {
                    yield return Standard.Read("all", typeof(AllRequest), entities);
                }
            }

            // --- The writes ---
            if (stack.Operations.HasFlag(StackOperations.Save))
            {
                yield return Standard.Write("save", typeof(SaveRequest<>).MakeGenericType(stack.EntityType), entities, AccessActions.Save);
            }

            if (stack.Operations.HasFlag(StackOperations.Delete))
            {
                yield return Standard.Write("delete", typeof(IdsRequest), typeof(AffectedResult), AccessActions.Delete);
                if (stack.Operations.HasFlag(StackOperations.DeleteByQuery))
                {
                    yield return Standard.Write("delete-by-query", typeof(DeleteByQueryRequest), typeof(AffectedResult), AccessActions.Delete);
                }

                if (tree)
                {
                    yield return Standard.Write("delete-with-descendants", typeof(IdsRequest), typeof(AffectedResult), AccessActions.Delete);
                }
            }
        }

        /// <summary>The body an action takes on the wire.</summary>
        /// <param name="action">The action.</param>
        /// <param name="leaf">The stack's entity leaf.</param>
        /// <returns>The body's type; null for an API action that takes none.</returns>
        private static Type? RequestOf(ActionDescriptor action, Type leaf)
        {
            return action switch
            {
                EntityActionDescriptor entity when entity.ResultType == typeof(EntitiesResult<>).MakeGenericType(leaf) =>
                    Closed(typeof(EntityActionRequest), typeof(EntityActionRequest<>), entity.ArgumentsType),
                EntityActionDescriptor { SingleTarget: true } entity => Closed(typeof(IdRequest), typeof(IdRequest<>), entity.ArgumentsType),
                EntityActionDescriptor entity => Closed(typeof(IdsRequest), typeof(IdsRequest<>), entity.ArgumentsType),
                ApiActionDescriptor api => api.RequestType,
                _ => throw new InvalidOperationException($"The action '{action.Name}' is of a kind the route model does not know."),
            };

            static Type Closed(Type bare, Type generic, Type? arguments)
            {
                return arguments is null ? bare : generic.MakeGenericType(arguments);
            }
        }

        /// <summary>The securable a stack's action evaluates.</summary>
        /// <param name="action">The action.</param>
        /// <param name="resource">The stack's resource.</param>
        /// <returns>The securable; null for a member-only API action.</returns>
        private static SecurableRef? SecurableOf(ActionDescriptor action, string resource)
        {
            return action switch
            {
                EntityActionDescriptor entity => new SecurableRef(resource, entity.Action),
                ApiActionDescriptor api => api.Securable,
                _ => throw new InvalidOperationException($"The action '{action.Name}' is of a kind the route model does not know."),
            };
        }

        /// <summary>The stack at the root of a child query surface, through the surfaces above it.</summary>
        /// <param name="registry">The registry.</param>
        /// <param name="child">The surface.</param>
        /// <returns>The root stack; null when the chain does not reach one.</returns>
        private static StackDescriptor? RootOf(IStackRegistry registry, ChildQueryDescriptor child)
        {
            // A grandchild's owner is the surface above it; the depth is bounded by the model's nesting.
            string owner = child.OwnerResource;
            for (int depth = 0; depth <= registry.ChildQueries.Count; depth++)
            {
                if (registry.Find(owner) is StackDescriptor stack)
                {
                    return stack;
                }

                if (registry.FindChildQuery(owner) is not ChildQueryDescriptor above)
                {
                    return null;
                }

                owner = above.OwnerResource;
            }

            return null;
        }

        /// <summary>The contract revisions an endpoint's fingerprint takes beside its action's.</summary>
        /// <param name="Stack">The stack's, or the root stack's for a child query surface.</param>
        /// <param name="Surface">The child query surface's own; 0 elsewhere.</param>
        private readonly record struct Revisions(int Stack, int Surface);

        /// <summary>A standard operation's route facts.</summary>
        /// <param name="Name">The operation's segment.</param>
        /// <param name="Request">The body's type.</param>
        /// <param name="Response">The result's type.</param>
        /// <param name="Securable">The securable action on the stack's resource.</param>
        /// <param name="IsMutation">Whether the operation writes.</param>
        /// <param name="Idempotent">Whether a retry is safe.</param>
        private sealed record Standard(string Name, Type Request, Type Response, string Securable, bool IsMutation, bool Idempotent)
        {
            /// <summary>A read: under <c>Read</c>, idempotent, not a mutation.</summary>
            /// <param name="name">The segment.</param>
            /// <param name="request">The body's type.</param>
            /// <param name="response">The result's type.</param>
            /// <returns>The operation.</returns>
            public static Standard Read(string name, Type request, Type response)
            {
                return new Standard(name, request, response, AccessActions.Read, IsMutation: false, Idempotent: true);
            }

            /// <summary>A write: a mutation, not idempotent.</summary>
            /// <param name="name">The segment.</param>
            /// <param name="request">The body's type.</param>
            /// <param name="response">The result's type.</param>
            /// <param name="securable">The securable action.</param>
            /// <returns>The operation.</returns>
            public static Standard Write(string name, Type request, Type response, string securable)
            {
                return new Standard(name, request, response, securable, IsMutation: true, Idempotent: false);
            }
        }

        /// <summary>The routes as they are derived, with their lookups.</summary>
        /// <param name="describer">Describes each route's contracts.</param>
        private sealed class Contents(ContractDescriber describer)
        {
            private readonly List<RouteContract> _routes = [];

            /// <summary>The routes, in mapping order.</summary>
            public ReadOnlyCollection<RouteContract> Routes => _routes.AsReadOnly();

            /// <summary>The routes by template.</summary>
            public Dictionary<string, RouteContract> ByTemplate { get; } = new(StringComparer.Ordinal);

            /// <summary>The canonical description of each route, by template.</summary>
            public Dictionary<string, string> Descriptions { get; } = new(StringComparer.Ordinal);

            /// <summary>Adds an action's route, with the action's own facts.</summary>
            /// <param name="segment">The stack, surface or service segment.</param>
            /// <param name="resource">The stack's or surface's resource; null on a routed service.</param>
            /// <param name="action">The action.</param>
            /// <param name="request">The body's type.</param>
            /// <param name="securable">The securable.</param>
            /// <param name="revisions">The stack's and surface's revisions.</param>
            public void Add(string segment, string? resource, ActionDescriptor action, Type? request, SecurableRef? securable, Revisions revisions)
            {
                Add(segment, action.Name, resource, action, request, action.ResultType, securable,
                    action.Mutation, action.AllowWhenReadOnly, action.Long, action.Idempotent, revisions);
            }

            /// <summary>Adds one route, fingerprinting its contracts.</summary>
            /// <param name="segment">The stack, surface or service segment.</param>
            /// <param name="operation">The operation's segment or the action's name.</param>
            /// <param name="resource">The resource; null on a routed service.</param>
            /// <param name="action">The action serving the route; null for a standard operation.</param>
            /// <param name="request">The body's type.</param>
            /// <param name="response">The result's type.</param>
            /// <param name="securable">The securable.</param>
            /// <param name="isMutation">Whether the route writes.</param>
            /// <param name="allowWhenReadOnly">Whether a read-only tenant admits it.</param>
            /// <param name="isLong">Whether it runs long.</param>
            /// <param name="idempotent">Whether a retry is safe.</param>
            /// <param name="revisions">The stack's and surface's revisions.</param>
            /// <exception cref="InvalidOperationException">Another route already has the template.</exception>
            public void Add(
                string segment, string operation, string? resource, ActionDescriptor? action, Type? request, Type response, SecurableRef? securable,
                bool isMutation, bool allowWhenReadOnly, bool isLong, bool idempotent, Revisions revisions)
            {
                string template = TemplatePrefix + segment + "/" + operation;
                string description = describer.Describe(request, response, revisions.Stack, revisions.Surface, action?.ContractRevision ?? 0);
                RouteContract route = new(
                    template, operation, resource, action, request, response, securable,
                    isMutation, allowWhenReadOnly, isLong, idempotent, ContractDescriber.Fingerprint(description));

                // The registry's checks keep segments and action names apart, so a repeat is a defect here.
                if (!ByTemplate.TryAdd(template, route))
                {
                    throw new InvalidOperationException($"Two web endpoints share the route '{template}'.");
                }

                Descriptions[template] = description;
                _routes.Add(route);
            }
        }
    }
}
