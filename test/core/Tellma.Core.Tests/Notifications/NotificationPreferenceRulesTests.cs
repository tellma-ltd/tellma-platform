// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Composition;
using Tellma.Core.Notifications;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Data.Batches;
using static Tellma.Core.Tests.Notifications.NotificationTestModel;

namespace Tellma.Core.Tests.Notifications
{
    /// <summary>
    ///     The rules of a preferences save with no database: the deviations kept and the defaults
    ///     dropped, every refusal at its row and member with its arguments, and the notifications'
    ///     self-scope criterion.
    /// </summary>
    public sealed class NotificationPreferenceRulesTests : IDisposable
    {
        /// <summary>The harness, composed with the catalogue: push is on by default, email off, the inbox on.</summary>
        private readonly BatchHarness _harness = new(compose: Compose);

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        [Fact]
        public void Rows_equal_to_their_channels_default_are_dropped_and_the_rest_kept_in_order()
        {
            IReadOnlyList<NotificationPreferenceList> kept = Deviations(
                Row(Shipped, CoreFeature.InboxChannel, true),
                Row(Shipped, CoreFeature.EmailChannel, true),
                Row(Shipped, Push, true),
                Row(Held, Push, false),
                Row(Held, CoreFeature.EmailChannel, false));

            Assert.Equal(
                [(Shipped, CoreFeature.EmailChannel, true), (Held, Push, false)],
                kept.Select(static row => (row.Type, row.Channel, row.Enabled)));
        }

        [Fact]
        public void Unknown_types_and_channels_are_refused_at_their_members()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Deviations(
                Row("acme.nothing", Push, true),
                Row(Shipped, "fax", true),
                null));

            Assert.Equal(
                [
                    ("[0].Type", NotificationValidationCodes.UnknownType),
                    ("[1].Channel", NotificationValidationCodes.UnknownChannel),
                    ("[2].Type", NotificationValidationCodes.UnknownType),
                ],
                refused.Errors.Select(static error => (error.Path.ToString(), error.Code)));
            Assert.Equal("acme.nothing", refused.Errors[0].Arguments["type"]);
            Assert.Equal("fax", refused.Errors[1].Arguments["channel"]);
        }

        [Fact]
        public void A_repeated_pair_is_refused_at_the_later_rows_channel()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Deviations(
                Row(Shipped, Push, false),
                Row(Shipped, CoreFeature.EmailChannel, true),
                Row(Shipped, Push, true)));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(("[2].Channel", NotificationValidationCodes.DuplicatePreference), (error.Path.ToString(), error.Code));
            Assert.Equal(Shipped, error.Arguments["type"]);
            Assert.Equal(Push, error.Arguments["channel"]);
        }

        [Fact]
        public void Turning_the_inbox_off_for_a_type_that_cannot_be_muted_is_refused_and_for_a_mutable_one_kept()
        {
            ValidationException refused = Assert.Throws<ValidationException>(() => Deviations(Row(Held, CoreFeature.InboxChannel, false)));
            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(("[0].Enabled", NotificationValidationCodes.CannotMute), (error.Path.ToString(), error.Code));
            Assert.Equal(Held, error.Arguments["type"]);

            // A non-mutable type's other channels are the user's to turn off.
            Assert.Single(Deviations(Row(Held, Push, false)));
            Assert.Single(Deviations(Row(Shipped, CoreFeature.InboxChannel, false)));
        }

        [Fact]
        public void The_notifications_self_scope_reads_the_callers_own_rows()
        {
            IAccessCriteriaProvider provider = Assert.Single(
                _harness.Services.GetServices<IAccessCriteriaProvider>(),
                static candidate => candidate.Resource == CoreResources.Notification);

            AccessCriterion criterion = Assert.Single(provider.Criteria);
            Assert.Equal(AccessActions.Read, criterion.Action);
            Assert.Equal("UserId = me()", Assert.IsType<FilterTree.LeafNode>(criterion.Filter).Text);
            Assert.Equal("self", criterion.Reason);
        }

        /// <summary>Runs the rules over the harness's registries.</summary>
        /// <param name="rows">The rows, as sent.</param>
        /// <returns>The deviations.</returns>
        private IReadOnlyList<NotificationPreferenceList> Deviations(params NotificationPreference?[] rows)
        {
            return NotificationPreferenceRules.Deviations(
                rows,
                _harness.Services.GetRequiredService<INotificationTypeRegistry>(),
                _harness.Services.GetRequiredService<INotificationChannelRegistry>());
        }

        /// <summary>A row as sent.</summary>
        /// <param name="type">The type.</param>
        /// <param name="channel">The channel.</param>
        /// <param name="enabled">Whether it is on.</param>
        /// <returns>The row.</returns>
        private static NotificationPreference Row(string type, string channel, bool enabled)
        {
            return new NotificationPreference { Type = type, Channel = channel, Enabled = enabled };
        }
    }
}
