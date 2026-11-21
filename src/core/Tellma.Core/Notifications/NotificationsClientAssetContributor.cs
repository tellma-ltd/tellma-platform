// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Realtime;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     Writes the notification catalogue into the Core chunk of the client metadata: the
    ///     <c>notificationTypes</c>, <c>notificationChannels</c> and <c>clientEvents</c> sections, each
    ///     in registration order. Types, channels and events are composed per distribution, never per
    ///     tenant, so the client learns them with the bundle and no route serves them.
    /// </summary>
    /// <param name="types">The notification types.</param>
    /// <param name="channels">The notification channels.</param>
    /// <param name="events">The client events.</param>
    public sealed class NotificationsClientAssetContributor(
        INotificationTypeRegistry types,
        INotificationChannelRegistry channels,
        IClientEventRegistry events) : IClientAssetContributor
    {
        /// <summary>The section listing every notification type with all its members.</summary>
        public const string TypesSection = "notificationTypes";

        /// <summary>The section listing every notification channel with its default.</summary>
        public const string ChannelsSection = "notificationChannels";

        /// <summary>The section listing every client event name.</summary>
        public const string ClientEventsSection = "clientEvents";

        /// <inheritdoc />
        public void Contribute(ClientAssetContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // The platform's JSON rules: camelCase members and enums as their names, so a severity
            // reads "Warning" on the client exactly as on the wire.
            context.Section(ClientMetadataChunk.Core, TypesSection, writer => JsonSerializer.Serialize(writer, types.All, TellmaJson.Options));
            context.Section(ClientMetadataChunk.Core, ChannelsSection, writer => JsonSerializer.Serialize(writer, channels.All, TellmaJson.Options));
            context.Section(ClientMetadataChunk.Core, ClientEventsSection, writer => JsonSerializer.Serialize(writer, events.Names, TellmaJson.Options));
        }
    }
}
