// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Api;
using Tellma.Core.Composition;

namespace Tellma.Core.AspNetCore.OpenApi
{
    // The OpenAPI document is generated on its first request, and one schema deeper than the
    // document's nesting limit fails all of it. Schema depth follows how deeply a type nests, not how
    // deeply its values do: the exporter writes each type in full the first time it meets it, so a
    // chain of optional members the wire carries shallowly still costs two levels per object member
    // and four per polymorphic level. In Development, where the document exists, this check exports
    // every web route's body and answer the way the generator does and refuses at startup a type
    // whose schema would not fit.

    /// <summary>
    ///     The Development check that every web route's body and answer has a schema within the OpenAPI
    ///     document's nesting limit.
    /// </summary>
    internal sealed class SchemaDepthCheck : IStartupCheck
    {
        /// <summary>The check's name.</summary>
        public const string CheckName = "host.openapi-depth";

        /// <summary>
        ///     The nesting the measuring export allows, well past the document's limit, so a schema
        ///     too deep for the document is measured rather than refused by the exporter.
        /// </summary>
        private const int MeasuringDepth = 4 * WebOpenApi.SchemaMaxDepth;

        /// <summary>
        ///     The exporter's settings, as the generator sets them. The generator writes an any-value
        ///     schema, which the exporter answers as <c>true</c>, as an object, so it is measured as one.
        /// </summary>
        private static readonly JsonSchemaExporterOptions Exporter = new()
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = static (_, schema) => schema is JsonValue ? new JsonObject() : schema,
        };

        /// <inheritdoc />
        public string Name => CheckName;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- Nothing to judge without the route model: the projection's own checks say why ---
            IReadOnlyList<RouteContract> routes;
            try
            {
                routes = services.GetService<IRouteContracts>()?.Routes ?? [];
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                routes = [];
            }

            if (routes.Count == 0)
            {
                return Task.FromResult<IReadOnlyList<CompositionProblem>>([]);
            }

            // --- Each distinct body and answer type, measured once under the document's options ---
            JsonSerializerOptions options = new(SchemaJson.Create(services).SerializerOptions) { MaxDepth = MeasuringDepth };
            List<CompositionProblem> problems = [];
            HashSet<Type> measured = [];
            foreach (Type type in routes.SelectMany(static route => (Type?[])[route.RequestType, route.ResponseType]).OfType<Type>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!measured.Add(type) || Depth(options, type) is not int depth || depth <= WebOpenApi.SchemaMaxDepth)
                {
                    continue;
                }

                problems.Add(new CompositionProblem(
                    CheckName,
                    $"The web surface's type {TypeDisplay.Short(type)} has a schema {(depth > MeasuringDepth ? $"more than {MeasuringDepth}" : depth)} levels deep, past the OpenAPI document's limit of {WebOpenApi.SchemaMaxDepth}, so the Development document would fail.",
                    "Nest the type less deeply: its schema takes two levels for each nested object member and four for each polymorphic one, however shallow its values are."));
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>How deeply a type's schema nests, as the document's generator exports it.</summary>
        /// <param name="options">The document's options, under the measuring depth.</param>
        /// <param name="type">The type.</param>
        /// <returns>
        ///     The schema's nesting, counting each object and array; one past the measuring depth when
        ///     the exporter refuses it as deeper; null for a type without a JSON contract, which the
        ///     contract audit reports.
        /// </returns>
        internal static int? Depth(JsonSerializerOptions options, Type type)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(type);
            try
            {
                _ = options.GetTypeInfo(type);
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                return null;
            }

            JsonNode schema;
            try
            {
                schema = options.GetJsonSchemaAsNode(type, Exporter);
            }
            catch (InvalidOperationException)
            {
                // The contract resolved, so the exporter refused the schema's depth.
                return options.MaxDepth + 1;
            }

            return Nesting(schema);
        }

        /// <summary>How deeply a JSON value nests.</summary>
        /// <param name="node">The value.</param>
        /// <returns>The count of objects and arrays on its deepest path; zero for a scalar.</returns>
        private static int Nesting(JsonNode? node)
        {
            return node switch
            {
                JsonObject members => 1 + members.Select(static member => Nesting(member.Value)).DefaultIfEmpty(0).Max(),
                JsonArray items => 1 + items.Select(Nesting).DefaultIfEmpty(0).Max(),
                _ => 0,
            };
        }
    }
}
