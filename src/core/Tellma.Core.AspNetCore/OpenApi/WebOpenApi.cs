// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.Crud;

namespace Tellma.Core.AspNetCore.OpenApi
{
    // Spec 0016 §12.1: /openapi/web.json, OpenAPI 3.1, in Development alone, for the coding agent
    // building the SPA or its tests. The schemas are the real wire types under the host's JSON
    // options; the transformers add what the types alone cannot say: the securable an operation
    // requires, which members a save never takes, what a navigation's related entity carries, the
    // tag of each operation's resource, and the string-or-number encodings of decimal and long.
    //
    // The schema generator reads the host's JSON options with their nesting limit raised to the
    // document's own (SchemaMaxDepth): spec 0016 §3.1's limit of 16 bounds the documents the wire
    // reads and writes, and a schema nests deeper than the documents it describes.

    /// <summary>The web surface's OpenAPI document, served in Development alone.</summary>
    internal static class WebOpenApi
    {
        /// <summary>The document's name, which its route carries.</summary>
        public const string DocumentName = "web";

        /// <summary>Why the document's endpoint serves no tenant.</summary>
        public const string TenantlessReason = "openapi";

        /// <summary>The document's route.</summary>
        public const string Route = "/openapi/{documentName}.json";

        /// <summary>The extension naming an operation's securable.</summary>
        public const string SecurableExtension = "x-tellma-securable";

        /// <summary>The extension marking a member only a create sets.</summary>
        public const string WriteOnceExtension = "x-tellma-write-once";

        /// <summary>The extension marking a member the service computes on every save.</summary>
        public const string DerivedExtension = "x-tellma-derived";

        /// <summary>The extension listing, per navigation, the members its related entity carries.</summary>
        public const string RelatedSelectExtension = "x-tellma-related-select";

        /// <summary>The format of a decimal, written as a string or read as a number.</summary>
        public const string DecimalFormat = "decimal";

        /// <summary>The format of a long, written as a string or read as a number.</summary>
        public const string Int64Format = "int64";

        /// <summary>
        ///     The deepest nesting a schema may have while the document is generated, in place of the
        ///     wire's 16, which still bounds every request and response body.
        /// </summary>
        /// <remarks>
        ///     A schema nests deeper than the values it describes: two levels for each object a value
        ///     nests (<c>properties</c>, then the member's schema) and four for each polymorphic one
        ///     (<c>anyOf</c>, its entry, <c>properties</c>, the member). The deepest value the wire
        ///     carries therefore needs at most 65 levels, and this limit leaves about as much again for the
        ///     generator's own wrapping. It guards the generator's recursion; nothing is read from a
        ///     caller under it.
        /// </remarks>
        public const int SchemaMaxDepth = 128;

        /// <summary>Registers the document and its transformers.</summary>
        /// <param name="services">The host's services.</param>
        public static void Register(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);
            _ = services.AddOpenApi(DocumentName, static options =>
            {
                options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;

                // --- The web surface's endpoints alone: no tenantless, blob, hub or public API endpoint ---
                options.ShouldInclude = static description => description.ActionDescriptor.EndpointMetadata
                    .OfType<TellmaGroupMetadata>()
                    .Any(static group => group.Group == TellmaGroup.Web);
                _ = options.AddSchemaTransformer(static (schema, context, _) =>
                {
                    ScalarSchemas.Apply(schema, context.JsonTypeInfo.Type);
                    return Task.CompletedTask;
                });
                _ = options.AddOperationTransformer(static (operation, context, _) =>
                {
                    Operations.Apply(operation, context.Description, context.Document);
                    return Task.CompletedTask;
                });
                _ = options.AddDocumentTransformer(static (document, context, _) =>
                {
                    Operations.PruneTags(document);
                    EntitySchemas.Apply(document, context.ApplicationServices);
                    return Task.CompletedTask;
                });
            });

            // --- The schema generator under the document's own nesting limit ---
            SchemaJson.Register(services, DocumentName);
        }

        /// <summary>Maps the document's endpoint.</summary>
        /// <param name="routes">The application's routes.</param>
        /// <returns>The endpoint, for the caller to stamp.</returns>
        public static IEndpointConventionBuilder Map(IEndpointRouteBuilder routes)
        {
            ArgumentNullException.ThrowIfNull(routes);
            return routes.MapOpenApi(Route);
        }

