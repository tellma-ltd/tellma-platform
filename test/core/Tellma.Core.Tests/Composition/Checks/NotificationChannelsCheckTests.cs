// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.notification-channels</c>: keys follow their grammar and are unique, the baseline
    ///     channels are composed, and each sender names a registered channel other than the inbox, at
    ///     most one per channel.
    /// </summary>
    public class NotificationChannelsCheckTests
    {
        private const string Check = "core.notification-channels";

        [Fact]
        public async Task The_baseline_channels_pass_without_a_sender()
        {
            // A channel without a sender keeps its preferences and delivers nothing.
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(Check));
        }

        [Fact]
        public async Task A_channel_with_its_sender_passes()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .NotificationChannel(new NotificationChannelDescriptor("sms", DefaultEnabled: false))
                    .Scoped<INotificationChannel, SmsChannel>()
                    .Scoped<INotificationChannel, EmailChannel>())));

            Assert.Empty(problems.From(Check));
        }

        [Theory]
        [InlineData("SMS")]
        [InlineData("push.web")]
        [InlineData("1sms")]
        [InlineData("-sms")]
        [InlineData("")]
        [InlineData("abcdefghijklmnopq")]
        public async Task A_malformed_key_is_refused(string key)
        {
            CompositionProblem problem = Assert.Single((await CheckAsync(new NotificationChannelDescriptor(key, false))).From(Check));

            Assert.Equal($"The notification channel '{key}' of the feature 'fixture' does not have a well-formed key.", problem.Problem);
        }

        [Fact]
        public async Task A_key_of_sixteen_characters_passes()
        {
            Assert.Empty((await CheckAsync(new NotificationChannelDescriptor("abcdefghijklmnop", false))).From(Check));
        }

        [Fact]
        public async Task A_key_registered_twice_names_both_features()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(new NotificationChannelDescriptor("email", true));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Equal("The notification channel 'email' is registered more than once, by the features 'core' and 'fixture'.", problem.Problem);
        }

        [Fact]
        public async Task A_missing_baseline_channel_is_reported()
        {
            // Core always registers both; a record built without them shows what the check guards.
            TellmaCompositionRecord record = new(TestComposition.Slug, TestComposition.Configuration(), new TestEnvironment());
            record.NotificationChannels.Add(new ContributedNotificationChannel(new NotificationChannelDescriptor("inbox", true), new TestFeature<Tag.A>("core")));

            await using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
            IReadOnlyList<CompositionProblem> problems = await new NotificationChannelsCheck(record).CheckAsync(provider, TestContext.Current.CancellationToken);

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal("The baseline notification channel 'email' is not registered.", problem.Problem);
        }

        [Fact]
        public async Task A_sender_of_an_unregistered_channel_or_of_the_inbox_is_refused_and_its_feature_named()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Scoped<INotificationChannel, SmsChannel>()
                    .Scoped<INotificationChannel, InboxChannel>())));

            Assert.Equal(
                [
                    $"The notification channel sender {Display(typeof(SmsChannel))} registered by the feature 'fixture' delivers the channel 'sms', which is not registered.",
                    $"The notification channel sender {Display(typeof(InboxChannel))} registered by the feature 'fixture' delivers the 'inbox' channel, which the notifier's own rows deliver.",
                ],
                problems.From(Check).Select(static problem => problem.Problem));
        }

        [Fact]
        public async Task Two_senders_of_one_channel_are_refused()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution.Scoped<INotificationChannel, EmailChannel>()))
                .AddFeature(new TestFeature<Tag.B>("other", contribute: static contribution => contribution.Scoped<INotificationChannel, OtherEmailChannel>())));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Equal(
                $"The notification channel 'email' has more than one sender: {Display(typeof(EmailChannel))} (registered by the feature 'fixture') and {Display(typeof(OtherEmailChannel))} (registered by the feature 'other').",
                problem.Problem);
        }

        [Fact]
        public async Task A_sender_that_cannot_be_created_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution.Scoped<INotificationChannel, UncreatableChannel>())));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.StartsWith(
                "The notification channel senders registered by the feature 'fixture' could not be created to read their keys: InvalidOperationException:",
                problem.Problem,
                StringComparison.Ordinal);
        }

        /// <summary>Runs every check over a composition in which one feature, <c>fixture</c>, registers the channels.</summary>
        /// <param name="channels">The channels.</param>
        /// <returns>The problems.</returns>
        private static Task<IReadOnlyList<CompositionProblem>> CheckAsync(params NotificationChannelDescriptor[] channels)
        {
            return TestComposition.CheckAsync(tellma => tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribution =>
            {
                foreach (NotificationChannelDescriptor channel in channels)
                {
                    _ = contribution.NotificationChannel(channel);
                }
            })));
        }

        /// <summary>A nested type's name as problems render it: namespace and declaring types, joined by dots.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The name.</returns>
        private static string Display(Type type)
        {
            return type.FullName!.Replace('+', '.');
        }

        /// <summary>A sender that delivers nothing, under a given key.</summary>
        /// <param name="key">The channel's key.</param>
        public abstract class TestChannel(string key) : INotificationChannel
        {
            /// <inheritdoc />
            public string Key { get; } = key;

            /// <inheritdoc />
            public Task DeliverAsync(IOpenWriteHost host, NotificationRequest request, IReadOnlyList<NotificationRecipient> recipients, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public Task<TestNotificationResult> SendTestAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(TestNotificationResult.NotConfigured);
            }
        }

        /// <summary>A sender of the <c>sms</c> channel.</summary>
        public sealed class SmsChannel() : TestChannel("sms");

        /// <summary>A sender of the <c>email</c> channel.</summary>
        public sealed class EmailChannel() : TestChannel("email");

        /// <summary>A second sender of the <c>email</c> channel.</summary>
        public sealed class OtherEmailChannel() : TestChannel("email");

        /// <summary>A sender of the <c>inbox</c> channel, which has none.</summary>
        public sealed class InboxChannel() : TestChannel("inbox");

        /// <summary>A sender whose dependency nothing registers.</summary>
        /// <param name="missing">The unregistered dependency.</param>
        public sealed class UncreatableChannel(IUnregistered missing) : TestChannel(missing.ToString()!);

        /// <summary>A service no composition registers.</summary>
        public interface IUnregistered;
    }
}
