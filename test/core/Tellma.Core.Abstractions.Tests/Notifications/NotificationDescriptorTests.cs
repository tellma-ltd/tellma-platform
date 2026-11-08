// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Core.Abstractions.Tests.Notifications
{
    /// <summary>The notification type and channel descriptors: their defaults, and the shape the client metadata carries.</summary>
    public class NotificationDescriptorTests
    {
        [Fact]
        public void A_type_defaults_to_an_informational_inbox_row_without_a_toast()
        {
            NotificationTypeDescriptor type = new("core.user.added", "users", Mutable: true, TargetResource: null);

            Assert.Equal(NotificationSeverity.Info, type.Severity);
            Assert.False(type.Toast);
            Assert.True(type.Inbox);
        }

        [Fact]
        public void Severities_ascend_from_info_to_error()
        {
            // GetValues lists members in ascending value order.
            Assert.Equal(
                [NotificationSeverity.Info, NotificationSeverity.Success, NotificationSeverity.Warning, NotificationSeverity.Error],
                Enum.GetValues<NotificationSeverity>());
        }

        [Fact]
        public void A_type_serializes_with_every_member_for_the_client()
        {
            NotificationTypeDescriptor type = new("core.export.ready", "files", Mutable: false, "core.Export", NotificationSeverity.Success, Toast: true);

            Assert.Equal(
                /*lang=json,strict*/ """{"key":"core.export.ready","category":"files","mutable":false,"targetResource":"core.Export","severity":"Success","toast":true,"inbox":true}""",
                JsonSerializer.Serialize(type, TellmaJson.Options));
        }

        [Fact]
        public void A_channel_serializes_its_key_and_default()
        {
            Assert.Equal(
                /*lang=json,strict*/ """{"key":"email","defaultEnabled":false}""",
                JsonSerializer.Serialize(new NotificationChannelDescriptor("email", DefaultEnabled: false), TellmaJson.Options));
        }
    }
}
