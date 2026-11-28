// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Localization;
using Tellma.Core.Tests.Infrastructure;
using Tellma.Testing.Resources;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The labels of the composed platform model's notification settings: every registered
    ///     notification category, notification type and notification channel has its label in Core's
    ///     shared resources, in English and in Arabic, so the Notifications section of My settings
    ///     never shows a bare key. The resource audit only accepts these keys where a file declares
    ///     them; this check asks for them.
    /// </summary>
    public sealed class NotificationLabelsTests
    {
        /// <summary>Core's shared resources, whose labels the browser application reads.</summary>
        private static readonly string Neutral = RepositoryRoot.Combine("src", "core", "Tellma.Core", "Resources", "Strings.resx");

        [Fact]
        public async Task Every_registered_category_type_and_channel_has_its_label_in_english_and_in_arabic()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            INotificationTypeRegistry types = provider.GetRequiredService<INotificationTypeRegistry>();
            INotificationChannelRegistry channels = provider.GetRequiredService<INotificationChannelRegistry>();

            // --- The label keys the section reads, by the same conventions as the server's ---
            string[] keys =
            [
                .. types.Categories.Select(static category => "NotificationCategory_" + category),
                .. types.All.Select(static type => StringKeyConventions.FromDottedKey("NotificationType_", type.Key)),
                .. channels.All.Select(static channel => StringKeyConventions.FromDottedKey("NotificationChannel_", channel.Key)),
            ];
            Assert.NotEmpty(types.Categories);
            Assert.NotEmpty(channels.All);

            // --- Each file must declare every key ---
            string arabic = ResxFile.Satellites(Neutral).Single(static satellite => satellite.Culture == "ar").Path;
            IReadOnlyDictionary<string, string> english = ResxFile.Read(Neutral);
            IReadOnlyDictionary<string, string> translated = ResxFile.Read(arabic);
            string[] missing =
            [
                .. keys.Where(key => !english.ContainsKey(key)).Select(static key => "Strings.resx: " + key),
                .. keys.Where(key => !translated.ContainsKey(key)).Select(static key => "Strings.ar.resx: " + key),
            ];

            Assert.True(missing.Length == 0, "Labels missing:" + Environment.NewLine + string.Join(Environment.NewLine, missing));
        }
    }
}
