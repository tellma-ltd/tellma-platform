// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Crud;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>
    ///     The items that attach to a stack: the sugar that builds them, the realizers that record
    ///     them once each in feature order, and the malformed items they refuse before the host is
    ///     built.
    /// </summary>
    public class CrudContributionTests
    {
        [Fact]
        public void The_sugar_records_each_item()
        {
            FeatureContribution contribution = new FeatureContribution()
                .EntityCompanion<SmGadget, GadgetOperations<SmGadget>>()
                .Validator<SmGadget, PlainGadgetRules>()
                .Validator<SmGadget>(typeof(GadgetRules<>))
                .PersistEffect<SmGizmo, GizmoEffect>()
                .PersistEffect<SmSprocket>(typeof(SprocketEffect))
                .DetailsContributor<SmGadget, GadgetDetails<SmGadget>>()
                .DetailsContributor<SmGadget>(typeof(GadgetDetails<>))
                .NamedFilters<SmGadget, GadgetFilters<SmGadget>>()
                .NamedFilters<SmGadget>(typeof(GadgetFilters<>))
                .ApiService<InboxService>()
                .Confirmation("Gadget.Recost");

            Assert.Equal(
                [
                    new StackCompanionContributionItem(typeof(SmGadget), typeof(GadgetOperations<SmGadget>)),
                    new ValidatorContributionItem(typeof(SmGadget), typeof(PlainGadgetRules), ComponentKind.Validator),
                    new ValidatorContributionItem(typeof(SmGadget), typeof(GadgetRules<>), ComponentKind.Validator),
                    new ValidatorContributionItem(typeof(SmGizmo), typeof(GizmoEffect), ComponentKind.PersistEffect),
                    new ValidatorContributionItem(typeof(SmSprocket), typeof(SprocketEffect), ComponentKind.PersistEffect),
                    new ValidatorContributionItem(typeof(SmGadget), typeof(GadgetDetails<SmGadget>), ComponentKind.DetailsContributor),
                    new ValidatorContributionItem(typeof(SmGadget), typeof(GadgetDetails<>), ComponentKind.DetailsContributor),
                    new ValidatorContributionItem(typeof(SmGadget), typeof(GadgetFilters<SmGadget>), ComponentKind.NamedFilterContributor),
                    new ValidatorContributionItem(typeof(SmGadget), typeof(GadgetFilters<>), ComponentKind.NamedFilterContributor),
                    new ApiServiceContributionItem(typeof(InboxService)),
                    new ConfirmationContributionItem("Gadget.Recost"),
                ],
                contribution.Items);
            _ = Assert.Throws<ArgumentNullException>(static () => new FeatureContribution().Validator<SmGadget>(null!));
            _ = Assert.Throws<ArgumentNullException>(static () => new FeatureContribution().Confirmation(null!));
        }

        [Fact]
        public void Each_item_is_recorded_once_with_its_feature_and_confirmations_as_declared()
        {
            using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("first", contribute: static contribution => contribution
                    .EntityCompanion<SmGadget, GadgetOperations<SmGadget>>()
                    .EntityCompanion<SmGadget, GadgetOperations<SmGadget>>()
                    .Validator<SmGadget>(typeof(GadgetRules<>))
                    .Validator<SmGadget>(typeof(GadgetRules<>))
                    .PersistEffect<SmGadget>(typeof(GadgetRules<>))
                    .ApiService<InboxService>()
                    .Confirmation("Gadget.Recost")))
                .AddFeature(new TestFeature<Tag.B>("second", contribute: static contribution => contribution
                    .ApiService<InboxService>()
                    .Confirmation("Gadget.Recost"))))
                .BuildServiceProvider();
            TellmaCompositionRecord record = provider.GetRequiredService<TellmaCompositionRecord>();

            // --- One companion and one component per entity, class and kind; the first feature keeps them ---
            ContributedCompanion companion = Assert.Single(record.Companions);
            Assert.Equal((typeof(SmGadget), typeof(GadgetOperations<SmGadget>), "first"), (companion.EntityType, companion.CompanionType, companion.Owner.Name));
            Assert.Equal(
                [(typeof(GadgetRules<>), ComponentKind.Validator), (typeof(GadgetRules<>), ComponentKind.PersistEffect)],
                record.Components.Select(static component => (component.ComponentType, component.Kind)));
            Assert.Equal("first", Assert.Single(record.ApiServices).Owner.Name);

            // --- A confirmation declared twice is recorded twice, for the startup gate to judge ---
            Assert.Equal([("Gadget.Recost", "first"), ("Gadget.Recost", "second")], record.Confirmations.Select(static confirmation => (confirmation.Key, confirmation.Owner.Name)));
        }

        [Fact]
        public void A_malformed_item_fails_the_graph_gate()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("first", contribute: static contribution => contribution
                    .Add(new StackCompanionContributionItem(typeof(IComparable), typeof(object)))
                    .Add(new StackCompanionContributionItem(typeof(SmGadget), typeof(GadgetOperations<>)))
                    .Add(new ValidatorContributionItem(typeof(List<>), typeof(PlainGadgetRules), ComponentKind.Validator))
                    .Add(new ValidatorContributionItem(typeof(SmGadget), typeof(PlainGadgetRules), (ComponentKind)42))
                    .Add(new ValidatorContributionItem(typeof(SmGadget), typeof(IComparable), ComponentKind.Validator))
                    .Add(new ValidatorContributionItem(typeof(SmGadget), typeof(Dictionary<,>), ComponentKind.Validator))
                    .Add(new ValidatorContributionItem(typeof(SmGadget), typeof(PlainGadgetRules), ComponentKind.PersistEffect))
                    .Add(new ApiServiceContributionItem(typeof(IDisposable)))
                    .Add(new ConfirmationContributionItem(null!)))));

            Assert.Equal(
                [
                    "Feature 'first' attaches a stack companion to System.IComparable, which is not a closed entity class.",
                    $"Feature 'first' attaches the stack companion {typeof(GadgetOperations<>).Namespace}.GadgetOperations<TGadget> to {typeof(SmGadget).FullName}, which is not a concrete, closed class.",
                    "Feature 'first' attaches a component to System.Collections.Generic.List<T>, which is not a closed entity class.",
                    $"Feature 'first' attaches a component of the undefined kind 42 to {typeof(SmGadget).FullName}.",
                    $"Feature 'first' attaches the validator System.IComparable to {typeof(SmGadget).FullName}, which is not a concrete class.",
                    $"Feature 'first' attaches the open validator System.Collections.Generic.Dictionary<TKey, TValue> to {typeof(SmGadget).FullName}, which has 2 type parameters; the platform closes a definition over the stack's leaf alone.",
                    $"Feature 'first' attaches the persist effect {typeof(PlainGadgetRules).FullName} to {typeof(SmGadget).FullName}, but it does not implement IPersistEffect<SmGadget>.",
                    "Feature 'first' contributes the routed service System.IDisposable, which is not a concrete, closed class.",
                    "Feature 'first' declares a confirmation without a key.",
                ],
                problems.Select(static problem => problem.Problem));
        }
    }
}
