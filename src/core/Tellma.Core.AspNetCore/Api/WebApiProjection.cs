// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Api;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.Composition;
using Tellma.Core.Crud;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §4.1-§4.3 and §3.10: the projector maps exactly the route model's entries onto the
    // Web group, each with the delegate its operation and action select and the metadata its members
    // state. Nothing else decides which web routes exist: the Contracts chunk and the snapshot read
    // the same entries.

    /// <summary>Projects the route model onto the web surface's group.</summary>
    internal static class WebApiProjection
    {
        /// <summary>The statuses every projected endpoint may answer with a problem.</summary>
        private static readonly int[] ProblemStatuses =
        [
            StatusCodes.Status400BadRequest,
            StatusCodes.Status401Unauthorized,
            StatusCodes.Status403Forbidden,
            StatusCodes.Status404NotFound,
            StatusCodes.Status409Conflict,
            StatusCodes.Status412PreconditionFailed,
            StatusCodes.Status413PayloadTooLarge,
            StatusCodes.Status422UnprocessableEntity,
            StatusCodes.Status503ServiceUnavailable,
        ];

        /// <summary>The polling routes, which must not stamp the caller's activity.</summary>
        private static readonly HashSet<string> PollingRoutes = new(StringComparer.Ordinal)
        {
            "users/me",
            "settings/client",
            "settings/entity-tags",
            "inbox/summary",
            "client/sync",
            "assignments/summary",
        };

        /// <summary>The standard operations' methods, by operation segment.</summary>
        private static readonly Dictionary<string, string> StandardMethods = new(StringComparer.Ordinal)
        {
            ["query"] = nameof(StandardEndpoints<,>.QueryAsync),
            ["get"] = nameof(StandardEndpoints<,>.GetAsync),
            ["get-by-ids"] = nameof(StandardEndpoints<,>.GetByIdsAsync),
            ["all"] = nameof(StandardEndpoints<,>.AllAsync),
            ["save"] = nameof(StandardEndpoints<,>.SaveAsync),
            ["delete"] = nameof(StandardEndpoints<,>.DeleteAsync),
            ["delete-by-query"] = nameof(StandardEndpoints<,>.DeleteByQueryAsync),
        };

        /// <summary>The tree operations' methods, by operation segment.</summary>
        private static readonly Dictionary<string, string> TreeMethods = new(StringComparer.Ordinal)
        {
            ["get-by-parent-ids"] = nameof(TreeEndpoints<,>.GetByParentIdsAsync),
            ["delete-with-descendants"] = nameof(TreeEndpoints<,>.DeleteWithDescendantsAsync),
        };

        /// <summary>
        ///     Maps every entry of the route model onto the web group. Maps nothing when no stack
        ///     registry is composed, or when the registry failed, whose own check then reports why.
        /// </summary>
        /// <param name="services">The application's services.</param>
        /// <param name="web">The web surface's group.</param>
        /// <returns>One problem per route the projection could not map.</returns>
        public static IReadOnlyList<CompositionProblem> Map(IServiceProvider services, RouteGroupBuilder web)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(web);
            if (services.GetService<StackRegistry>() is not StackRegistry registry || registry.Failed
                || services.GetService<IRouteContracts>() is not IRouteContracts contracts)
            {
                return [];
            }

            // --- The route model: derived on first use, which fails on a defect the registry let through ---
            IReadOnlyList<RouteContract> routes;
            try
            {
                routes = contracts.Routes;
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                return [new CompositionProblem(
                    TellmaEndpointAudit.CheckName,
                    $"The web surface's route model could not be derived from the stack registry: {exception.Message}",
                    "Fix the stack, child query surface or routed service the message names.")];
            }

            // --- What owns each route: actions by reference, standard operations by resource ---
            Owners owners = new(registry);
            RequireAssuranceMetadata? stepUp = services.GetService<StepUpBar>()?.Requirement;
            (ISecurableRegistry? securables, _) = SecurableRegistry.ForChecks(services);
            List<CompositionProblem> problems = [];
            foreach (RouteContract route in routes)
            {
                try
                {
                    MapRoute(web, route, owners, securables, stepUp, problems);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    // A closed generic whose constraints the stack's types do not meet, or a route the projection does not know.
                    problems.Add(Problem(route, $"could not be projected: {exception.Message}", "Fix the stack's entity, key or action types the message names."));
                }
            }

            // --- The child query surfaces are on by default, so the host states which it serves ---
            ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(ApiLog.Category);
            if (registry.ChildQueries.Count > 0 && logger.IsEnabled(LogLevel.Information))
            {
                string surfaces = string.Join("; ", registry.ChildQueries.Select(static child =>
                    string.Create(CultureInfo.InvariantCulture, $"{child.Resource} ({child.ResourceSegment}, owner {child.OwnerResource})")));
                ApiLog.ChildSurfacesProjected(logger, registry.ChildQueries.Count, surfaces);
            }

            return problems;
        }

        /// <summary>Maps one route with its delegate and metadata.</summary>
        /// <param name="web">The web surface's group.</param>
        /// <param name="route">The route.</param>
        /// <param name="owners">What owns each route.</param>
        /// <param name="securables">The securable registry, which says which securables are sensitive; null when it did not compose.</param>
        /// <param name="stepUp">The assurance a sensitive securable requires.</param>
        /// <param name="problems">Receives the problems.</param>
        private static void MapRoute(
            RouteGroupBuilder web, RouteContract route, Owners owners, ISecurableRegistry? securables, RequireAssuranceMetadata? stepUp, List<CompositionProblem> problems)
        {
            // --- The delegate the route's operation and action select ---
            (Delegate? handler, ProjectedRouteKind kind, string segment) = owners.Handler(route);
            if (handler is null)
            {
                problems.Add(Problem(route, "is in the route model, but nothing in the stack registry serves it.", "Report the route model's defect; every route is derived from a stack, a child query surface or a routed service."));
                return;
            }

            // --- A pre-serialized document leaves as its bytes, unchecked: only the platform's own actions may answer one ---
            if (route.Action is ActionDescriptor raw && ActionAnswers.IsPreSerialized(raw.ResultType) && !owners.PreSerialized(raw))
            {
                Assembly? declaring = owners.DeclaringAssembly(raw);
                problems.Add(Problem(
                    route,
                    $"answers (byte[] Json, string WireTag), the platform's pre-serialized JSON document, but its method is declared in {declaring?.GetName().Name ?? "no assembly"}, outside the platform.",
                    "Return a result record the host's JSON options serialize; only the platform's own actions answer bytes as JSON."));
                return;
            }

            // --- The body the delegate reads is the body the route's contract describes ---
            Type? body = handler.Method.GetParameters().FirstOrDefault(static parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null)?.ParameterType;
            if (body != route.RequestType)
            {
                problems.Add(Problem(
                    route,
                    $"reads a body of {TypeDisplay.Short(body ?? typeof(void))}, but its contract describes {TypeDisplay.Short(route.RequestType ?? typeof(void))}.",
                    "Report the projection's defect; the delegate and the route model must agree on the body."));
                return;
            }

            // --- The endpoint and the metadata of its members ---
            string relative = route.Template[RouteContracts.TemplatePrefix.Length..];
            RouteHandlerBuilder endpoint = web.MapPost(relative, handler)
                .WithMetadata(
                    new TenantEndpointMetadata(TenantSurface.Web, route.IsMutation, route.AllowWhenReadOnly),
                    new ContractMetadata(route.Fingerprint),
                    new ProjectedRouteMetadata(route, kind, segment));
            _ = route.Securable is SecurableRef securable
                ? endpoint.RequireSecurable(securable.Resource, securable.Action)
                : endpoint.AllowMember();

            // --- A polling route stamps no activity; step-up guards a sensitive securable ---
            if (PollingRoutes.Contains(relative))
            {
                _ = endpoint.WithMetadata(new NoActivityStampMetadata());
            }

            if (route.Securable is SecurableRef sensitive && stepUp is not null && securables?.Find(sensitive.Resource, sensitive.Action) is { IsSensitive: true })
            {
                _ = endpoint.WithMetadata(stepUp);
            }

            // --- What OpenAPI documents: the problems every endpoint may answer, and an action's result ---
            foreach (int status in ProblemStatuses)
            {
                _ = endpoint.ProducesProblem(status);
            }

            if (route.Action is ActionDescriptor action && handler.Method.ReturnType == typeof(Task<IResult>))
            {
                _ = action.ResultType == typeof(void)
                    ? endpoint.Produces(StatusCodes.Status204NoContent)
                    : action.ResultType == typeof(ExportOutcome)
                        ? endpoint.Produces(StatusCodes.Status200OK, responseType: null, ActionAnswers.WorkbookContentType)
                        : ActionAnswers.IsPreSerialized(action.ResultType)
                            ? endpoint.Produces(StatusCodes.Status200OK, responseType: null, ActionAnswers.JsonContentType)
                            : endpoint.Produces(ActionAnswers.StatusOf(action.ResultType), action.ResultType, ActionAnswers.JsonContentType);
            }

            // --- A long route runs under the long timeout ---
            if (route.Long)
            {
                _ = endpoint.WithRequestTimeout(TellmaRequestTimeouts.Long);
            }
        }

        /// <summary>A problem naming a route.</summary>
        /// <param name="route">The route.</param>
        /// <param name="description">What is wrong.</param>
        /// <param name="fix">How to fix it.</param>
        /// <returns>The problem.</returns>
        private static CompositionProblem Problem(RouteContract route, string description, string fix)
        {
            return new CompositionProblem(TellmaEndpointAudit.CheckName, $"The web route {route.Template} {description}", fix);
        }

        /// <summary>A delegate over a method, with the method's own parameter and return types.</summary>
        /// <param name="method">The method, closed.</param>
        /// <param name="target">The instance; null for a static method.</param>
        /// <returns>The delegate.</returns>
        private static Delegate DelegateOf(MethodInfo method, object? target)
        {
            Type[] signature = [.. method.GetParameters().Select(static parameter => parameter.ParameterType), method.ReturnType];
            return method.CreateDelegate(Expression.GetDelegateType(signature), target);
        }

        /// <summary>A public method of a type by name.</summary>
        /// <param name="type">The type.</param>
        /// <param name="name">The method's name.</param>
        /// <returns>The method.</returns>
        private static MethodInfo MethodOf(Type type, string name)
        {
            return type.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"{type.Name} declares no endpoint method {name}.");
        }

        /// <summary>What owns each route of the model, and the delegate each takes.</summary>
        private sealed class Owners
        {
            private readonly StackRegistry _registry;

            private readonly Dictionary<ActionDescriptor, (StackDescriptor? Stack, ChildQueryDescriptor? Child, ApiServiceDescriptor? Service)> _actions =
                new(ReferenceEqualityComparer.Instance);

            /// <summary>Indexes the registry's actions by reference: descriptors are the registry's own.</summary>
            /// <param name="registry">The stack registry.</param>
            public Owners(StackRegistry registry)
            {
                _registry = registry;
                foreach (StackDescriptor stack in registry.Stacks)
                {
                    foreach (ActionDescriptor action in stack.Actions)
                    {
                        _actions[action] = (stack, null, null);
                    }
                }

                foreach (ChildQueryDescriptor child in registry.ChildQueries)
                {
                    foreach (ApiActionDescriptor action in child.Actions)
                    {
                        _actions[action] = (null, child, null);
                    }
                }

                foreach (ApiServiceDescriptor service in registry.Services)
                {
                    foreach (ApiActionDescriptor action in service.Actions)
                    {
                        _actions[action] = (null, null, service);
                    }
                }
            }

            /// <summary>The assembly that declares an action's method; null for a built-in action.</summary>
            /// <param name="action">The action, as the registry described it.</param>
            /// <returns>The assembly, or null.</returns>
            public Assembly? DeclaringAssembly(ActionDescriptor action)
            {
                return _registry.MethodOf(action)?.Method.DeclaringType?.Assembly;
            }

            /// <summary>
            ///     Whether an action answers a pre-serialized JSON document as its bytes: its declared
            ///     result has the document's shape and a platform assembly declares its method.
            /// </summary>
            /// <param name="action">The action.</param>
            /// <returns>True when its result is written as its bytes.</returns>
            public bool PreSerialized(ActionDescriptor action)
            {
                return ActionAnswers.IsPreSerialized(action.ResultType) && DeclaringAssembly(action) is Assembly declaring && ActionAnswers.IsPlatform(declaring);
            }

            /// <summary>The delegate a route takes, with what it is mapped under.</summary>
            /// <param name="route">The route.</param>
            /// <returns>The delegate, null when nothing serves the route; its kind; and its segment.</returns>
            public (Delegate? Handler, ProjectedRouteKind Kind, string Segment) Handler(RouteContract route)
            {
                // --- An action: its owner's, found by the descriptor itself ---
                if (route.Action is ActionDescriptor action)
                {
                    if (!_actions.TryGetValue(action, out (StackDescriptor? Stack, ChildQueryDescriptor? Child, ApiServiceDescriptor? Service) owner))
                    {
                        return (null, ProjectedRouteKind.Stack, string.Empty);
                    }

                    return (owner, action) switch
                    {
                        ({ Stack: StackDescriptor stack }, EntityActionDescriptor entity) => (EntityAction(stack, entity, PreSerialized(entity)), ProjectedRouteKind.Stack, stack.ResourceSegment),
                        ({ Stack: StackDescriptor stack }, ApiActionDescriptor api) => (ApiAction(api, PreSerialized(api)), ProjectedRouteKind.Stack, stack.ResourceSegment),
                        ({ Child: ChildQueryDescriptor child }, ApiActionDescriptor api) => (ApiAction(api, PreSerialized(api)), ProjectedRouteKind.ChildQuery, child.ResourceSegment),
                        ({ Service: ApiServiceDescriptor service }, ApiActionDescriptor api) => (ApiAction(api, PreSerialized(api)), ProjectedRouteKind.Service, service.Route),
                        _ => (null, ProjectedRouteKind.Stack, string.Empty),
                    };
                }

                // --- A standard operation: the stack's, or a child query surface's query ---
                if (route.Resource is string resource && _registry.Find(resource) is StackDescriptor owning)
                {
                    return (Standard(owning, route.Operation), ProjectedRouteKind.Stack, owning.ResourceSegment);
                }

                if (route.Resource is string childResource && route.Operation == "query" && _registry.FindChildQuery(childResource) is ChildQueryDescriptor surface)
                {
                    ChildQueryEndpoint query = new(surface.Resource);
                    return (DelegateOf(MethodOf(typeof(ChildQueryEndpoint), nameof(ChildQueryEndpoint.QueryAsync)), query), ProjectedRouteKind.ChildQuery, surface.ResourceSegment);
                }

                return (null, ProjectedRouteKind.Stack, string.Empty);
            }

            /// <summary>A standard operation's delegate, closed over the stack's entity and key.</summary>
            /// <param name="stack">The stack.</param>
            /// <param name="operation">The operation's segment.</param>
            /// <returns>The delegate; null for an operation the projection does not know.</returns>
            private static Delegate? Standard(StackDescriptor stack, string operation)
            {
                Type? endpoints = StandardMethods.ContainsKey(operation)
                    ? typeof(StandardEndpoints<,>)
                    : TreeMethods.ContainsKey(operation) ? typeof(TreeEndpoints<,>) : null;
                if (endpoints is null)
                {
                    return null;
                }

                string name = StandardMethods.GetValueOrDefault(operation) ?? TreeMethods[operation];
                return DelegateOf(MethodOf(endpoints.MakeGenericType(stack.EntityType, stack.KeyType), name), target: null);
            }

            /// <summary>An entity action's delegate: its body follows its result, its target and its arguments.</summary>
            /// <param name="stack">The stack.</param>
            /// <param name="action">The action.</param>
            /// <param name="preSerialized">Whether the action answers a pre-serialized JSON document as its bytes.</param>
            /// <returns>The delegate.</returns>
            private static Delegate EntityAction(StackDescriptor stack, EntityActionDescriptor action, bool preSerialized)
            {
                Type closed = typeof(EntityActionEndpoints<,>).MakeGenericType(stack.EntityType, stack.KeyType);
                object target = Activator.CreateInstance(closed, action, preSerialized)!;
                bool entities = action.ResultType == typeof(EntitiesResult<>).MakeGenericType(stack.EntityType);
                Type? arguments = action.ArgumentsType;

                // The names are the generic definition's, which every closed type shares.
                MethodInfo method = (entities, action.SingleTarget, arguments) switch
                {
                    (true, _, null) => MethodOf(closed, nameof(EntityActionEndpoints<,>.EntitiesAsync)),
                    (true, _, Type args) => MethodOf(closed, nameof(EntityActionEndpoints<,>.EntitiesWithArgumentsAsync)).MakeGenericMethod(args),
                    (false, true, null) => MethodOf(closed, nameof(EntityActionEndpoints<,>.IdAsync)).MakeGenericMethod(action.ResultType),
                    (false, true, Type args) => MethodOf(closed, nameof(EntityActionEndpoints<,>.IdWithArgumentsAsync)).MakeGenericMethod(args, action.ResultType),
                    (false, false, null) => MethodOf(closed, nameof(EntityActionEndpoints<,>.IdsAsync)).MakeGenericMethod(action.ResultType),
                    (false, false, Type args) => MethodOf(closed, nameof(EntityActionEndpoints<,>.IdsWithArgumentsAsync)).MakeGenericMethod(args, action.ResultType),
                };
                return DelegateOf(method, target);
            }

            /// <summary>An API action's delegate: its request type, or no body.</summary>
            /// <param name="action">The action.</param>
            /// <param name="preSerialized">Whether the action answers a pre-serialized JSON document as its bytes.</param>
            /// <returns>The delegate.</returns>
            private static Delegate ApiAction(ApiActionDescriptor action, bool preSerialized)
            {
                ApiActionEndpoint target = new(action, preSerialized);
                MethodInfo method = action.RequestType is Type request
                    ? MethodOf(typeof(ApiActionEndpoint), nameof(ApiActionEndpoint.InvokeAsync)).MakeGenericMethod(request)
                    : MethodOf(typeof(ApiActionEndpoint), nameof(ApiActionEndpoint.InvokeWithoutBodyAsync));
                return DelegateOf(method, target);
            }
        }
    }
}
