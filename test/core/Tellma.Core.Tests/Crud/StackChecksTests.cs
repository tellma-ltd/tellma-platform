// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Access;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Crud.Checks;
using Tellma.Core.Localization;
using Tellma.Core.Tests.Access;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud
{
    /// <summary>
    ///     Every startup check over the stacks, each with a fixture that fails it and the problems
    ///     naming the entity, the service or the method; the sound scenarios pass every check.
    /// </summary>
    public class StackChecksTests
    {
        [Fact]
        public async Task The_gadget_scenario_and_its_substituted_leaf_pass_every_check()
        {
            Assert.Empty(await StackScenarios.CheckAsync());
            Assert.Empty(await StackScenarios.CheckAsync(compose: static tellma => tellma.UseEntity<SmGadget, SmMyGadget>()));
        }

        // --- core.stacks ---

        [Fact]
        public async Task Two_unrelated_features_stacking_one_entity_fail()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync<GadgetService<SmGadget>>(
                compose: static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: static contribution => contribution.Entity<SmGadget, ExtendedGadgetService>())));

            Assert.Equal(
                [$"The features 'sm' and 'acme' both register a stack over {typeof(SmGadget).FullName}, and neither requires the other; an entity has one stack."],
                Problems(problems, CoreCheckNames.Stacks));
        }

        [Fact]
        public async Task A_service_class_no_stack_runs_fails()
        {
            // --- Stacked with the default service, while service classes are written for it ---
            IReadOnlyList<CompositionProblem> defaulted = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.D>("sm", contribute: static contribution => contribution.Entity<SmGadget>().Entity<SmGizmo>())));
            List<string> problems = Problems(defaulted, CoreCheckNames.Stacks);
            Assert.Contains(
                $"The entity service {typeof(ExtendedGadgetService).FullName} serves {typeof(SmGadget).FullName}, whose stack the feature 'sm' registers with the platform's default service, so no stack runs it.",
                problems);
            Assert.Contains(problems, static problem => problem.StartsWith("The entity service Tellma.Core.Tests.Crud.GadgetService<TGadget> serves", StringComparison.Ordinal));

            // --- Registered with the container instead of a stack ---
            IReadOnlyList<CompositionProblem> registered = await StackScenarios.CheckAsync(static contribution => contribution.Scoped<ExtendedGadgetService, ExtendedGadgetService>());
            Assert.Equal(
                [$"The feature 'sm' registers the entity service {typeof(ExtendedGadgetService).FullName} as a service, but no stack runs it."],
                Problems(registered, CoreCheckNames.Stacks));
        }

        [Fact]
        public async Task An_operation_set_the_pipeline_cannot_serve_fails()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.D>("sm", contribute: static contribution => contribution
                    .Add(new StackContributionItem(typeof(SmGadget), typeof(GadgetService<SmGadget>), StackOperations.Query | StackOperations.Save))
                    .Add(new StackContributionItem(typeof(SmGizmo), null, StackOperations.Details | StackOperations.Save | StackOperations.Import))
                    .Add(new StackContributionItem(typeof(SmSprocket), null, StackOperations.Details | StackOperations.Export | StackOperations.DeleteByQuery)))));

            Assert.Equal(
                [
                    "The stack of sm.SmGadget declares the operations Query, Save, which keeps Save but omits Details; a save reads its rows back through the details plan.",
                    "The stack of sm.SmGizmo declares the operations Details, Save, Import, which keeps Import without all of Query, Details and Save; an import reads the rows it matches and saves them.",
                    "The stack of sm.SmSprocket declares the operations Details, Export, DeleteByQuery, which keeps Export but omits Query; an export runs the stack's query.",
                    "The stack of sm.SmSprocket declares the operations Details, Export, DeleteByQuery, which keeps DeleteByQuery but omits Delete; a delete by query is a delete, under its securable action.",
                ],
                Problems(problems, CoreCheckNames.Stacks));
        }

        [Fact]
        public async Task A_delete_by_query_on_a_stack_whose_service_or_a_validator_judges_deletes_fails_and_one_that_omits_it_passes()
        {
            const string Refusal = "The stack of sm.SmLatch declares the operations All, which keeps DeleteByQuery while its service or a validator judges its deletes; a delete by query names no ids those rules could see, so it would delete past them.";
            const StackOperations WithoutByQuery = StackOperations.Read | StackOperations.Save | StackOperations.Delete;

            // --- The service judges deletes, then a validator does: each is refused ---
            IReadOnlyList<CompositionProblem> byService = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.D>("sm", contribute: static contribution => contribution.Entity<SmLatch, JudgingLatchService>())));
            IReadOnlyList<CompositionProblem> byValidator = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.D>("sm", contribute: static contribution => contribution.Entity<SmLatch, PlainLatchService>().Validator<SmLatch, LatchDeleteRules>())));
            Assert.Equal([Refusal], Problems(byService, CoreCheckNames.Stacks));
            Assert.Equal([Refusal], Problems(byValidator, CoreCheckNames.Stacks));

            // --- The same stacks without the operation pass, and so does a validator that judges saves alone ---
            IReadOnlyList<CompositionProblem> omitted = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.D>("sm", contribute: static contribution => contribution
                    .Add(new StackContributionItem(typeof(SmLatch), typeof(JudgingLatchService), WithoutByQuery)))));
            IReadOnlyList<CompositionProblem> omittedByValidator = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.D>("sm", contribute: static contribution => contribution
                    .Add(new StackContributionItem(typeof(SmLatch), typeof(PlainLatchService), WithoutByQuery))
                    .Validator<SmLatch, LatchDeleteRules>())));
            IReadOnlyList<CompositionProblem> savesOnly = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.D>("sm", contribute: static contribution => contribution.Entity<SmLatch, PlainLatchService>().Validator<SmLatch, LatchSaveRules>())));
            Assert.Empty(Problems(omitted, CoreCheckNames.Stacks));
            Assert.Empty(Problems(omittedByValidator, CoreCheckNames.Stacks));
            Assert.Empty(Problems(savesOnly, CoreCheckNames.Stacks));
        }

        [Fact]
        public async Task A_delete_by_query_over_a_security_table_fails_and_the_platform_s_own_stacks_serve_none()
        {
            // --- A distribution's role leaf that takes the operation back ---
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma.UseEntity<Role, SmEveryRole>());

            Assert.Equal(
                ["The stack of core.Role declares the operations All, which keeps DeleteByQuery over a security table; its rows are deleted by their ids, so each deletion meets the rules that keep the tenant's administrators."],
                Problems(problems, CoreCheckNames.Stacks));

            // --- The users' and the roles' own declarations leave it out ---
            Assert.False(typeof(User).GetCustomAttribute<StackAttribute>(inherit: true)!.Operations.HasFlag(StackOperations.DeleteByQuery));
            Assert.False(typeof(Role).GetCustomAttribute<StackAttribute>(inherit: true)!.Operations.HasFlag(StackOperations.DeleteByQuery));
        }

        [Fact]
        public async Task A_ceiling_of_zero_fails_naming_the_stack()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync(static contribution => contribution.Entity<SmCeiling>());

            Assert.Equal(
                ["The stack of sm.SmCeiling declares [Stack(MaxTake = 0)]; every ceiling is positive, since a ceiling of zero would refuse every request."],
                Problems(problems, CoreCheckNames.Stacks));
        }

        [Fact]
        public async Task A_malformed_or_repeated_segment_fails()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync(static contribution => contribution.Entity<SmClash>().Entity<SmOddity>());

            Assert.Equal(
                [
                    "The stack of sm.SmClash takes the resource segment 'gadgets', which the stack of sm.SmGadget already takes.",
                    "The stack of sm.SmOddity takes the resource segment 'Odd_Segment', which is not kebab-case.",
                ],
                Problems(problems, CoreCheckNames.Stacks));
        }

        [Fact]
        public async Task A_stack_over_a_child_or_with_a_service_of_another_entity_fails()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync(static contribution => contribution
                .Entity<SmGadgetPart>()
                .Add(new StackContributionItem(typeof(SmClash), typeof(GadgetService<SmGadget>), null)));

            Assert.Equal(
                [
                    $"The stack of {typeof(SmGadgetPart).FullName} is over a child entity; a child is saved with its aggregate and served by its owner's stack and its child query surface.",
                    $"The service Tellma.Core.Tests.Crud.GadgetService<{typeof(SmGadget).FullName}> of the stack of {typeof(SmClash).FullName} does not derive from EntityService<SmClash, int>.",
                ],
                Problems(problems, CoreCheckNames.Stacks));
        }

        // --- core.stack-actions ---

        [Fact]
        public async Task Every_misdeclared_action_of_a_service_fails_naming_its_method()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync<MisdeclaredGadgetService>(static contribution => contribution.Entity<SmGizmo>());
            string service = typeof(MisdeclaredGadgetService).FullName!;

            Assert.Equal(
                [
                    $"The action of {service}.BadNameAsync is named 'Bad Name', which is not one or more kebab-case segments joined by '/'.",
                    $"The action of {service}.SaveAgainAsync is named 'save', a standard segment the stack projects.",
                    $"The action of {service}.SecondAsync is named 'twice', which {service}.FirstAsync already takes; an action's name is unique per stack.",
                    $"The entity action 'no-context' of {service}.NoContextAsync takes (int); an entity action takes ActionContext<SmGadget, int> and, optionally, its arguments record.",
                    $"The entity action 'class-arguments' of {service}.ClassArgumentsAsync takes the arguments {typeof(NotARecord).FullName}, which is not a concrete record.",
                    $"The entity action 'single' of {service}.SingleAsync declares SingleTarget but returns no result; a single-target action answers its own result.",
                    $"The API action 'two-bodies' of {service}.TwoBodiesAsync takes (SummaryRequest, SummaryRequest); an API action takes at most one body, then optionally a cancellation token.",
                    $"The API action 'elsewhere' of {service}.ElsewhereAsync names the resource 'sm.Elsewhere'; an action of a stack's service is secured by the stack's resource, sm.SmGadget.",
                    $"The method {service}.BothAsync declares both [EntityAction] and [ApiAction]; a method is one action.",
                    "The argument DanglingArguments.TargetId of the action 'dangling' of sm.SmGadget references 'sm.Nothing', which no stack with Query owns.",
                ],
                Problems(problems, CoreCheckNames.StackActions));
        }

        [Fact]
        public async Task An_arguments_record_the_json_contract_refuses_fails()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync<CollidingGadgetService>(static contribution => contribution.Entity<SmGizmo>());

            string problem = Assert.Single(Problems(problems, CoreCheckNames.StackActions));
            Assert.StartsWith(
                $"The entity action 'colliding' of {typeof(CollidingGadgetService).FullName}.CollidingAsync takes the arguments {typeof(CollidingArguments).FullName}, which the platform cannot read from JSON: ",
                problem,
                StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_export_action_passes_on_a_stack_without_export_and_fails_on_one_with_it()
        {
            const StackOperations NoExport = StackOperations.Query | StackOperations.Details | StackOperations.Save | StackOperations.Delete;
            IReadOnlyList<CompositionProblem> passing = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.D>("sm", contribute: static contribution => contribution
                    .Add(new StackContributionItem(typeof(SmGadget), typeof(ExportingGadgetService), NoExport))
                    .Entity<SmGizmo>())));
            Assert.Empty(passing);

            IReadOnlyList<CompositionProblem> failing = await StackScenarios.CheckAsync<ExportingGadgetService>(static contribution => contribution.Entity<SmGizmo>());
            Assert.Equal(
                [$"The action of {typeof(ExportingGadgetService).FullName}.ExportAsync is named 'export', a standard segment the stack projects."],
                Problems(failing, CoreCheckNames.StackActions));
        }

        [Fact]
        public async Task A_misdeclared_or_unattached_companion_fails()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync(static contribution => contribution
                .EntityCompanion<SmGadget, MisdeclaredOperations<SmGadget>>()
                .EntityCompanion<SmGadget, IdleOperations<SmGadget>>()
                .EntityCompanion<SmCeiling, GadgetOperations<SmGadget>>());
            string companion = $"Tellma.Core.Tests.Crud.MisdeclaredOperations<{typeof(SmGadget).FullName}>";

            Assert.Equal(
                [
                    $"The feature 'sm' attaches the stack companion Tellma.Core.Tests.Crud.GadgetOperations<{typeof(SmGadget).FullName}> to {typeof(SmCeiling).FullName}, which no stack owns.",
                    $"The method {companion}.StrayAsync declares [EntityAction], but MisdeclaredOperations<SmGadget> is a stack companion; an entity action is declared on the stack's service.",
                    $"The action of {companion}.SummaryAsync is named 'summary', which Tellma.Core.Tests.Crud.GadgetService<{typeof(SmGadget).FullName}>.SummaryAsync already takes; an action's name is unique per stack.",
                    $"The action of {companion}.QueryAsync is named 'query', a standard segment the stack projects.",
                    $"The stack companion Tellma.Core.Tests.Crud.IdleOperations<{typeof(SmGadget).FullName}> of sm.SmGadget declares no [ApiAction] method, so it adds nothing to the stack.",
                ],
                Problems(problems, CoreCheckNames.StackActions));
        }

        [Fact]
        public async Task A_routed_service_without_a_route_or_a_securable_fails()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync(static contribution => contribution
                .ApiService<UnroutedService>()
                .ApiService<UnsecuredService>());

            Assert.Equal(
                [
                    $"The routed service {typeof(UnroutedService).FullName}, contributed by the feature 'sm', carries no [ApiRoute], so no route reaches it.",
                    $"The API action 'run' of {typeof(UnsecuredService).FullName}.RunAsync names neither a Resource nor MemberOnly; an action of a routed service has no stack whose resource secures it.",
                    $"The method {typeof(UnsecuredService).FullName}.StrayAsync declares [EntityAction], but UnsecuredService is a routed service; an entity action is declared on a stack's service.",
                ],
                Problems(problems, CoreCheckNames.StackActions));
        }

        // --- core.stack-components ---

        [Fact]
        public async Task A_component_the_leaf_cannot_close_fails()
        {
            // --- Not generic, on an entity a leaf substitutes ---
            IReadOnlyList<CompositionProblem> substituted = await StackScenarios.CheckAsync(
                static contribution => contribution.Validator<SmGadget, PlainGadgetRules>(),
                static tellma => tellma.UseEntity<SmGadget, SmMyGadget>());
            Assert.Equal(
                [$"The validator {typeof(PlainGadgetRules).FullName}, attached by the feature 'sm' to {typeof(SmGadget).FullName}, is not generic over one type parameter, the entity, so it cannot follow the leaf {typeof(SmMyGadget).FullName} that substitutes {typeof(SmGadget).FullName}."],
                Problems(substituted, CoreCheckNames.StackComponents));

            // --- Generic, with a constraint the leaf does not meet ---
            IReadOnlyList<CompositionProblem> constrained = await StackScenarios.CheckAsync(static contribution => contribution.Validator<SmGadget>(typeof(MyGadgetRules<>)));
            Assert.Equal(
                [$"The validator Tellma.Core.Tests.Crud.MyGadgetRules<TGadget>, attached by the feature 'sm' to {typeof(SmGadget).FullName}, has constraints the leaf {typeof(SmGadget).FullName} does not meet, so it cannot be closed over it."],
                Problems(constrained, CoreCheckNames.StackComponents));

            // --- The same non-generic component passes on its own entity's stack ---
            Assert.Empty(await StackScenarios.CheckAsync(static contribution => contribution.Validator<SmGadget, PlainGadgetRules>()));
        }

        [Fact]
        public async Task A_component_for_an_entity_no_stack_owns_fails()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync<GadgetService<SmGadget>>(static contribution => contribution
                .PersistEffect<SmCeiling>(typeof(GadgetRules<>))
                .Validator<SmCeiling>(typeof(GadgetRules<>))
                .NamedFilters<SmCeiling>(typeof(GadgetFilters<>)));

            Assert.Equal(
                [
                    $"The feature 'sm' attaches the persist effect Tellma.Core.Tests.Crud.GadgetRules<TGadget> to {typeof(SmCeiling).FullName}, which no stack owns, so its ContributeAsync never runs.",
                    $"The feature 'sm' attaches the validator Tellma.Core.Tests.Crud.GadgetRules<TGadget> to {typeof(SmCeiling).FullName}, which no stack owns, so it never runs.",
                    $"The feature 'sm' attaches the named filter contributor Tellma.Core.Tests.Crud.GadgetFilters<TGadget> to {typeof(SmCeiling).FullName}, which no stack owns, so it never runs.",
                ],
                Problems(problems, CoreCheckNames.StackComponents));
        }

        [Fact]
        public async Task A_non_generic_component_whose_entity_only_derived_stacks_own_fails()
        {
            // Every stack's leaf is a top-level entity, but none is the base itself.
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync<GadgetService<SmGadget>>(static contribution => contribution
                .Validator<TopLevelEntity, EveryEntityRules>());

            string problem = Assert.Single(Problems(problems, CoreCheckNames.StackComponents));
            Assert.StartsWith(
                $"The validator {typeof(EveryEntityRules).FullName}, attached by the feature 'sm' to {typeof(TopLevelEntity).FullName}, is not generic and no stack runs that entity, so it runs on none of the stacks of its derived entities (",
                problem,
                StringComparison.Ordinal);
            Assert.Contains(typeof(SmGadget).FullName!, problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_extra_declared_twice_or_the_platforms_own_fails()
        {
            IReadOnlyList<CompositionProblem> twin = await StackScenarios.CheckAsync(static contribution => contribution.DetailsContributor<SmGadget>(typeof(TwinDetails<>)));
            Assert.Equal(
                [$"The details contributor Tellma.Core.Tests.Crud.TwinDetails<{typeof(SmGadget).FullName}> declares the extra 'twin' 2 times."],
                Problems(twin, CoreCheckNames.StackComponents));

            IReadOnlyList<CompositionProblem> permissions = await StackScenarios.CheckAsync<PermissionsGadgetService>(static contribution => contribution.Entity<SmGizmo>());
            Assert.Equal(
                [$"The service {typeof(PermissionsGadgetService).FullName} declares the extra 'permissions', which the platform produces itself."],
                Problems(permissions, CoreCheckNames.StackComponents));

            IReadOnlyList<CompositionProblem> contributed = await StackScenarios.CheckAsync(static contribution => contribution.DetailsContributor<SmGadget>(typeof(PermissionsDetails<>)));
            Assert.Equal(
                [$"The details contributor Tellma.Core.Tests.Crud.PermissionsDetails<{typeof(SmGadget).FullName}> declares the extra 'permissions', which the platform produces itself."],
                Problems(contributed, CoreCheckNames.StackComponents));
        }

        [Fact]
        public async Task A_named_filter_outside_the_grammar_repeated_or_from_a_failing_contributor_fails()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync(static contribution => contribution
                .NamedFilters<SmGadget>(typeof(BrokenFilters<>))
                .NamedFilters<SmGadget>(typeof(ThrowingFilters<>)));
            string broken = $"Tellma.Core.Tests.Crud.BrokenFilters<{typeof(SmGadget).FullName}>";

            Assert.Equal(
                [
                    $"The named filter contributor {broken} of sm.SmGadget declares the filter 'Mine', a name outside ^[a-z][a-z0-9-]*$.",
                    $"The named filter 'active' of sm.SmGadget is declared by both Tellma.Core.Tests.Crud.GadgetFilters<{typeof(SmGadget).FullName}> and {broken}.",
                    $"The named filter contributor Tellma.Core.Tests.Crud.ThrowingFilters<{typeof(SmGadget).FullName}> of sm.SmGadget could not be created or read at startup: no tenant here",
                ],
                Problems(problems, CoreCheckNames.StackComponents));
        }

        // --- core.stack-projections ---

        [Fact]
        public async Task A_projection_text_or_a_named_filter_that_does_not_validate_fails()
        {
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync(static contribution => contribution
                .Entity<SmProjection>()
                .NamedFilters<SmGadget>(typeof(BrokenFilters<>)));
            List<string> found = Problems(problems, CoreCheckNames.StackProjections);

            Assert.Equal(7, found.Count);
            Assert.StartsWith("The named filter 'unknown' of the stack of sm.SmGadget, 'Nope = 1', does not validate against the query schema: ", found[0], StringComparison.Ordinal);
            Assert.Equal("The named filter 'nowhere' of the stack of sm.SmGadget goes via 'Nowhere', whose segment 'Nowhere' is not a navigation of the entity it has reached.", found[1]);
            Assert.Equal("The named filter 'sideways' of the stack of sm.SmGadget tests the existence of 'sm.SmGadgetPart' through 'Gizmo', which is not a navigation of sm.SmGadgetPart to sm.SmGadget.", found[2]);
            Assert.StartsWith("The default select of the stack of sm.SmProjection, 'Id, Nope', does not validate against the query schema: ", found[3], StringComparison.Ordinal);
            Assert.StartsWith("The related select of the stack of sm.SmProjection, 'Id, Absent', does not validate against the query schema: ", found[4], StringComparison.Ordinal);
            Assert.StartsWith("The default ordering of the stack of sm.SmProjection, 'Missing desc', does not validate against the query schema: ", found[5], StringComparison.Ordinal);
            Assert.Equal("The details expansion 'Nowhere.Else' of the stack of sm.SmProjection names 'Nowhere', which is neither a navigation nor a child collection of sm.SmProjection.", found[6]);
        }

        // --- core.confirmations ---

        [Fact]
        public async Task A_confirmation_malformed_repeated_or_without_its_string_fails()
        {
            ServiceCollection services = [];
            _ = services.AddSingleton<INeutralStringKeys>(new DeclaredStrings("Gadget.Recost"));
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync(
                static contribution => contribution.Confirmation("Gadget.Recost").Confirmation("gadget.recost").Confirmation("Gadget.Unwritten"),
                static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: static contribution => contribution.Confirmation("Gadget.Recost"))),
                services);

            Assert.Equal(
                [
                    "The feature 'sm' declares the confirmation 'gadget.recost', which is not a dotted PascalCase key.",
                    "The confirmation 'Gadget.Unwritten' of the feature 'sm' has no neutral string resource, so its warning has no message.",
                    "The confirmation 'Gadget.Recost' is declared by both the feature 'sm' and the feature 'acme'.",
                ],
                Problems(problems, CoreCheckNames.Confirmations));
        }

        // --- core.entity-pipeline ---

        [Fact]
        public async Task A_substituted_entity_pipeline_fails()
        {
            ServiceCollection services = [];
            _ = services.AddSingleton<IEntityPipelineFactory, SubstitutePipelineFactory>();
            _ = services.AddScoped<IEntityPipeline<SmGadget, int>>(static _ => throw new InvalidOperationException("Never resolved."));
            IReadOnlyList<CompositionProblem> problems = await StackScenarios.CheckAsync(services: services);

            Assert.Equal(
                [
                    $"IEntityPipelineFactory is registered with {typeof(SubstitutePipelineFactory).FullName}, which is not the platform's entity pipeline; every stack's operations run through the platform's pipeline, which carries the access guards and the validation rounds.",
                    "IEntityPipeline<SmGadget, int> is registered with a factory, which is not the platform's entity pipeline; every stack's operations run through the platform's pipeline, which carries the access guards and the validation rounds.",
                ],
                Problems(problems, CoreCheckNames.EntityPipeline));
        }

        // --- core.enlistments ---

        [Fact]
        public async Task An_enlistment_cycle_or_a_target_that_refuses_enlisted_writes_fails_and_a_self_pair_passes()
        {
            // --- The self-pair and a pair to a stack that accepts enlisted writes ---
            IReadOnlyList<CompositionProblem> selfPair = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.D>("sm", contribute: static contribution => contribution
                    .Add(new StackContributionItem(typeof(SmGadget), typeof(SelfEnlistingGadgetService), StackOperations.All | StackOperations.Enlist))
                    .Entity<SmGizmo>())));
            Assert.Empty(selfPair);

            // --- A cycle: gizmo → sprocket → gizmo ---
            IReadOnlyList<CompositionProblem> cycle = await StackScenarios.CheckAsync(static contribution => contribution
                .PersistEffect<SmGizmo, GizmoEffect>()
                .PersistEffect<SmSprocket, SprocketEffect>());
            Assert.Equal(
                ["The declared enlistments form a cycle: sm.SmGizmo → sm.SmSprocket → sm.SmGizmo; a stack's hooks would re-enter a stack whose hooks enlisted them."],
                Problems(cycle, CoreCheckNames.Enlistments));

            // --- A target without Enlist, and one no stack serves ---
            IReadOnlyList<CompositionProblem> refused = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(StackModel.Feature<SelfEnlistingGadgetService>(static contribution => contribution.Entity<SmGizmo>().PersistEffect<SmGizmo, GizmoEffect>())));
            Assert.Equal(
                [
                    $"The writer {typeof(SelfEnlistingGadgetService).FullName} declares IEnlists<SmGadget>, but the stack of sm.SmGadget does not accept enlisted writes.",
                    $"The writer {typeof(GizmoEffect).FullName} declares IEnlists<SmSprocket>, but no stack serves {typeof(SmSprocket).FullName}.",
                ],
                Problems(refused, CoreCheckNames.Enlistments));
        }

        // --- core.stack-securables ---

        [Fact]
        public async Task A_securable_a_stack_requires_that_is_not_registered_fails_the_audit()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            IStackRegistry stacks = provider.GetRequiredService<IStackRegistry>();
            SecurableRegistry securables = SecurableRegistryTests.Compose(("sm", static builder => builder.Add(StackModel.Gadgets, "Read", StackModel.Gadgets)));

            List<CompositionProblem> problems = [];
            new StackSecurablesCheck().Audit(stacks, securables, problems);

            Assert.Contains("The securable 'sm.SmGadget:Save', which the stack of sm.SmGadget requires, is not registered, so no grant can name it.", problems.Select(static problem => problem.Problem));
            Assert.Contains("The securable 'sm.Inbox:Purge', which the action 'purge' of the routed service 'sm-inbox' requires, is not registered, so no grant can name it.", problems.Select(static problem => problem.Problem));
            Assert.Contains("The securable 'sm.SmGadgetPartNote:Read', which the child query surface of sm.SmGadgetPartNote requires, is not registered, so no grant can name it.", problems.Select(static problem => problem.Problem));
            Assert.All(problems, static problem => Assert.Equal(CoreCheckNames.StackSecurables, problem.Source));
        }

        [Fact]
        public async Task The_audit_derives_the_standard_operations_apart_from_the_contributor()
        {
            // A registry holding every securable the stacks' contributor registers except the
            // gadget's Delete stands for a contributor that lost a standard operation: the audit,
            // reading the stack's segments, still requires it.
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            IStackRegistry stacks = provider.GetRequiredService<IStackRegistry>();
            SecurableDescriptor[] kept = [.. provider.GetRequiredService<ISecurableRegistry>().All
                .Where(static securable => !(securable.Resource == StackModel.Gadgets && securable.Action == "Delete"))];
            SecurableRegistry securables = SecurableRegistryTests.Compose(("sm", builder => Keep(builder, kept)));

            List<CompositionProblem> problems = [];
            new StackSecurablesCheck().Audit(stacks, securables, problems);

            Assert.Equal(
                ["The securable 'sm.SmGadget:Delete', which the stack of sm.SmGadget requires, is not registered, so no grant can name it."],
                problems.Select(static problem => problem.Problem));
        }

        /// <summary>Registers securables as they were registered.</summary>
        /// <param name="builder">The builder.</param>
        /// <param name="securables">The securables.</param>
        private static void Keep(SecurableRegistryBuilder builder, IEnumerable<SecurableDescriptor> securables)
        {
            foreach (SecurableDescriptor securable in securables)
            {
                _ = builder.Add(securable.Resource, securable.Action, securable.FilterRoot, owner: securable.Owner);
            }
        }

        /// <summary>The problems of one check.</summary>
        /// <param name="problems">Every problem.</param>
        /// <param name="source">The check.</param>
        /// <returns>Its problems' texts.</returns>
        private static List<string> Problems(IReadOnlyList<CompositionProblem> problems, string source)
        {
            return [.. problems.From(source).Select(static problem => problem.Problem)];
        }

        /// <summary>The neutral strings of a composition that declares exactly the given keys.</summary>
        /// <param name="keys">The keys.</param>
        private sealed class DeclaredStrings(params string[] keys) : INeutralStringKeys
        {
            /// <inheritdoc />
            public bool Declares(string key)
            {
                return keys.Contains(key, StringComparer.Ordinal);
            }
        }
    }
}
