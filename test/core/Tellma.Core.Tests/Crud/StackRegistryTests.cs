// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Composition;
using Tellma.Core.Crud;
using Tellma.Core.Tests.Infrastructure;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Crud
{
    /// <summary>
    ///     The stack registry: lookups by resource and table, child query surfaces at every depth,
    ///     the declarations read with no tenant scope, the registrations of each stack's classes
    ///     closed over its leaf, and the action descriptors it owns.
    /// </summary>
    public class StackRegistryTests
    {
        [Fact]
        public async Task Stacks_and_child_surfaces_are_found_by_resource_and_every_table_by_its_owner()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            IStackRegistry registry = provider.GetRequiredService<IStackRegistry>();

            StackDescriptor gadgets = registry.Find(StackModel.Gadgets)!;
            Assert.Equal("gadgets", gadgets.ResourceSegment);
            Assert.Null(registry.Find(StackModel.Parts));
            Assert.Null(registry.Find("sm.smgadget"));

            // --- The owners chain: a grandchild's owner is the child surface above it ---
            ChildQueryDescriptor parts = registry.FindChildQuery(StackModel.Parts)!;
            ChildQueryDescriptor notes = registry.FindChildQuery(StackModel.Notes)!;
            Assert.Equal((StackModel.Gadgets, "Gadget", "Parts"), (parts.OwnerResource, parts.OwnerNavigation, parts.Collection));
            Assert.Equal((StackModel.Parts, "GadgetPart", "Notes"), (notes.OwnerResource, notes.OwnerNavigation, notes.Collection));
            Assert.Equal("part-notes", notes.ResourceSegment);

            // Until the Excel feature lands, an exportable surface carries no export actions.
            Assert.True(parts.Exportable);
            Assert.Empty(parts.Actions);

            // --- Every table of an aggregate belongs to its stack, compared as the database does ---
            Assert.Same(gadgets, registry.OwnerOf(new TableName("sm", "Gadgets")));
            Assert.Same(gadgets, registry.OwnerOf(new TableName("sm", "GadgetParts")));
            Assert.Same(gadgets, registry.OwnerOf(new TableName("SM", "gadgetpartnotes")));
            Assert.Equal(StackModel.Gizmos, registry.OwnerOf(new TableName("sm", "Gizmos"))!.Resource);
            Assert.Null(registry.OwnerOf(new TableName("sm", "Nothing")));
        }

        [Fact]
        public async Task A_child_type_that_opts_out_has_no_surface_and_neither_do_its_children()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(StackModel.Feature<GadgetService<SmGadget>>(static contribution => contribution.Entity<SmGizmo>()))
                .UseEntity<SmGadget, SmQuietGadget>())
                .BuildServiceProvider(validateScopes: true);
            IStackRegistry registry = provider.GetRequiredService<IStackRegistry>();

            Assert.Equal(
                [StackModel.Parts, StackModel.Notes],
                registry.ChildQueries.Select(static child => child.Resource).Where(static resource => resource.StartsWith("sm.", StringComparison.Ordinal)));
            Assert.Null(registry.FindChildQuery("sm.SmQuietPart"));
            Assert.Null(registry.FindChildQuery("sm.SmQuietNote"));

            // The tables still belong to the stack.
            Assert.Equal(StackModel.Gadgets, registry.OwnerOf(new TableName("sm", "QuietNotes"))!.Resource);
        }

        [Fact]
        public async Task A_stack_without_query_has_no_child_surfaces()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.D>("sm", contribute: static contribution => contribution
                    .Add(new StackContributionItem(typeof(SmGadget), typeof(GadgetService<SmGadget>), StackOperations.Details | StackOperations.Save))
                    .Entity<SmGizmo>())))
                .BuildServiceProvider(validateScopes: true);
            IStackRegistry registry = provider.GetRequiredService<IStackRegistry>();

            Assert.Equal(StackOperations.Details | StackOperations.Save, registry.Find(StackModel.Gadgets)!.Operations);
            Assert.Null(registry.FindChildQuery(StackModel.Parts));
        }

        [Fact]
        public async Task Named_filters_and_extras_are_read_once_with_no_tenant_scope()
        {
            GadgetFilterCount count = new();
            await using ServiceProvider provider = StackScenarios.Gadgets().AddSingleton(count).BuildServiceProvider(validateScopes: true);

            // The root provider builds the registry; a scope is never opened.
            IStackRegistry registry = provider.GetRequiredService<IStackRegistry>();
            StackDescriptor gadgets = registry.Find(StackModel.Gadgets)!;
            _ = registry.Stacks;
            _ = provider.GetRequiredService<IStackRegistry>().Find(StackModel.Gadgets);

            Assert.Equal(1, count.Created);
            Assert.Equal(["active", "on-gizmo", "with-parts"], gadgets.NamedFilters.Keys);
            Assert.Equal(["history", "lineage", "permissions"], gadgets.Extras);
        }

        [Fact]
        public async Task The_actions_carry_their_flags_and_the_registry_owns_each_descriptor()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(StackModel.Feature<ExtendedGadgetService>(static contribution => contribution.Entity<SmGizmo>())))
                .BuildServiceProvider(validateScopes: true);
            StackRegistry registry = provider.GetRequiredService<StackRegistry>();
            StackDescriptor gadgets = registry.Find(StackModel.Gadgets)!;

            // --- Built-ins first, then the base class's actions, then the subclass's ---
            Assert.Equal(["activate", "deactivate", "archive", "prices/reprice", "summary", "take-over"], gadgets.Actions.Select(static action => action.Name));
            EntityActionDescriptor archive = Assert.IsType<EntityActionDescriptor>(gadgets.Actions[2]);
            EntityActionDescriptor reprice = Assert.IsType<EntityActionDescriptor>(gadgets.Actions[3]);
            EntityActionDescriptor takeOver = Assert.IsType<EntityActionDescriptor>(gadgets.Actions[5]);
            Assert.Equal((true, false, "Archive"), (archive.Idempotent, archive.Long, archive.Action));
            Assert.Equal((false, true, "Reprice", false, typeof(int)), (reprice.Idempotent, reprice.Long, reprice.Action, reprice.SupportsFilter, reprice.ResultType));
            Assert.Equal((true, true, McpExposure.Hidden, typeof(string)), (takeOver.SingleTarget, takeOver.Destructive, takeOver.Mcp, takeOver.ResultType));

            // --- The method behind an action, found by the instance the registry owns only ---
            ActionMethod method = registry.MethodOf(archive)!;
            Assert.Equal((typeof(ExtendedGadgetService), nameof(ExtendedGadgetService.ArchiveAsync), typeof(ExtendedGadgetService)), (method.Handler, method.Method.Name, method.Method.DeclaringType));
            Assert.Null(registry.MethodOf(archive with { }));
            Assert.Null(registry.MethodOf(gadgets.Actions[0]));
        }

        [Fact]
        public async Task A_substituted_leaf_closes_the_service_the_companion_and_the_components_over_itself()
        {
            IServiceCollection services = StackScenarios.Gadgets(
                static contribution => contribution.Validator<SmGadget>(typeof(MyGadgetRules<>)),
                static tellma => tellma.UseEntity<SmGadget, SmMyGadget>());
            await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            StackDescriptor gadgets = provider.GetRequiredService<IStackRegistry>().Find(StackModel.Gadgets)!;

            // --- The descriptor: the pack's resource, the leaf's classes ---
            Assert.Equal((typeof(SmMyGadget), typeof(GadgetService<SmMyGadget>)), (gadgets.EntityType, gadgets.ServiceType));
            Assert.Equal(typeof(GadgetOperations<SmMyGadget>), Assert.IsType<ApiActionDescriptor>(gadgets.Actions.Single(static action => action.Name == "count")).HandlerType);
            Assert.Contains(gadgets.Properties, static property => property.Name == nameof(SmMyGadget.Colour));

            // --- The registrations: the service through its factory, every class closed over the leaf ---
            Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(GadgetService<SmMyGadget>) && descriptor.ImplementationFactory is not null);
            Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(EntityService<SmMyGadget, int>));
            Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(GadgetOperations<SmMyGadget>));
            Assert.Equal(
                [typeof(GadgetRules<SmMyGadget>), typeof(MyGadgetRules<SmMyGadget>)],
                services.Where(static descriptor => descriptor.ServiceType == typeof(IEntityValidator<SmMyGadget>)).Select(static descriptor => descriptor.ImplementationType));
            Assert.Equal(typeof(GadgetDetails<SmMyGadget>), Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IDetailsContributor<SmMyGadget>)).ImplementationType);
            Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType.IsGenericType && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(INamedFilterContributor<>));
            Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(IEntityValidator<SmGadget>));

            // The components resolve per scope, closed over the leaf.
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            Assert.Equal(2, scope.ServiceProvider.GetServices<IEntityValidator<SmMyGadget>>().Count());
        }

        [Fact]
        public async Task A_dependent_feature_replaces_the_stack_service_and_the_stack_keeps_its_feature()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(StackModel.Feature<GadgetService<SmGadget>>(static contribution => contribution.Entity<SmGizmo>()))
                .AddFeature(new TestFeature<Tag.A>(
                    "acme",
                    declare: static declaration => declaration.Requires("sm"),
                    contribute: static contribution => contribution.Entity<SmGadget, ExtendedGadgetService>())))
                .BuildServiceProvider(validateScopes: true);
            StackRegistry registry = provider.GetRequiredService<StackRegistry>();

            StackDescriptor gadgets = registry.Find(StackModel.Gadgets)!;
            Assert.Empty(registry.Problems);
            Assert.Equal((typeof(ExtendedGadgetService), "sm"), (gadgets.ServiceType, gadgets.Feature));
        }

        [Fact]
        public async Task The_routes_answer_the_link_builder_and_the_endpoint_audit()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            ITellmaLinks links = provider.GetRequiredService<ITellmaLinks>();
            IStackRouteSource routes = provider.GetRequiredService<IStackRouteSource>();

            Assert.Same(provider.GetRequiredService<StackRegistry>(), routes);
            Assert.Contains(new StackRoute("gadgets", StackOperations.All), routes.Routes);
            Assert.Equal("https://acme.app.tellma.com/42/gadgets/7", links.Target(42, StackModel.Gadgets, 7));
        }

        [Fact]
        public async Task The_routed_services_and_the_declared_enlistments_are_described()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.D>("sm", contribute: static contribution => contribution
                    .Add(new StackContributionItem(typeof(SmGadget), typeof(SelfEnlistingGadgetService), StackOperations.All | StackOperations.Enlist))
                    .Entity<SmGizmo>()
                    .Entity<SmSprocket>()
                    .PersistEffect<SmGizmo, GizmoEffect>()
                    .ApiService<InboxService>())))
                .BuildServiceProvider(validateScopes: true);
            StackRegistry registry = provider.GetRequiredService<StackRegistry>();

            // The platform's own enlistments, the users' self-service one among them, are not this test's.
            Assert.Empty(registry.Problems);
            Assert.Equal(
                [
                    new EnlistmentDescriptor(typeof(SelfEnlistingGadgetService), TypeDisplay.Qualified(typeof(SelfEnlistingGadgetService)), StackModel.Gadgets),
                    new EnlistmentDescriptor(typeof(SelfEnlistingGadgetService), TypeDisplay.Qualified(typeof(SelfEnlistingGadgetService)), StackModel.Gizmos),
                    new EnlistmentDescriptor(typeof(GizmoEffect), TypeDisplay.Qualified(typeof(GizmoEffect)), StackModel.Sprockets),
                ],
                registry.Enlistments.Where(static enlistment => !enlistment.TargetResource.StartsWith("core.", StringComparison.Ordinal)));
            // The platform's own routed services, the settings among them, are not this test's.
            ApiServiceDescriptor inbox = Assert.Single(registry.Services, static service => service.Feature != "core");
            Assert.Equal(("inbox", typeof(InboxService), "sm"), (inbox.Route, inbox.ServiceType, inbox.Feature));
            Assert.Equal([null, new SecurableRef("sm.Inbox", "Purge")], inbox.Actions.Select(static action => action.Securable));
        }

        [Fact]
        public async Task The_stack_securables_follow_the_descriptors_and_pass_the_securables_audit()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync();
            Assert.Empty(problems);

            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            ISecurableRegistry securables = provider.GetRequiredService<ISecurableRegistry>();

            Assert.Equal(
                [
                    new SecurableDescriptor(StackModel.Gadgets, "Read", StackModel.Gadgets, false, "sm", null),
                    new SecurableDescriptor(StackModel.Gadgets, "Save", StackModel.Gadgets, false, "sm", null),
                    new SecurableDescriptor(StackModel.Gadgets, "Delete", StackModel.Gadgets, false, "sm", null),
                    new SecurableDescriptor(StackModel.Gadgets, "Activate", StackModel.Gadgets, false, "sm", null),
                    new SecurableDescriptor(StackModel.Gadgets, "Archive", StackModel.Gadgets, false, "sm", null),
                    new SecurableDescriptor(StackModel.Gadgets, "Export", StackModel.Gadgets, false, "sm", null),
                    new SecurableDescriptor(StackModel.Gadgets, "Reprice", null, false, "sm", null),
                    new SecurableDescriptor(StackModel.Parts, "Read", StackModel.Parts, false, "sm", new SecurableOwner(StackModel.Gadgets, "Gadget")),
                    new SecurableDescriptor(StackModel.Notes, "Read", StackModel.Notes, false, "sm", new SecurableOwner(StackModel.Parts, "GadgetPart")),
                ],
                securables.ForResource(StackModel.Gadgets).Concat(securables.ForResource(StackModel.Parts)).Concat(securables.ForResource(StackModel.Notes)));
            Assert.Equal(new SecurableDescriptor("sm.Inbox", "Purge", null, false, "sm", null), securables.Find("sm.Inbox", "Purge"));
        }

        [Fact]
        public async Task A_cacheable_stack_reads_without_a_filter_and_a_read_only_stack_registers_its_read_and_its_actions_alone()
        {
            ServiceCollection services = [];
            _ = FixtureComposition.Compose(services);
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            ISecurableRegistry securables = provider.GetRequiredService<ISecurableRegistry>();

            // --- One cached list serves every reader, so its read takes no filter root ---
            Assert.Equal(
                [
                    new SecurableDescriptor("fixture.Lookup", "Read", null, false, "fixture", null),
                    new SecurableDescriptor("fixture.Lookup", "Save", "fixture.Lookup", false, "fixture", null),
                    new SecurableDescriptor("fixture.Lookup", "Delete", "fixture.Lookup", false, "fixture", null),
                    new SecurableDescriptor("fixture.Lookup", "Activate", "fixture.Lookup", false, "fixture", null),
                ],
                securables.ForResource("fixture.Lookup"));

            // --- Query and details only: the read and nothing else ---
            Assert.Equal([new SecurableDescriptor("core.Notification", "Read", "core.Notification", false, "core", null)], securables.ForResource("core.Notification"));

            // --- A read-only stack with declared actions: the read, then the platform's actions in their order, then its own ---
            Assert.Equal(
                ["Read", "Retry", "Cancel", "Resume", "Confirm"],
                securables.ForResource("core.Job").Select(static securable => securable.Action));
        }
    }
}
