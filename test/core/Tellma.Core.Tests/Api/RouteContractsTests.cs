// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Api;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Crud;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Api
{
    /// <summary>
    ///     The web surface's route model: the routes each stack, child query surface and routed service
    ///     projects, the facts each carries, the fingerprints of their contracts and the client
    ///     metadata's contracts chunk written from them.
    /// </summary>
    /// <remarks>
    ///     The routes and fingerprints are pinned as text, so a fingerprint that differed between
    ///     operating systems fails on one of them. After an intended change, run the suite once with
    ///     <c>TELLMA_UPDATE_GOLDENS=1</c>: the run rewrites the goldens and fails on purpose, and the
    ///     next run passes once the diff is reviewed.
    /// </remarks>
    public class RouteContractsTests
    {
        private const string UpdateVariable = "TELLMA_UPDATE_GOLDENS";

        private const string Prefix = "POST /{tenantId}/api/web/";

        [Fact]
        public async Task The_platform_and_fixture_stacks_project_their_routes_with_stable_fingerprints()
        {
            ServiceCollection services = [];
            _ = FixtureComposition.Compose(services);
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            Golden("fixture-routes.txt", Render(provider.GetRequiredService<IRouteContracts>()));
        }

        [Fact]
        public async Task A_stack_with_actions_a_companion_child_surfaces_and_a_routed_service_projects_every_route()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);

            Golden("gadget-routes.txt", Render(provider.GetRequiredService<IRouteContracts>()));
        }

        [Fact]
        public async Task A_routes_fingerprint_hashes_a_canonical_description_of_its_contracts()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            var routes = (RouteContracts)provider.GetRequiredService<IRouteContracts>();

            // The gadget stack's own routes, each with the description its fingerprint hashes.
            StringBuilder text = new();
            foreach (RouteContract route in routes.Routes.Where(static route => route.Template.StartsWith(Prefix + "gadgets/", StringComparison.Ordinal)))
            {
                string description = routes.DescriptionOf(route.Template)!;
                Assert.Equal(route.Fingerprint, ContractDescriber.Fingerprint(description));
                _ = text.Append(route.Template).Append('\n').Append(description).Append('\n');
            }

            Golden("gadget-route-descriptions.txt", text.ToString());
        }

        [Fact]
        public async Task A_fingerprint_is_twelve_base64url_characters_and_the_same_in_every_composition()
        {
            await using ServiceProvider first = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            await using ServiceProvider second = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);

            IReadOnlyList<RouteContract> routes = first.GetRequiredService<IRouteContracts>().Routes;
            Assert.All(routes, static route => Assert.Matches("^[A-Za-z0-9_-]{12}$", route.Fingerprint));
            Assert.Equal(
                routes.Select(static route => (route.Template, route.Fingerprint)),
                second.GetRequiredService<IRouteContracts>().Routes.Select(static route => (route.Template, route.Fingerprint)));
        }

        [Fact]
        public async Task Bumping_a_stacks_contract_revision_changes_exactly_the_fingerprints_of_its_routes_and_its_child_surfaces()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            IStackRegistry registry = provider.GetRequiredService<IStackRegistry>();
            IEntityMetadataProvider metadata = provider.GetRequiredService<IEntityMetadataProvider>();
            const string Gadget = "sm.SmGadget";

            Dictionary<string, string> before = Fingerprints(new RouteContracts(registry, metadata));
            Dictionary<string, string> after = Fingerprints(new RouteContracts(
                new RevisedRegistry(registry, stack => stack.Resource == Gadget ? stack with { ContractRevision = 1 } : stack),
                metadata));

            // The gadget's own routes and the routes of the part and part-note surfaces beneath it.
            string[] covered = [.. registry.Stacks.Where(static stack => stack.Resource == Gadget).Select(static stack => Prefix + stack.ResourceSegment + "/")
                .Concat(registry.ChildQueries.Where(static child => child.Resource.StartsWith("sm.SmGadgetPart", StringComparison.Ordinal)).Select(static child => Prefix + child.ResourceSegment + "/"))];
            Assert.Equal(3, covered.Length);
            Assert.Equal(before.Keys, after.Keys);
            Assert.All(before, route => Assert.Equal(
                covered.Any(prefix => route.Key.StartsWith(prefix, StringComparison.Ordinal)),
                route.Value != after[route.Key]));
        }

        [Fact]
        public async Task Bumping_an_actions_contract_revision_changes_exactly_that_actions_fingerprint()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            IStackRegistry registry = provider.GetRequiredService<IStackRegistry>();
            IEntityMetadataProvider metadata = provider.GetRequiredService<IEntityMetadataProvider>();

            Dictionary<string, string> before = Fingerprints(new RouteContracts(registry, metadata));
            Dictionary<string, string> after = Fingerprints(new RouteContracts(
                new RevisedRegistry(registry, static stack => stack with { Actions = [.. stack.Actions.Select(static action => action.Name == "summary" ? Revised((ApiActionDescriptor)action) : action)] }),
                metadata));

            string changed = Assert.Single(before, route => route.Value != after[route.Key]).Key;
            Assert.Equal(Prefix + "gadgets/summary", changed);
        }

        [Fact]
        public async Task The_standard_operations_carry_their_classification_and_the_actions_their_declared_facts()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            IRouteContracts routes = provider.GetRequiredService<IRouteContracts>();

            // --- A read and a write the pipeline serves itself ---
            RouteContract query = routes.For(Prefix + "gadgets/query")!;
            Assert.Equal(("query", "sm.SmGadget", null, typeof(QueryRequest), typeof(RowPage)), (query.Operation, query.Resource, query.Action, query.RequestType, query.ResponseType));
            Assert.Equal((false, false, false, true), (query.IsMutation, query.AllowWhenReadOnly, query.Long, query.Idempotent));
            Assert.Equal("sm.SmGadget:Read", query.Securable!.ToString());

            RouteContract save = routes.For(Prefix + "gadgets/save")!;
            Assert.Equal((true, false, false, false), (save.IsMutation, save.AllowWhenReadOnly, save.Long, save.Idempotent));
            Assert.Equal("sm.SmGadget:Save", save.Securable!.ToString());

            // --- An entity action with its own result, a member-only API action, a routed service's action ---
            RouteContract reprice = routes.For(Prefix + "gadgets/prices/reprice")!;
            Assert.Same(reprice.Action, provider.GetRequiredService<IStackRegistry>().Find("sm.SmGadget")!.Actions.Single(static action => action.Name == "prices/reprice"));
            Assert.Equal((true, true, false), (reprice.IsMutation, reprice.Long, reprice.Idempotent));
            Assert.Equal(typeof(int), reprice.ResponseType);

            RouteContract count = routes.For(Prefix + "gadgets/count")!;
            Assert.Null(count.Securable);
            Assert.Null(count.RequestType);

            RouteContract purge = routes.For(Prefix + "sm-inbox/purge")!;
            Assert.Null(purge.Resource);
            Assert.Equal("sm.Inbox:Purge", purge.Securable!.ToString());

            // --- A route the projection does not map ---
            Assert.Null(routes.For(Prefix + "gadgets/no-such-action"));
            Assert.Null(routes.For("POST /{tenantId}/api/web/GADGETS/query"));
        }

        [Fact]
        public async Task The_contracts_chunk_maps_every_route_to_its_fingerprint_and_lists_the_long_and_idempotent_routes()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            IReadOnlyList<RouteContract> routes = provider.GetRequiredService<IRouteContracts>().Routes;
            ClientAssetContext context = new(provider);

            provider.GetServices<IClientAssetContributor>().OfType<ContractsClientAssetContributor>().Single().Contribute(context);

            Assert.Equal(["fingerprints", "long", "idempotent"], context.Sections.Select(static section => section.Name));
            Assert.All(context.Sections, static section => Assert.Equal(ClientMetadataChunk.Contracts, section.Chunk));

            using var fingerprints = JsonDocument.Parse(context.Sections[0].Json);
            Assert.Equal(
                routes.Select(static route => (route.Template, route.Fingerprint)),
                fingerprints.RootElement.EnumerateObject().Select(static member => (member.Name, member.Value.GetString()!)));

            using var longRoutes = JsonDocument.Parse(context.Sections[1].Json);
            Assert.Equal(
                routes.Where(static route => route.Long).Select(static route => route.Template),
                longRoutes.RootElement.EnumerateArray().Select(static route => route.GetString()!));
            Assert.Contains(Prefix + "gadgets/prices/reprice", longRoutes.RootElement.EnumerateArray().Select(static route => route.GetString()!));
            Assert.Contains(Prefix + "gadgets/export-for-import", longRoutes.RootElement.EnumerateArray().Select(static route => route.GetString()!));
            Assert.DoesNotContain(Prefix + "gadgets/query", longRoutes.RootElement.EnumerateArray().Select(static route => route.GetString()!));

            using var idempotent = JsonDocument.Parse(context.Sections[2].Json);
            Assert.Equal(
                routes.Where(static route => route.Idempotent).Select(static route => route.Template),
                idempotent.RootElement.EnumerateArray().Select(static route => route.GetString()!));
            Assert.Contains(Prefix + "gadgets/query", idempotent.RootElement.EnumerateArray().Select(static route => route.GetString()!));
            Assert.Contains(Prefix + "gadgets/activate", idempotent.RootElement.EnumerateArray().Select(static route => route.GetString()!));
            Assert.DoesNotContain(Prefix + "gadgets/save", idempotent.RootElement.EnumerateArray().Select(static route => route.GetString()!));
        }

        [Fact]
        public async Task The_queryex_discovery_is_a_member_only_idempotent_read_in_the_contracts_chunks_idempotent_list()
        {
            ServiceCollection services = [];
            _ = FixtureComposition.Compose(services);
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            ClientAssetContext context = new(provider);

            RouteContract discover = provider.GetRequiredService<IRouteContracts>().For(Prefix + "queryex/discover")!;
            provider.GetServices<IClientAssetContributor>().OfType<ContractsClientAssetContributor>().Single().Contribute(context);

            // A read every editor retries, open to every member, and kept from the agents.
            Assert.Equal((typeof(QueryexDiscoverRequest), typeof(QueryexDiscoverResult)), (discover.RequestType, discover.ResponseType));
            Assert.Equal((false, false, true), (discover.IsMutation, discover.Long, discover.Idempotent));
            Assert.Null(discover.Securable);
            Assert.Equal(McpExposure.Hidden, discover.Action!.Mcp);
            using var idempotent = JsonDocument.Parse(context.Sections.Single(static section => section.Name == "idempotent").Json);
            Assert.Contains(Prefix + "queryex/discover", idempotent.RootElement.EnumerateArray().Select(static route => route.GetString()!));
        }

        /// <summary>Renders every route as one line: template, operation, resource, serving action, body, result, securable, facts and fingerprint.</summary>
        /// <param name="routes">The route model.</param>
        /// <returns>The text.</returns>
        private static string Render(IRouteContracts routes)
        {
            StringBuilder text = new();
            foreach (RouteContract route in routes.Routes)
            {
                List<string> facts = [];
                AddIf(facts, route.IsMutation, "mutation");
                AddIf(facts, route.AllowWhenReadOnly, "read-only");
                AddIf(facts, route.Long, "long");
                AddIf(facts, route.Idempotent, "idempotent");

                _ = text.Append(route.Template)
                    .Append(" | ").Append(route.Operation)
                    .Append(" | ").Append(route.Resource ?? "-")
                    .Append(" | ").Append(route.Action switch { EntityActionDescriptor => "entity", ApiActionDescriptor => "api", _ => "-" })
                    .Append(" | ").Append(route.RequestType is Type request ? TypeDisplay.Short(request) : "-")
                    .Append(" | ").Append(TypeDisplay.Short(route.ResponseType))
                    .Append(" | ").Append(route.Securable?.ToString() ?? "member-only")
                    .Append(" | ").Append(facts.Count == 0 ? "-" : string.Join(", ", facts))
                    .Append(" | ").Append(route.Fingerprint)
                    .Append('\n');
            }

            return text.ToString();

            static void AddIf(List<string> facts, bool condition, string fact)
            {
                if (condition)
                {
                    facts.Add(fact);
                }
            }
        }

        /// <summary>The fingerprint of every route, by template.</summary>
        /// <param name="routes">The route model.</param>
        /// <returns>The fingerprints.</returns>
        private static Dictionary<string, string> Fingerprints(RouteContracts routes)
        {
            return routes.Routes.ToDictionary(static route => route.Template, static route => route.Fingerprint, StringComparer.Ordinal);
        }

        /// <summary>An API action with its contract revision bumped.</summary>
        /// <param name="action">The action.</param>
        /// <returns>The revised action.</returns>
        private static ApiActionDescriptor Revised(ApiActionDescriptor action)
        {
            return new ApiActionDescriptor(
                action.Name, action.Description, action.Mutation, action.AllowWhenReadOnly, action.Idempotent, action.Long, action.Destructive,
                action.Mcp, action.ContractRevision + 1, action.ResultType, action.Securable, action.RequestType, action.HandlerType);
        }

        /// <summary>Compares a rendering with its golden, or rewrites the golden when asked to.</summary>
        /// <param name="name">The golden's file name.</param>
        /// <param name="rendered">The rendering.</param>
        private static void Golden(string name, string rendered)
        {
            string path = Path.Combine(GoldenDirectory(), name);
            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            {
                File.WriteAllText(path, rendered, new UTF8Encoding(false));
                Assert.Fail("The route golden was rewritten; review the diff and run again without " + UpdateVariable + " set.");
            }

            Assert.True(File.Exists(path), "No route golden " + name + ". Set " + UpdateVariable + " to record one.");
            Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), rendered);
        }

        /// <summary>The directory of the goldens, beside this source file.</summary>
        /// <param name="here">This file's path, supplied by the compiler.</param>
        /// <returns>The directory.</returns>
        private static string GoldenDirectory([CallerFilePath] string here = "")
        {
            return Path.Combine(Path.GetDirectoryName(here)!, "Goldens");
        }

        /// <summary>A stack registry whose stacks are revised by a function, everything else passed through.</summary>
        /// <param name="inner">The registry revised.</param>
        /// <param name="revise">Revises each stack.</param>
        private sealed class RevisedRegistry(IStackRegistry inner, Func<StackDescriptor, StackDescriptor> revise) : IStackRegistry
        {
            private readonly Dictionary<string, StackDescriptor> _stacks = inner.Stacks.ToDictionary(static stack => stack.Resource, revise, StringComparer.Ordinal);

            /// <inheritdoc />
            public IReadOnlyList<StackDescriptor> Stacks => [.. inner.Stacks.Select(stack => _stacks[stack.Resource])];

            /// <inheritdoc />
            public IReadOnlyList<ChildQueryDescriptor> ChildQueries => inner.ChildQueries;

            /// <inheritdoc />
            public IReadOnlyList<ApiServiceDescriptor> Services => inner.Services;

            /// <inheritdoc />
            public IReadOnlyList<EnlistmentDescriptor> Enlistments => inner.Enlistments;

            /// <inheritdoc />
            public StackDescriptor? Find(string resource)
            {
                return _stacks.GetValueOrDefault(resource);
            }

            /// <inheritdoc />
            public ChildQueryDescriptor? FindChildQuery(string resource)
            {
                return inner.FindChildQuery(resource);
            }

            /// <inheritdoc />
            public StackDescriptor? OwnerOf(TableName table)
            {
                return inner.OwnerOf(table) is StackDescriptor stack ? _stacks[stack.Resource] : null;
            }
        }
    }
}
