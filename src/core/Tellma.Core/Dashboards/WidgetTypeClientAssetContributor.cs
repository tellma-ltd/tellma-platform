// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Dashboards;

namespace Tellma.Core.Dashboards
{
    // Spec 0035 §4.1: every registered key, so the client's build can check that each type it
    // declares is registered and that each registered type is declared (§2.2's drift check).
    /// <summary>
    ///     Writes the <c>widgetTypes</c> section of the client metadata's Core chunk: the key of every
    ///     widget type this composition registers, in the registry's order.
    /// </summary>
    /// <param name="registry">The widget types this composition registers.</param>
    internal sealed class WidgetTypeClientAssetContributor(IWidgetTypeRegistry registry) : IClientAssetContributor
    {
        /// <summary>The section listing every registered widget type.</summary>
        public const string Section = "widgetTypes";

        /// <inheritdoc />
        public void Contribute(ClientAssetContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- The keys alone, under the platform's JSON rules ---
            string[] keys = [.. registry.Types.Select(static type => type.Key)];
            context.Section(ClientMetadataChunk.Core, Section, writer => JsonSerializer.Serialize(writer, keys, TellmaJson.Options));
        }
    }
}
