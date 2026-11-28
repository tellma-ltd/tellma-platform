// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Composition;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;

namespace Tellma.Distro.Acme.Web.Tests
{
    /// <summary>
    ///     Acme's client metadata as its client build receives it: the same names and bytes from every
    ///     emit, the version the web host reports, and every section the client reads, an empty list
    ///     where nothing contributes one.
    /// </summary>
    public sealed partial class ClientAssetsTests
    {
        /// <summary>The Core chunk's sections the client reads as lists.</summary>
        private static readonly string[] CoreListSections =
        [
            "features", "languages", "calendars", "entities", "securables", "settingKeys", "settingCategories",
            "notificationTypes", "notificationChannels", "clientEvents", "jobHandlers", "widgetTypes", "codedReports",
        ];

        /// <summary>The Entities chunk's sections, both lists.</summary>
        private static readonly string[] EntitiesListSections = ["entities", "enums"];

        [Fact]
        public async Task Two_emits_write_the_same_names_and_bytes()
        {
            using AcmeEmit first = await AcmeEmit.RunAsync(TestContext.Current.CancellationToken);

            // The second emit runs under a culture whose separators and calendar differ, as a build
            // machine in another locale would: the output must not depend on it.
            using AcmeEmit second = await EmitUnderAsync(CultureInfo.GetCultureInfo("ar-SA"));

            SortedDictionary<string, byte[]> written = first.Files();
            SortedDictionary<string, byte[]> again = second.Files();
            Assert.Equal(written.Keys, again.Keys);
            Assert.All(written, file => Assert.True(file.Value.AsSpan().SequenceEqual(again[file.Key]), $"{file.Key} differs between two emits."));

            // Each chunk and string pack is named after the hash of its bytes, so the names are stable too.
            Assert.All(
                written.Keys.Where(static name => name.StartsWith("assets/", StringComparison.Ordinal)),
                static name => Assert.Matches(HashedName(), name));
        }

        [Fact]
        public async Task The_metadata_carries_the_version_the_web_host_reports()
        {
            using AcmeEmit emit = await AcmeEmit.RunAsync(TestContext.Current.CancellationToken);
            using var core = JsonDocument.Parse(emit.Chunk("core-"));
            string? emitted = core.RootElement.GetProperty("distribution").GetProperty("version").GetString();

            // The web host stamps this version on every response as Tellma-Build.
            await using WebApplication app = AcmeWebApplication.Build("Production", AcmeWebApplication.ProductionSettings);
            Assert.Equal(app.Services.GetRequiredService<DeploymentVersions>().DistributionVersion, emitted);

            // Both are the composition library's, which carries Acme's one Version.
            Assembly library = typeof(AcmeComposition).Assembly;
            Assert.Equal(library.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion, emitted);

            // Acme's own feature is composed last, with the same version.
            JsonElement acme = core.RootElement.GetProperty("features").EnumerateArray().Last();
            Assert.Equal(AcmeFeature.FeatureName, acme.GetProperty("key").GetString());
            Assert.Equal(emitted, acme.GetProperty("version").GetString());
        }

