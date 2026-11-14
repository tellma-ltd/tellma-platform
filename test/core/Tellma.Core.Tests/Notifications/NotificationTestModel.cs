// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Notifications
{
    /// <summary>
    ///     The notifier tests' catalogue: an <c>acme</c> feature declaring a targeted mutable type, an
    ///     untargeted non-mutable one, a channel-only one, two more the renderer formats, and a
    ///     <c>push</c> channel on by default; and, for the delivering composition, senders for
    ///     <c>email</c> and <c>push</c>.
    /// </summary>
    internal static class NotificationTestModel
    {
        /// <summary>A mutable type that targets <see cref="OrderResource" />.</summary>
        public const string Shipped = "acme.order.shipped";

        /// <summary>A non-mutable warning without a target.</summary>
        public const string Held = "acme.order.held";

        /// <summary>A channel-only type: no inbox row.</summary>
        public const string SmsOnly = "acme.order.texted";

        /// <summary>A type whose text formats a calendar date.</summary>
        public const string ReportDue = "acme.report.due";

        /// <summary>A type whose text formats arguments of every other kind.</summary>
        public const string Raw = "acme.raw";

        /// <summary>The resource <see cref="Shipped" /> targets.</summary>
        public const string OrderResource = "acme.Order";

        /// <summary>The channel the feature registers, on by default.</summary>
        public const string Push = "push";

        /// <summary>Composes the catalogue with no sender: no channel but the inbox delivers.</summary>
        /// <param name="tellma">The builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.D>("acme", contribute: static contribution => Contribute(contribution)));
        }

        /// <summary>Composes the catalogue with senders for <c>email</c> and <c>push</c>.</summary>
        /// <param name="tellma">The builder.</param>
        public static void ComposeDelivering(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.D>("acme", contribute: static contribution => Contribute(contribution)
                .Scoped<INotificationChannel, EmailSender>()
                .Scoped<INotificationChannel, PushSender>()));
        }

        /// <summary>The catalogue's types and channel.</summary>
        /// <param name="contribution">The contribution.</param>
        /// <returns>The contribution, for chaining.</returns>
        private static FeatureContribution Contribute(FeatureContribution contribution)
        {
            return contribution
                .NotificationType(new NotificationTypeDescriptor(Shipped, "orders", Mutable: true, TargetResource: OrderResource))
                .NotificationType(new NotificationTypeDescriptor(Held, "orders", Mutable: false, TargetResource: null, Severity: NotificationSeverity.Warning, Toast: true))
                .NotificationType(new NotificationTypeDescriptor(SmsOnly, "orders", Mutable: false, TargetResource: null, Inbox: false))
                .NotificationType(new NotificationTypeDescriptor(ReportDue, "reports", Mutable: true, TargetResource: null))
                .NotificationType(new NotificationTypeDescriptor(Raw, "reports", Mutable: true, TargetResource: null))
                .NotificationChannel(new NotificationChannelDescriptor(Push, DefaultEnabled: true));
        }

        /// <summary>A sender that only names its channel; the notifier never delivers through it.</summary>
        /// <param name="key">The channel's key.</param>
        internal abstract class KeyedSender(string key) : INotificationChannel
        {
            /// <inheritdoc />
            public string Key => key;

            /// <inheritdoc />
            public Task DeliverAsync(IOpenWriteHost host, NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients, CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public Task<TestNotificationResult> SendTestAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>The email channel's sender.</summary>
        internal sealed class EmailSender() : KeyedSender(CoreFeature.EmailChannel);

        /// <summary>The push channel's sender.</summary>
        internal sealed class PushSender() : KeyedSender(Push);

        /// <summary>A publisher that records every event handed to it, and fails when told to.</summary>
        internal sealed class RecordingPublisher : IClientEventPublisher
        {
            /// <summary>The events published, in order.</summary>
            public List<ClientEvent> Published { get; } = [];

            /// <summary>When set, every publish throws it.</summary>
            public Exception? Failure { get; set; }

            /// <inheritdoc />
            public void Publish(Abstractions.Data.IDataBatch? batch, ClientEvent clientEvent)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public Task PublishAsync(ClientEvent clientEvent)
            {
                if (Failure is not null)
                {
                    throw Failure;
                }

                Published.Add(clientEvent);
                return Task.CompletedTask;
            }
        }
    }
}
