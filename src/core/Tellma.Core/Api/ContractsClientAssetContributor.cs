// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Client;

namespace Tellma.Core.Api
{
    // The chunk's sections are spec 0016 §3.10's and spec 0024 §3.4's.

    /// <summary>
    ///     Writes the Contracts chunk of the client metadata from the route model: <c>fingerprints</c>,
    ///     every projected route's template to its fingerprint; <c>long</c>, the routes that run under
    ///     the long timeout; and <c>idempotent</c>, the routes a client retries. Routes are in mapping
    ///     order.
    /// </summary>
    /// <param name="routes">The route model.</param>
    internal sealed class ContractsClientAssetContributor(IRouteContracts routes) : IClientAssetContributor
    {
        /// <summary>The section mapping each route to its fingerprint.</summary>
        public const string FingerprintsSection = "fingerprints";

        /// <summary>The section listing the long routes.</summary>
        public const string LongSection = "long";

        /// <summary>The section listing the idempotent routes.</summary>
        public const string IdempotentSection = "idempotent";

        /// <inheritdoc />
        public void Contribute(ClientAssetContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            IReadOnlyList<RouteContract> all = routes.Routes;

            // --- Template to fingerprint ---
            context.Section(ClientMetadataChunk.Contracts, FingerprintsSection, writer =>
            {
                writer.WriteStartObject();
                foreach (RouteContract route in all)
                {
                    writer.WriteString(route.Template, route.Fingerprint);
                }

                writer.WriteEndObject();
            });

            // --- The two route lists ---
            context.Section(ClientMetadataChunk.Contracts, LongSection, writer => WriteTemplates(writer, all.Where(static route => route.Long)));
            context.Section(ClientMetadataChunk.Contracts, IdempotentSection, writer => WriteTemplates(writer, all.Where(static route => route.Idempotent)));
        }

        /// <summary>Writes the templates of some routes as an array.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="routes">The routes.</param>
        private static void WriteTemplates(Utf8JsonWriter writer, IEnumerable<RouteContract> routes)
        {
            writer.WriteStartArray();
            foreach (RouteContract route in routes)
            {
                writer.WriteStringValue(route.Template);
            }

            writer.WriteEndArray();
        }
    }
}