        [Fact]
        public async Task Every_section_the_client_reads_is_a_list_and_one_nothing_contributes_is_empty()
        {
            using AcmeEmit emit = await AcmeEmit.RunAsync(TestContext.Current.CancellationToken);

            // --- The sections Acme's contributors write, from the composition the emit runs ---
            await using WebApplication app = AcmeWebApplication.Build("Production", AcmeWebApplication.ProductionSettings);
            ClientAssetContext context = new(app.Services);
            foreach (IClientAssetContributor contributor in app.Services.GetServices<IClientAssetContributor>())
            {
                contributor.Contribute(context);
            }

            // --- Each chunk: every list present, and empty when no contributor writes it ---
            foreach ((ClientMetadataChunk chunk, string prefix, string[] sections) in new[]
            {
                (ClientMetadataChunk.Core, "core-", CoreListSections),
                (ClientMetadataChunk.Entities, "entities-", EntitiesListSections),
            })
            {
                HashSet<string> contributed = [.. context.Sections.Where(section => section.Chunk == chunk).Select(static section => section.Name)];
                using var document = JsonDocument.Parse(emit.Chunk(prefix));
                foreach (string name in sections)
                {
                    Assert.True(document.RootElement.TryGetProperty(name, out JsonElement section), $"The {chunk} chunk has no '{name}'.");
                    Assert.Equal(JsonValueKind.Array, section.ValueKind);
                    if (!contributed.Contains(name))
                    {
                        Assert.True(section.GetArrayLength() == 0, $"The {chunk} chunk's '{name}', which nothing contributes, is not empty.");
                    }
                }
            }
        }

        [Fact]
        public async Task The_emit_writes_the_entity_model_and_its_declarations()
        {
            using AcmeEmit emit = await AcmeEmit.RunAsync(TestContext.Current.CancellationToken);

            // --- The summaries in the Core chunk, the full model and the enums in the Entities chunk ---
            using var core = JsonDocument.Parse(emit.Chunk("core-"));
            using var entities = JsonDocument.Parse(emit.Chunk("entities-"));
            Assert.Contains(core.RootElement.GetProperty("entities").EnumerateArray(), static summary => summary.GetProperty("name").GetString() == "core.User");
            Assert.Contains(entities.RootElement.GetProperty("entities").EnumerateArray(), static entity => entity.GetProperty("name").GetString() == "core.User");
            Assert.Contains(entities.RootElement.GetProperty("enums").EnumerateArray(), static item => item.GetProperty("name").GetString() == "core.TimeZone");
            Assert.Equal(
                core.RootElement.GetProperty("entities").GetArrayLength(),
                entities.RootElement.GetProperty("entities").GetArrayLength());

            // --- The declarations beside the assets, registering every entity ---
            SortedDictionary<string, byte[]> files = emit.Files();
            Assert.True(files.TryGetValue("tellma-entities.d.ts", out byte[]? written), "The emit wrote no tellma-entities.d.ts.");
            string declarations = Encoding.UTF8.GetString(written);
            Assert.Contains("declare module '@tellma/core' {", declarations, StringComparison.Ordinal);
            Assert.Contains("    'core.User': CoreUser;", declarations, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_emit_lists_the_registered_widget_types()
        {
            using AcmeEmit emit = await AcmeEmit.RunAsync(TestContext.Current.CancellationToken);

            // --- The keys alone, the platform's first: Acme registers none of its own yet ---
            using var core = JsonDocument.Parse(emit.Chunk("core-"));
            Assert.Equal(["core.assignments-to-me", "core.assignments-by-me", "core.recents"], core.RootElement.GetProperty("widgetTypes").EnumerateArray().Select(static key => key.GetString()));
        }

        /// <summary>Runs an emit under a culture, restoring the caller's cultures after it.</summary>
        /// <param name="culture">The culture, both for formatting and for resources.</param>
        /// <returns>The emit.</returns>
        private static async Task<AcmeEmit> EmitUnderAsync(CultureInfo culture)
        {
            CultureInfo current = CultureInfo.CurrentCulture;
            CultureInfo currentUi = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                return await AcmeEmit.RunAsync(TestContext.Current.CancellationToken);
            }
            finally
            {
                CultureInfo.CurrentCulture = current;
                CultureInfo.CurrentUICulture = currentUi;
            }
        }

        /// <summary>An emitted asset's name: its kind, a language for a string pack, and a 12-character Crockford hash.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex("^assets/(core|entities|contracts|strings-[a-z]{2,3}(-[A-Za-z0-9]+)*)-[0-9A-HJKMNP-TV-Z]{12}\\.json$")]
        private static partial Regex HashedName();
    }
}
