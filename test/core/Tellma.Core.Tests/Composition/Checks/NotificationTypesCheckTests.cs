// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.notification-types</c>: keys and categories follow their grammars, keys are unique,
    ///     severities are defined, channel-only types follow their rule, and targets name a top-level
    ///     entity with an integer key.
    /// </summary>
    public class NotificationTypesCheckTests
    {
        private const string Check = "core.notification-types";

        [Fact]
        public async Task The_core_catalogue_passes()
        {
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(Check));
        }

        [Fact]
        public async Task Sound_types_pass_with_a_target_a_channel_only_type_and_every_severity()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(
                new NotificationTypeDescriptor("fixture.user.invited", "users", Mutable: false, "core.User", NotificationSeverity.Error, Toast: true),
                new NotificationTypeDescriptor("fixture.digest.sent", "digest-mail", Mutable: false, null, NotificationSeverity.Warning, Toast: false, Inbox: false),
                new NotificationTypeDescriptor("fixture.a1-b.c-2", "a", Mutable: true, null, NotificationSeverity.Success));

            Assert.Empty(problems.From(Check));
        }

        [Theory]
        [InlineData("fixture")]
        [InlineData("Fixture.user.invited")]
        [InlineData("fixture.User")]
        [InlineData("fixture..invited")]
        [InlineData("fixture.1user")]
        [InlineData("fixture.-user")]
        [InlineData("fixture_user.invited")]
        [InlineData("fixture.user.")]
        [InlineData("fixture.user\n")]
        [InlineData("")]
        public async Task A_malformed_key_is_refused(string key)
        {
            CompositionProblem problem = Assert.Single((await CheckAsync(new NotificationTypeDescriptor(key, "users", false, null))).From(Check));

            Assert.Equal($"The notification type '{key}' of the feature 'fixture' does not have a well-formed key.", problem.Problem);
        }

        [Fact]
        public async Task A_key_over_sixty_four_characters_is_refused_and_one_of_sixty_four_is_not()
        {
            string longest = "fixture." + new string('a', 56);

            Assert.Empty((await CheckAsync(new NotificationTypeDescriptor(longest, "users", false, null))).From(Check));
            _ = Assert.Single((await CheckAsync(new NotificationTypeDescriptor(longest + "a", "users", false, null))).From(Check));
        }

        [Theory]
        [InlineData("Users")]
        [InlineData("user.added")]
        [InlineData("1users")]
        [InlineData("")]
        [InlineData("abcdefghijklmnopqrstuvwxyzabcdefg")]
        public async Task A_malformed_category_is_refused(string category)
        {
            CompositionProblem problem = Assert.Single((await CheckAsync(new NotificationTypeDescriptor("fixture.user.invited", category, false, null))).From(Check));

            Assert.Equal($"The notification type 'fixture.user.invited' of the feature 'fixture' has the category '{category}', which is not well formed.", problem.Problem);
        }

        [Fact]
        public async Task A_key_registered_twice_names_both_features()
        {
            NotificationTypeDescriptor type = new("fixture.user.invited", "users", false, null);
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribution => contribution.NotificationType(type)))
                .AddFeature(new TestFeature<Tag.B>("other", contribute: contribution => contribution.NotificationType(type with { Category = "other" }))));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Equal("The notification type 'fixture.user.invited' is registered more than once, by the features 'fixture' and 'other'.", problem.Problem);
        }

        [Fact]
        public async Task A_key_differing_only_in_case_is_another_key_and_refused_by_the_grammar()
        {
            // Keys compare ordinally, so the upper-case one is not a duplicate: the grammar refuses it.
            CompositionProblem problem = Assert.Single((await CheckAsync(
                new NotificationTypeDescriptor("fixture.user.invited", "users", false, null),
                new NotificationTypeDescriptor("fixture.User.invited", "users", false, null))).From(Check));

            Assert.Contains("does not have a well-formed key", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_undefined_severity_is_refused()
        {
            CompositionProblem problem = Assert.Single((await CheckAsync(
                new NotificationTypeDescriptor("fixture.user.invited", "users", false, null, (NotificationSeverity)9))).From(Check));

            Assert.Equal("The notification type 'fixture.user.invited' of the feature 'fixture' has the undefined severity 9.", problem.Problem);
        }

        [Theory]
        [InlineData(true, false, "mutable")]
        [InlineData(false, true, "a toast")]
        [InlineData(true, true, "mutable and a toast")]
        public async Task A_channel_only_type_that_is_mutable_or_a_toast_is_refused(bool mutable, bool toast, string described)
        {
            CompositionProblem problem = Assert.Single((await CheckAsync(
                new NotificationTypeDescriptor("fixture.digest.sent", "digests", mutable, null, Toast: toast, Inbox: false))).From(Check));

            Assert.Equal($"The notification type 'fixture.digest.sent' of the feature 'fixture' is channel-only, yet {described}.", problem.Problem);
        }

        [Fact]
        public async Task A_target_that_is_not_an_entity_of_the_model_is_refused()
        {
            CompositionProblem problem = Assert.Single((await CheckAsync(
                new NotificationTypeDescriptor("fixture.export.ready", "files", false, "core.Export"))).From(Check));

            Assert.Equal("The notification type 'fixture.export.ready' of the feature 'fixture' targets 'core.Export', which is not a top-level entity of the model.", problem.Problem);
        }

        [Fact]
        public async Task A_target_naming_a_child_entity_or_differing_in_case_is_refused()
        {
            // A child has no stack, so the client has no route to it.
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            string child = provider.GetRequiredService<IEntityMetadataProvider>().All.First(static entity => !entity.IsTopLevel).Name;

            IReadOnlyList<CompositionProblem> problems = (await CheckAsync(
                new NotificationTypeDescriptor("fixture.child.changed", "users", false, child),
                new NotificationTypeDescriptor("fixture.user.changed", "users", false, "core.user"))).From(Check);

            Assert.Equal(
                [
                    $"The notification type 'fixture.child.changed' of the feature 'fixture' targets '{child}', which is not a top-level entity of the model.",
                    "The notification type 'fixture.user.changed' of the feature 'fixture' targets 'core.user', which is not a top-level entity of the model.",
                ],
                problems.Select(static problem => problem.Problem));
        }

        [Fact]
        public void A_target_whose_entity_has_a_guid_key_is_refused()
        {
            // The platform's entities are keyed by integers, so no composed model holds a Guid-keyed
            // entity; the check is exercised on metadata built by hand.
            TellmaCompositionRecord record = new(TestComposition.Slug, TestComposition.Configuration(), new TestEnvironment());
            record.NotificationTypes.Add(new ContributedNotificationType(
                new NotificationTypeDescriptor("fixture.token.issued", "tokens", false, "fixture.Token"), new TestFeature<Tag.A>("fixture")));
            record.NotificationTypes.Add(new ContributedNotificationType(
                new NotificationTypeDescriptor("fixture.counter.reset", "tokens", false, "fixture.Counter"), new TestFeature<Tag.A>("fixture")));

            List<CompositionProblem> problems = [];
            new NotificationTypesCheck(record).CheckTargets(
                new HandBuiltMetadata([HandBuiltMetadata.Entity("fixture.Token", typeof(Guid)), HandBuiltMetadata.Entity("fixture.Counter", typeof(long))]),
                problems);

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal(
                "The notification type 'fixture.token.issued' of the feature 'fixture' targets 'fixture.Token', whose key is a Guid, which a notification's 64-bit target id cannot hold.",
                problem.Problem);
        }

        /// <summary>Runs every check over a composition in which one feature, <c>fixture</c>, registers the types.</summary>
        /// <param name="types">The types.</param>
        /// <returns>The problems.</returns>
        private static Task<IReadOnlyList<CompositionProblem>> CheckAsync(params NotificationTypeDescriptor[] types)
        {
            return TestComposition.CheckAsync(tellma => tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribution =>
            {
                foreach (NotificationTypeDescriptor type in types)
                {
                    _ = contribution.NotificationType(type);
                }
            })));
        }

        /// <summary>Entity metadata built by hand, for shapes no composed model holds.</summary>
        /// <param name="all">The entities.</param>
        private sealed class HandBuiltMetadata(IReadOnlyList<EntityMetadata> all) : IEntityMetadataProvider
        {
            /// <inheritdoc />
            public IReadOnlyList<EntityMetadata> All => all;

            /// <inheritdoc />
            public string StorageFingerprint => "fingerprint";

            /// <inheritdoc />
            public EntityMetadata Get(Type entityType)
            {
                return all.Single(entity => entity.ClrType == entityType);
            }

            /// <summary>The metadata of a top-level entity with nothing but its key.</summary>
            /// <param name="name">The entity's name.</param>
            /// <param name="keyType">The key's type.</param>
            /// <returns>The metadata.</returns>
            public static EntityMetadata Entity(string name, Type keyType)
            {
                PropertyMetadata key = new(
                    "Id", "Id", keyType, PropertyOwnership.ServerOwned, false, false, null, null, null, null, null, null, false, null, false, false,
                    static _ => null, static (_, _) => { });

                return new EntityMetadata(
                    typeof(object), name, new TableName("fixture", name), null, key, [key], [], [], [],
                    isTopLevel: true, isTemporal: false, isActivatable: false, isJobEntity: false, isCacheable: false, tree: null, blobReferences: [],
                    displayName: new DisplayNameMetadata(key, null, null),
                    static () => new object(),
                    children: static _ => [],
                    static _ => [],
                    schemaFingerprint: static _ => "fingerprint");
            }
        }
    }
}
