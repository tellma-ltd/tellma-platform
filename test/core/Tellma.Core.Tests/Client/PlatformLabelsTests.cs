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
    ///     without a resource that declares it, so a distribution's build warns only about its own
    ///     labels, never about the platform's.
    /// </summary>
    public sealed class PlatformLabelsTests
    {
        /// <summary>The suffix of every member that carries a label's fallback; its key's member ends in <c>LabelKey</c>.</summary>
        private const string FallbackSuffix = "abelFallback";

        [Fact]
        public void The_platform_alone_declares_every_label_its_client_metadata_names()
        {
            using ServiceProvider services = TestComposition
                .Compose(static _ => { }, TestComposition.Configuration(TestComposition.Sound(("Tellma:DisplayName", "Acme ERP"))))
                .BuildServiceProvider(validateScopes: true);

            // --- Every section every contributor writes, as a client build reads them ---
            ClientAssetContext context = new(services);
            foreach (IClientAssetContributor contributor in services.GetServices<IClientAssetContributor>())
            {
                contributor.Contribute(context);
            }

            // --- Each label key written with a fallback, which no declared resource resolves ---
            SortedSet<string> missing = new(StringComparer.Ordinal);
            foreach (ClientMetadataSection section in context.Sections)
            {
                using var document = JsonDocument.Parse(section.Json);
                Collect(document.RootElement, missing);
            }

            Assert.True(missing.Count == 0, $"The platform writes {missing.Count} label keys that no resource declares: {string.Join(", ", missing)}");
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
