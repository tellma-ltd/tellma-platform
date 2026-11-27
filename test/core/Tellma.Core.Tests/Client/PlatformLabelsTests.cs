// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Client
{
    /// <summary>
    ///     The platform's own labels: the client metadata the platform alone writes names no label key
    ///     without a resource that declares it, and each of its entities declares a plural of its own,
    ///     so a distribution's build warns only about its own labels, never about the platform's.
    /// </summary>
    public sealed class PlatformLabelsTests
    {
        /// <summary>The suffix of every member that carries a label's fallback; its key's member ends in <c>LabelKey</c>.</summary>
        private const string FallbackSuffix = "abelFallback";

        /// <summary>
        ///     The platform's entities whose singular label doubles as their plural: one settings row per
        ///     tenant, and a stamp per user read only beside that user.
        /// </summary>
        private static readonly HashSet<string> SingularAsPlural = new(StringComparer.Ordinal) { "core.Settings", "core.UserStamp" };

        [Fact]
        public void The_platform_alone_declares_every_label_its_client_metadata_names()
        {
            // --- Each label key written with a fallback, which no declared resource resolves ---
            SortedSet<string> missing = new(StringComparer.Ordinal);
            foreach (JsonElement root in PlatformSections())
            {
                Collect(root, missing);
            }

            Assert.True(missing.Count == 0, $"The platform writes {missing.Count} label keys that no resource declares: {string.Join(", ", missing)}");
        }

        [Fact]
        public void Every_platform_entity_declares_its_plural_label()
        {
            // A plural resolves to the singular when its own key is missing and writes no fallback, so
            // the walk above cannot see it: each entity's plural key must be its own.
            SortedSet<string> missing = new(StringComparer.Ordinal);
            foreach (JsonElement root in PlatformSections())
            {
                CollectSingularPlurals(root, missing);
            }

            Assert.True(missing.Count == 0, $"The platform's entities fall back to their singular for their plural label: {string.Join(", ", missing)}");
        }

        /// <summary>Every section every client asset contributor writes for the platform alone, parsed as a client build reads it.</summary>
        /// <returns>Each section's root element, detached from its document.</returns>
        private static List<JsonElement> PlatformSections()
        {
            using ServiceProvider services = TestComposition
                .Compose(static _ => { }, TestComposition.Configuration(TestComposition.Sound(("Tellma:DisplayName", "Acme ERP"))))
                .BuildServiceProvider(validateScopes: true);

            // --- Every section every contributor writes ---
            ClientAssetContext context = new(services);
            foreach (IClientAssetContributor contributor in services.GetServices<IClientAssetContributor>())
            {
                contributor.Contribute(context);
            }

            // Each root is cloned so it outlives its document.
            List<JsonElement> roots = [];
            foreach (ClientMetadataSection section in context.Sections)
            {
                using var document = JsonDocument.Parse(section.Json);
                roots.Add(document.RootElement.Clone());
            }

            return roots;
        }

        /// <summary>
        ///     Collects, at any depth, the name of every platform entity whose plural label key is not
        ///     its label key's own plural, other than the entities whose singular doubles as their plural.
        /// </summary>
        /// <param name="element">The element to walk.</param>
        /// <param name="missing">The names found so far.</param>
        private static void CollectSingularPlurals(JsonElement element, SortedSet<string> missing)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                // An entity's summary names it beside its label and plural keys.
                if (element.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String
                    && element.TryGetProperty("labelKey", out JsonElement labelKey) && labelKey.ValueKind == JsonValueKind.String
                    && element.TryGetProperty("pluralLabelKey", out JsonElement pluralLabelKey) && pluralLabelKey.ValueKind == JsonValueKind.String)
                {
                    string entity = name.GetString()!;
                    string expected = labelKey.GetString() + "_Plural";
                    if (entity.StartsWith("core.", StringComparison.Ordinal) && !SingularAsPlural.Contains(entity)
                        && !string.Equals(pluralLabelKey.GetString(), expected, StringComparison.Ordinal))
                    {
                        _ = missing.Add($"{entity} ({expected})");
                    }
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    CollectSingularPlurals(property.Value, missing);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    CollectSingularPlurals(item, missing);
                }
            }
        }

        /// <summary>Collects, at any depth, the key of every label written with a fallback.</summary>
        /// <param name="element">The element to walk.</param>
        /// <param name="missing">The keys found so far.</param>
        private static void Collect(JsonElement element, SortedSet<string> missing)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    // A fallback sits beside its key: labelFallback by labelKey, resourceLabelFallback by resourceLabelKey.
                    if (property.Name.EndsWith(FallbackSuffix, StringComparison.Ordinal))
                    {
                        string keyName = property.Name[..^"Fallback".Length] + "Key";
                        _ = missing.Add(element.TryGetProperty(keyName, out JsonElement key) && key.ValueKind == JsonValueKind.String
                            ? key.GetString()!
                            : $"({property.Name} {property.Value.GetRawText()})");
                    }
                    else
                    {
                        Collect(property.Value, missing);
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Collect(item, missing);
                }
            }
        }
    }
}