        /// <summary>A JSON value as an extension.</summary>
        /// <param name="node">The value.</param>
        /// <returns>The extension.</returns>
        internal static JsonNodeExtension Extension(JsonNode node)
        {
            return new JsonNodeExtension(node);
        }
    }

    /// <summary>The scalar encodings the platform's converters write, which the schema exporter cannot see.</summary>
    internal static class ScalarSchemas
    {
        /// <summary>
        ///     Rewrites a decimal's schema as a string or a number of format <c>decimal</c>, and a long's
        ///     as a string or an integer of format <c>int64</c>: each is written as a string, so a client
        ///     that reads numbers as doubles loses no digit, and read from either.
        /// </summary>
        /// <param name="schema">The schema.</param>
        /// <param name="type">The type it describes.</param>
        public static void Apply(OpenApiSchema schema, Type type)
        {
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(type);
            Type? underlying = Nullable.GetUnderlyingType(type);
            Type scalar = underlying ?? type;
            (JsonSchemaType kind, string format) = scalar == typeof(decimal) ? (JsonSchemaType.Number, WebOpenApi.DecimalFormat)
                : scalar == typeof(long) ? (JsonSchemaType.Integer, WebOpenApi.Int64Format)
                : (default, string.Empty);
            if (format.Length == 0)
            {
                return;
            }

            // --- A nullable value keeps its null ---
            bool nullable = underlying is not null || schema.Type?.HasFlag(JsonSchemaType.Null) == true;
            schema.Type = JsonSchemaType.String | kind | (nullable ? JsonSchemaType.Null : 0);
            schema.Format = format;
            schema.Pattern = null;
        }
    }

    /// <summary>What an operation's endpoint states: its securable and its resource's tag.</summary>
    internal static class Operations
    {
        /// <summary>Stamps an operation with its securable and tags it by its resource segment.</summary>
        /// <param name="operation">The operation.</param>
        /// <param name="description">The endpoint's description.</param>
        /// <param name="document">The document being built.</param>
        public static void Apply(OpenApiOperation operation, ApiDescription description, OpenApiDocument? document)
        {
            ArgumentNullException.ThrowIfNull(operation);
            ArgumentNullException.ThrowIfNull(description);
            IList<object> metadata = description.ActionDescriptor.EndpointMetadata;

            // --- The securable the endpoint requires; a member endpoint requires none ---
            if (metadata.OfType<SecurableEndpointMetadata>().LastOrDefault() is SecurableEndpointMetadata securable)
            {
                operation.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
                operation.Extensions[WebOpenApi.SecurableExtension] = WebOpenApi.Extension(new JsonObject
                {
                    ["resource"] = securable.Resource,
                    ["action"] = securable.Action,
                });
            }

            // --- One tag: the stack's or surface's resource segment, or the service's route ---
            if (metadata.OfType<ProjectedRouteMetadata>().LastOrDefault() is ProjectedRouteMetadata projected && document is not null)
            {
                document.Tags ??= new HashSet<OpenApiTag>();
                if (!document.Tags.Any(tag => string.Equals(tag.Name, projected.Segment, StringComparison.Ordinal)))
                {
                    _ = document.Tags.Add(new OpenApiTag { Name = projected.Segment });
                }

                operation.Tags = new HashSet<OpenApiTagReference> { new(projected.Segment, document) };
            }
        }

        /// <summary>Drops the tags no operation carries, such as the application's default, and orders the rest.</summary>
        /// <param name="document">The document.</param>
        public static void PruneTags(OpenApiDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            HashSet<string> used = new(StringComparer.Ordinal);
            foreach (IOpenApiPathItem path in document.Paths?.Values ?? Enumerable.Empty<IOpenApiPathItem>())
            {
                foreach (OpenApiOperation operation in path.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
                {
                    foreach (OpenApiTagReference tag in operation.Tags ?? Enumerable.Empty<OpenApiTagReference>())
                    {
                        if (tag.Name is string name)
                        {
                            _ = used.Add(name);
                        }
                    }
                }
            }

            document.Tags = new HashSet<OpenApiTag>(used.Order(StringComparer.Ordinal).Select(static name => new OpenApiTag { Name = name }));
        }
    }

    /// <summary>What an entity's schema states beyond its types: who writes each member, and what a navigation carries.</summary>
    internal static class EntitySchemas
    {
        /// <summary>
        ///     Marks each entity schema's members a save never takes <c>readOnly</c> (write-once and
        ///     derived ones flagged as such) and lists each navigation's related members.
        /// </summary>
        /// <param name="document">The document, its components resolved.</param>
        /// <param name="services">The application's services.</param>
        public static void Apply(OpenApiDocument document, IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(services);
            if (document.Components?.Schemas is not IDictionary<string, IOpenApiSchema> schemas
                || services.GetService<IEntityMetadataProvider>() is not IEntityMetadataProvider entities)
            {
                return;
            }

            // --- The schema of each entity, found by the name the generator gave it ---
            JsonSerializerOptions json = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            OpenApiOptions openApi = services.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get(WebOpenApi.DocumentName);
            IStackRegistry? registry = services.GetService<IStackRegistry>();
            foreach (EntityMetadata entity in entities.All)
            {
                JsonTypeInfo info = json.GetTypeInfo(entity.ClrType);
                if (openApi.CreateSchemaReferenceId(info) is string id && schemas.TryGetValue(id, out IOpenApiSchema? found) && found is OpenApiSchema schema)
                {
                    Annotate(schema, entity, info, registry, json);
                }
            }
        }

        /// <summary>Annotates one entity's schema.</summary>
        /// <param name="schema">The schema.</param>
        /// <param name="entity">The entity's metadata.</param>
        /// <param name="info">The entity's JSON contract.</param>
        /// <param name="registry">The stack registry, whose related selects win; null when none is composed.</param>
        /// <param name="json">The host's JSON options.</param>
        private static void Annotate(OpenApiSchema schema, EntityMetadata entity, JsonTypeInfo info, IStackRegistry? registry, JsonSerializerOptions json)
        {
            // --- Each member's wire name, by its CLR property ---
            Dictionary<string, string> wireNames = new(StringComparer.Ordinal);
            foreach (JsonPropertyInfo property in info.Properties)
            {
                if (property.AttributeProvider is MemberInfo member)
                {
                    wireNames[member.Name] = property.Name;
                }
            }

            // --- The members a save never takes: server-owned, derived, write-once and database-owned ---
            foreach (PropertyMetadata property in entity.Properties)
            {
                if (property.Ownership == PropertyOwnership.Editable
                    || !wireNames.TryGetValue(property.Name, out string? wire)
                    || schema.Properties?.TryGetValue(wire, out IOpenApiSchema? member) != true)
                {
                    continue;
                }

                string? flag = property.Ownership switch
                {
                    PropertyOwnership.WriteOnce => WebOpenApi.WriteOnceExtension,
                    PropertyOwnership.Derived => WebOpenApi.DerivedExtension,
                    PropertyOwnership.ServerOwned or PropertyOwnership.DatabaseOwned or PropertyOwnership.Editable => null,
                    _ => throw new InvalidOperationException($"The member '{property.Name}' has an unknown ownership."),
                };
                schema.Properties[wire] = ReadOnly(member!, flag);
            }

            // --- Each navigation's related members, by the navigation's wire name ---
            JsonObject related = [];
            foreach (ReferenceMetadata reference in entity.References)
            {
                EntityMetadata target = reference.Target;
                IEnumerable<string> select = registry?.Find(target.Name)?.RelatedSelect
                    ?? registry?.FindChildQuery(target.Name)?.RelatedSelect
                    ?? StackDescriptorBuilder.RelatedSelect(target);
                string navigation = wireNames.GetValueOrDefault(reference.Navigation) ?? json.PropertyNamingPolicy?.ConvertName(reference.Navigation) ?? reference.Navigation;
                related[navigation] = new JsonArray([.. select.Select(column => (JsonNode?)(json.PropertyNamingPolicy?.ConvertName(column) ?? column))]);
            }

            if (related.Count > 0)
            {
                schema.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
                schema.Extensions[WebOpenApi.RelatedSelectExtension] = WebOpenApi.Extension(related);
            }
        }

        /// <summary>
        ///     A member's schema marked read-only, with its flag. A reference is marked beside its
        ///     <c>$ref</c>, so the shared component it points at stays as it is.
        /// </summary>
        /// <param name="member">The member's schema.</param>
        /// <param name="flag">The extension that flags it; null for none.</param>
        /// <returns>The marked schema.</returns>
        private static IOpenApiSchema ReadOnly(IOpenApiSchema member, string? flag)
        {
            switch (member)
            {
                case OpenApiSchema inline:
                    inline.ReadOnly = true;
                    if (flag is not null)
                    {
                        inline.Extensions ??= new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal);
                        inline.Extensions[flag] = WebOpenApi.Extension(JsonValue.Create(true));
                    }

                    return inline;

                case OpenApiSchemaReference reference:
                    reference.ReadOnly = true;
                    if (flag is not null)
                    {
                        // A reference's own extensions, never the component's it would read through to.
                        reference.Extensions = new Dictionary<string, IOpenApiExtension>(StringComparer.Ordinal)
                        {
                            [flag] = WebOpenApi.Extension(JsonValue.Create(true)),
                        };
                    }

                    return reference;

                default:
                    return member;
            }
        }
    }
}
