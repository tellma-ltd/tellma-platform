// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Localization;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The string packs: one per offered language and one for English, each the merged entries of
    ///     every composed feature assembly's shared resources in that language, keys verbatim; a key
    ///     two assemblies declare refused; server-only resources never read.
    /// </summary>
    public sealed class StringPackComposerTests
    {
        /// <summary>The version every in-memory composition's packs carry.</summary>
        private const string Version = "4.2.0+abc";

        [Fact]
        public void Each_language_gets_one_pack_of_its_own_entries_from_every_source_with_the_keys_verbatim()
        {
            StringPackComposer composer = new(
                [
                    Source("Acme.Resources.Strings", ("", "Acme_Order", "Order"), ("", "Acme_Order_Plural", "Orders"), ("ar", "Acme_Order", "طلب")),
                    Source("Beta.Resources.Strings", ("", "Beta_Item", "Item")),
                ],
                ["ar", "am"],
                Version);

            // English first, though not offered, then the offered languages in declaration order.
            Assert.Equal(["en", "ar", "am"], composer.Packs.Select(static pack => pack.Language));
            Assert.All(composer.Packs, static pack => Assert.Equal(Version, pack.Version));

            // A language holds its own entries only: English is the fallback, never merged in.
            Assert.Equal(
                new Dictionary<string, string> { ["Acme_Order"] = "Order", ["Acme_Order_Plural"] = "Orders", ["Beta_Item"] = "Item" },
                Entries(composer.Get("en")!));
            Assert.Equal(new Dictionary<string, string> { ["Acme_Order"] = "طلب" }, Entries(composer.Get("ar")!));
            Assert.Empty(Entries(composer.Get("am")!));
            Assert.Empty(composer.Conflicts);
        }

        [Fact]
        public void A_pack_is_one_flat_object_with_its_keys_in_ordinal_order_spelled_as_the_platforms_json()
        {
            StringPackComposer composer = new([Source("Acme.Resources.Strings", ("", "b", "Bee"), ("", "B", "<B>"), ("", "a", "مركز"))], [], Version);

            // Every script as itself, the HTML-sensitive characters escaped.
            Assert.Equal(/*lang=json,strict*/ """{"B":"\u003CB\u003E","a":"مركز","b":"Bee"}""", System.Text.Encoding.UTF8.GetString(composer.Get("en")!.Json.Span));
        }

        [Fact]
        public void A_pack_is_found_without_regard_to_case_and_only_for_english_and_the_offered_languages()
        {
            StringPackComposer composer = new([Source("Acme.Resources.Strings", ("", "Acme_Order", "Order"))], ["ar"], Version);

            Assert.Same(composer.Get("ar"), composer.Get("AR"));
            Assert.NotNull(composer.Get("EN"));
            Assert.Null(composer.Get("fr"));
            Assert.Null(composer.Get("ar-SA"));
            Assert.Null(composer.Strings("fr"));
            _ = Assert.Throws<ArgumentNullException>(() => composer.Get(null!));
        }

        [Fact]
        public void The_neutral_keys_are_the_english_dictionarys()
        {
            StringPackComposer composer = new(
                [Source("Acme.Resources.Strings", ("", "Acme_Order", "Order"), ("ar", "Acme_Order_Plural", "طلبات"))],
                ["ar"],
                Version);

            Assert.True(composer.Declares("Acme_Order"));
            Assert.False(composer.Declares("acme_order"));
            Assert.False(composer.Declares("Acme_Order_Plural"));
            Assert.False(composer.Declares(null!));
        }

        [Fact]
        public void A_key_two_sources_declare_is_one_conflict_and_the_earlier_source_keeps_it()
        {
            StringPackComposer composer = new(
                [
                    Source("Acme.Resources.Strings", ("", "Shared_Invoice", "Invoice"), ("ar", "Shared_Invoice", "فاتورة")),
                    Source("Beta.Resources.Strings", ("", "Shared_Invoice", "Bill"), ("ar", "Shared_Invoice", "كشف")),
                ],
                ["ar"],
                Version);

            StringKeyConflict conflict = Assert.Single(composer.Conflicts);
            Assert.Equal(new StringKeyConflict("Shared_Invoice", "Acme.Resources.Strings", "Beta.Resources.Strings"), conflict);
            Assert.Equal("Invoice", composer.English["Shared_Invoice"]);
            Assert.Equal("فاتورة", composer.Strings("ar")!["Shared_Invoice"]);
        }

        [Fact]
        public void A_translation_of_another_sources_key_is_a_conflict_and_never_merged()
        {
            StringPackComposer composer = new(
                [
                    Source("Core.Resources.Strings", ("", "Code", "Code")),
                    Source("Beta.Resources.Strings", ("", "Beta_Item", "Item"), ("am", "Code", "ኮድ"), ("am", "Beta_Item", "ዕቃ")),
                ],
                ["am"],
                Version);

            StringKeyConflict conflict = Assert.Single(composer.Conflicts);
            Assert.Equal(new StringKeyConflict("Code", "Core.Resources.Strings", "Beta.Resources.Strings"), conflict);
            Assert.Equal(new Dictionary<string, string> { ["Beta_Item"] = "ዕቃ" }, Entries(composer.Get("am")!));
        }

        [Fact]
        public void A_translation_of_a_key_no_neutral_set_declares_is_left_out_of_the_pack()
        {
            StringPackComposer composer = new(
                [Source("Acme.Resources.Strings", ("", "Acme_Order", "Order"), ("ar", "Acme_Order", "طلب"), ("ar", "Acme_Stray", "شارد"))],
                ["ar"],
                Version);

            Assert.Equal(new Dictionary<string, string> { ["Acme_Order"] = "طلب" }, Entries(composer.Get("ar")!));
            Assert.Empty(composer.Conflicts);
        }

        [Fact]
        public async Task The_startup_check_refuses_a_key_two_assemblies_declare()
        {
            StringPackComposer composer = new(
                [Source("Acme.Resources.Strings", ("", "Shared_Invoice", "Invoice")), Source("Beta.Resources.Strings", ("", "Shared_Invoice", "Bill"))],
                [],
                Version);
            await using ServiceProvider services = new ServiceCollection().AddSingleton(composer).BuildServiceProvider();

            CompositionProblem problem = Assert.Single(await new StringPackCheck().CheckAsync(services, TestContext.Current.CancellationToken));

            Assert.Equal("core.string-pack", problem.Source);
            Assert.Contains("'Shared_Invoice'", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("Acme.Resources.Strings", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("Beta.Resources.Strings", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_platforms_own_composition_composes_without_conflict()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .Languages(["en", "ar"])
                .AddFeature(new TestFeature<Tag.A>("test.strings")));

            Assert.Empty(problems.From(CoreCheckNames.StringPack));
        }

        [Fact]
        public async Task The_composed_packs_merge_every_feature_assemblys_shared_strings_and_leave_out_server_strings()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .Languages(["en", "ar"])
                .AddFeature(new TestFeature<Tag.A>("test.strings")))
                .BuildServiceProvider(validateScopes: true);
            IStringPackProvider packs = provider.GetRequiredService<IStringPackProvider>();

            // --- Core's keys and this assembly's in one pack, the server-only ones in none ---
            Dictionary<string, string> english = Entries(packs.Get("en")!);
            Assert.Equal("Activate", english["Action_Activate"]);
            Assert.Equal("Contraption", english["Fixture_Widget"]);
            Assert.DoesNotContain("Fixture_ServerOnly", english.Keys);
            Assert.Equal("أداة", Entries(packs.Get("ar")!)["Fixture_Widget"]);
            Assert.Null(packs.Get("am"));

            // --- The distribution's version, and one composer behind both seams ---
            Assert.Equal(provider.GetRequiredService<DeploymentVersions>().DistributionVersion, packs.Get("en")!.Version);
            Assert.Same(packs, provider.GetRequiredService<INeutralStringKeys>());
        }

        [Fact]
        public async Task The_contributor_writes_every_pack_into_the_client_bundle()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma.Languages(["ar", "en"]))
                .BuildServiceProvider(validateScopes: true);
            ClientAssetContext context = new(provider);

            provider.GetServices<IClientAssetContributor>().OfType<StringPackClientAssetContributor>().Single().Contribute(context);

            Assert.Equal(["en", "ar"], context.StringPacks.Select(static pack => pack.Language));
            Assert.Same(provider.GetRequiredService<IStringPackProvider>().Get("ar"), context.StringPacks[1]);
        }

        /// <summary>An in-memory source: entries by culture name, empty for the neutral set.</summary>
        /// <param name="name">The source's name.</param>
        /// <param name="entries">The entries.</param>
        /// <returns>The source.</returns>
        private static StringResourceSource Source(string name, params (string Culture, string Key, string Value)[] entries)
        {
            return new StringResourceSource(name, culture =>
            {
                var set = entries
                    .Where(entry => entry.Culture == culture.Name)
                    .ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.Ordinal);
                return set.Count == 0 ? null : set;
            });
        }

        /// <summary>A pack's entries.</summary>
        /// <param name="pack">The pack.</param>
        /// <returns>The entries by key.</returns>
        private static Dictionary<string, string> Entries(StringPack pack)
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(pack.Json.Span)!;
        }
    }
}
