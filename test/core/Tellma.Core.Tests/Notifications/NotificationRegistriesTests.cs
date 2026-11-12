// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Notifications;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Notifications
{
    /// <summary>
    ///     The notification type, channel and client event registries, as built directly and as
    ///     composed, and the client metadata sections written from them.
    /// </summary>
    public class NotificationRegistriesTests
    {
        // --- Built directly ---

        [Fact]
        public void The_type_registry_keeps_registration_order_and_the_first_type_of_a_key()
        {
            NotificationTypeDescriptor first = new("gl.posting.failed", "jobs", Mutable: false, TargetResource: null);
            NotificationTypeDescriptor second = new("gl.period.closed", "periods", Mutable: true, TargetResource: null);
            NotificationTypeDescriptor third = new("gl.export.ready", "jobs", Mutable: true, TargetResource: null);
            NotificationTypeDescriptor repeated = first with { Category = "other" };

            NotificationTypeRegistry registry = new([first, second, null!, repeated, third, new NotificationTypeDescriptor(null!, "jobs", false, null)]);

            Assert.Equal([first, second, third], registry.All);
            Assert.Equal(["jobs", "periods"], registry.Categories);
            Assert.Same(first, registry.Find("gl.posting.failed"));
        }

        [Fact]
        public void A_type_is_found_by_its_exact_key()
        {
            NotificationTypeRegistry registry = new([new NotificationTypeDescriptor("gl.posting.failed", "jobs", false, null)]);

            Assert.Null(registry.Find("GL.posting.failed"));
            Assert.Null(registry.Find("gl.posting"));
            Assert.Null(registry.Find(null!));
        }

        [Fact]
        public void The_channel_registry_keeps_registration_order_and_the_first_channel_of_a_key()
        {
            NotificationChannelDescriptor inbox = new("inbox", true);
            NotificationChannelDescriptor sms = new("sms", false);

            NotificationChannelRegistry registry = new([inbox, null!, sms, new NotificationChannelDescriptor("inbox", false)]);

            Assert.Equal([inbox, sms], registry.All);
            Assert.Same(inbox, registry.Find("inbox"));
            Assert.Null(registry.Find("Inbox"));
            Assert.Null(registry.Find(null!));
        }

        [Fact]
        public void The_client_event_registry_keeps_registration_order_and_each_name_once()
        {
            ClientEventRegistry registry = new(
            [
                new ClientEventContributionItem("inbox.changed"),
                new ClientEventContributionItem("job.changed"),
                new ClientEventContributionItem("inbox.changed"),
                null!,
                new ClientEventContributionItem(null!),
            ]);

            Assert.Equal(["inbox.changed", "job.changed"], registry.Names);
            Assert.True(registry.IsRegistered("job.changed"));
            Assert.False(registry.IsRegistered("Job.changed"));
            Assert.False(registry.IsRegistered(null!));
        }

        // --- As composed ---

        [Fact]
        public async Task The_core_catalogue_comes_first_and_a_features_items_follow_in_its_order()
        {
            NotificationTypeDescriptor approved = new("acme.document.approved", "documents", Mutable: true, TargetResource: null, NotificationSeverity.Success, Toast: true);
            await using ServiceProvider provider = TestComposition.Compose(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribution => contribution
                    .NotificationType(approved)
                    .NotificationChannel(new NotificationChannelDescriptor("sms", DefaultEnabled: false))
                    .ClientEvent("acme.document.changed"))))
                .BuildServiceProvider(validateScopes: true);

            INotificationTypeRegistry types = provider.GetRequiredService<INotificationTypeRegistry>();
            Assert.Equal(["core.user.added", "acme.document.approved"], types.All.Select(static type => type.Key));
            Assert.Equal(["users", "documents"], types.Categories);
            Assert.Same(approved, types.Find("acme.document.approved"));

            NotificationTypeDescriptor userAdded = types.Find("core.user.added")!;
            Assert.Equal(new NotificationTypeDescriptor("core.user.added", "users", Mutable: true, TargetResource: null), userAdded);

            Assert.Equal(
                [new NotificationChannelDescriptor("inbox", true), new NotificationChannelDescriptor("email", false), new NotificationChannelDescriptor("sms", false)],
                provider.GetRequiredService<INotificationChannelRegistry>().All);

            Assert.Equal(["inbox.changed", "session.ended", "acme.document.changed"], provider.GetRequiredService<IClientEventRegistry>().Names);
        }

        [Fact]
        public void The_sugar_refuses_null()
        {
            FeatureContribution contribution = new();

            _ = Assert.Throws<ArgumentNullException>(() => contribution.NotificationType(null!));
            _ = Assert.Throws<ArgumentNullException>(() => contribution.NotificationChannel(null!));
            _ = Assert.Throws<ArgumentNullException>(() => contribution.ClientEvent(null!));
            Assert.Empty(contribution.Items);
        }

        [Fact]
        public void An_item_without_its_descriptor_or_name_is_a_composition_problem()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Add(new NotificationTypeContributionItem(null!))
                    .Add(new NotificationChannelContributionItem(null!))
                    .Add(new ClientEventContributionItem(null!)))));

            Assert.Equal(
                [
                    "Feature 'fixture' contributes a notification type without a descriptor.",
                    "Feature 'fixture' contributes a notification channel without a descriptor.",
                    "Feature 'fixture' contributes a client event without a name.",
                ],
                problems.Select(static problem => problem.Problem));
        }

        // --- The client metadata ---

        [Fact]
        public async Task The_contributor_writes_the_three_sections_of_the_core_chunk_in_registration_order()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .NotificationType(new NotificationTypeDescriptor("acme.export.ready", "files", Mutable: false, "core.User", NotificationSeverity.Success, Toast: true))
                    .NotificationType(new NotificationTypeDescriptor("acme.digest.sent", "digests", Mutable: false, null, Inbox: false))
                    .ClientEvent("acme.document.changed"))))
                .BuildServiceProvider(validateScopes: true);

            // The notifications contributor alone: the core chunk also carries the distribution's own sections.
            ClientAssetContext context = new(provider);
            provider.GetServices<IClientAssetContributor>().OfType<NotificationsClientAssetContributor>().Single().Contribute(context);

            Assert.Equal(
                [
                    (ClientMetadataChunk.Core, "notificationTypes", /*lang=json,strict*/ """[{"key":"core.user.added","category":"users","mutable":true,"severity":"Info","toast":false,"inbox":true},{"key":"acme.export.ready","category":"files","mutable":false,"targetResource":"core.User","severity":"Success","toast":true,"inbox":true},{"key":"acme.digest.sent","category":"digests","mutable":false,"severity":"Info","toast":false,"inbox":false}]"""),
                    (ClientMetadataChunk.Core, "notificationChannels", /*lang=json,strict*/ """[{"key":"inbox","defaultEnabled":true},{"key":"email","defaultEnabled":false}]"""),
                    (ClientMetadataChunk.Core, "clientEvents", /*lang=json,strict*/ """["inbox.changed","session.ended","acme.document.changed"]"""),
                ],
                context.Sections.Select(static section => (section.Chunk, section.Name, Encoding.UTF8.GetString(section.Json.Span))));
        }
    }
}
