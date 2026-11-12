// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     The platform's own sections of the client metadata's Core chunk: <c>distribution</c> (the
    ///     slug, the configured display name and copyright, and the distribution's and platform's
    ///     versions), <c>features</c> (every composed feature with its assembly's version, in
    ///     realisation order) and <c>routing</c> (the reserved route words).
    /// </summary>
    /// <param name="record">The realised composition.</param>
    /// <param name="versions">The distribution's and platform's versions.</param>
    internal sealed class CoreClientAssetContributor(TellmaCompositionRecord record, DeploymentVersions versions) : IClientAssetContributor
    {
        /// <summary>The configuration key of the distribution's display name.</summary>
        public const string DisplayNameKey = "Tellma:DisplayName";

        /// <summary>The configuration key of the distribution's copyright line.</summary>
        public const string CopyrightKey = "Tellma:Copyright";

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">The display name is not configured.</exception>
        public void Contribute(ClientAssetContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- distribution ---
            string displayName = record.Configuration[DisplayNameKey] is { Length: > 0 } configured && !string.IsNullOrWhiteSpace(configured)
                ? configured
                : throw new InvalidOperationException($"{DisplayNameKey} is not set; the client metadata names the distribution by it.");

            string? copyright = record.Configuration[CopyrightKey] is { Length: > 0 } line && !string.IsNullOrWhiteSpace(line) ? line : null;
            context.Section(ClientMetadataChunk.Core, "distribution", writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("slug", record.Slug);
                writer.WriteString("displayName", displayName);
                writer.WriteString("copyright", copyright);
                writer.WriteString("version", versions.DistributionVersion);
                writer.WriteString("platformVersion", versions.PlatformVersion);
                writer.WriteEndObject();
            });

            // --- features: each composed feature and its assembly's version, in realisation order ---
            context.Section(ClientMetadataChunk.Core, "features", writer =>
            {
                writer.WriteStartArray();
                foreach (ITellmaFeature feature in record.Features)
                {
                    writer.WriteStartObject();
                    writer.WriteString("key", feature.Name);
                    writer.WriteString("version", DeploymentVersionsReader.InformationalVersion(feature.GetType().Assembly));
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            });

            // --- routing: the reserved route words, in the order they are declared ---
            context.Section(ClientMetadataChunk.Core, "routing", static writer =>
            {
                writer.WriteStartObject();
                WriteList(writer, "serverFirstSegments", TellmaRoutes.ServerFirstSegments);
                WriteList(writer, "serverTenantSegments", TellmaRoutes.ServerTenantSegments);
                WriteList(writer, "spaFirstSegments", TellmaRoutes.SpaFirstSegments);
                WriteList(writer, "spaTenantSegments", TellmaRoutes.SpaTenantSegments);
                writer.WriteEndObject();
            });
        }

        /// <summary>Writes a named array of strings.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="name">The member's name.</param>
        /// <param name="values">The strings, in order.</param>
        private static void WriteList(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
        {
            writer.WriteStartArray(name);
            foreach (string value in values)
            {
                writer.WriteStringValue(value);
            }

            writer.WriteEndArray();
        }
    }
}
