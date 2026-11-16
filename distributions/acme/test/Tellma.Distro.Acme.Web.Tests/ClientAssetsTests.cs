// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
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
            using AcmeEmit second = await AcmeEmit.RunAsync(TestContext.Current.CancellationToken);

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

        /// <summary>An emitted asset's name: its kind, a language for a string pack, and a 12-character Crockford hash.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex("^assets/(core|entities|contracts|strings-[a-z]{2,3}(-[A-Za-z0-9]+)*)-[0-9A-HJKMNP-TV-Z]{12}\\.json$")]
        private static partial Regex HashedName();
    }
}
