// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Api;
using Tellma.Core.Composition;
using Tellma.Core.Crud;

namespace Tellma.Core.AspNetCore.Routing
{
    // Spec 0016 §3.1 and §4.6: the realised gate resolves the JSON contract of every type the web
    // surface reads or writes once, so an unserializable member fails startup rather than the first
    // request; and refuses a mapped property the wire never carries, which the pipeline would
    // otherwise write as a default value.

    /// <summary>
    ///     The web surface's contract audit: every registered entity, child collection element, action
    ///     request and action result type resolves its JSON contract under the host's HTTP JSON
    ///     options, transitively, and no mapped property the caller may write is ignored by JSON.
    /// </summary>
    internal static class ApiContractsAudit
    {
        /// <summary>The name System.Text.Json gives its converter for a type it refuses to read or write.</summary>
        private const string UnsupportedConverterName = "UnsupportedTypeConverter";

        /// <summary>Audits the composed web surface's contracts.</summary>
        /// <param name="services">The application's services.</param>
        /// <returns>One problem per unresolvable type and per ignored writable property.</returns>
        public static IReadOnlyList<CompositionProblem> Check(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // --- Nothing to judge without a composed registry and model: their own checks say why ---
            if (services.GetService<StackRegistry>() is not StackRegistry registry || registry.Failed
                || services.GetService<IRouteContracts>() is not IRouteContracts contracts)
            {
                return [];
            }

            IReadOnlyList<RouteContract> routes;
            IEntityMetadataProvider metadata;
            try
            {
                routes = contracts.Routes;
                metadata = services.GetRequiredService<IEntityMetadataProvider>();
            }
            catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
            {
                // The projection and the metadata check report the defect; the contracts wait for it.
                return [];
            }

            JsonSerializerOptions options = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
            List<CompositionProblem> problems = [];

            // --- The wire's entities: each stack's, each child query surface's, and their children at every depth ---
            List<EntityMetadata> entities = [];
            HashSet<Type> seen = [];
            foreach (Type root in registry.Stacks.Select(static stack => stack.EntityType).Concat(registry.ChildQueries.Select(static child => child.EntityType)))
            {
                CollectEntities(SafeGet(metadata, root), entities, seen);
            }

            foreach (EntityMetadata entity in entities)
            {
                AuditIgnored(entity, problems);
            }

            // --- Every type's contract, transitively: the entities, then each route's body and result ---
            IEnumerable<Type> types = entities.Select(static entity => entity.ClrType)
                .Concat(routes.SelectMany(static route => (Type?[])[route.RequestType, route.ResponseType]).OfType<Type>());
            HashSet<Type> resolved = [];
            foreach (Type type in types)
            {
                Resolve(type, options, resolved, problems);
            }

            return problems;
        }

        /// <summary>An entity's metadata; null when the model does not describe it.</summary>
        /// <param name="metadata">The entity metadata.</param>
        /// <param name="type">The entity type.</param>
        /// <returns>The metadata.</returns>
        private static EntityMetadata? SafeGet(IEntityMetadataProvider metadata, Type type)
        {
            try
            {
                return metadata.Get(type);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or KeyNotFoundException)
            {
                // The registry composed the stack, so its metadata check reports an entity the model lacks.
                return null;
            }
        }

        /// <summary>Collects an entity and its children at every depth.</summary>
        /// <param name="entity">The entity; null to collect nothing.</param>
        /// <param name="entities">Receives the entities.</param>
        /// <param name="seen">The entity types collected so far.</param>
        private static void CollectEntities(EntityMetadata? entity, List<EntityMetadata> entities, HashSet<Type> seen)
        {
            if (entity is null || !seen.Add(entity.ClrType))
            {
                return;
            }

            entities.Add(entity);
            foreach (ChildCollectionMetadata child in entity.Children)
            {
                CollectEntities(child.Child, entities, seen);
            }
        }

        /// <summary>
        ///     Refuses a mapped property JSON always ignores unless the caller never writes it: a
        ///     server-owned, derived or database-owned value is never taken from the payload, so its
        ///     absence on the wire maps nothing into the row.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="problems">Receives the problems.</param>
        private static void AuditIgnored(EntityMetadata entity, List<CompositionProblem> problems)
        {
            foreach (PropertyMetadata property in entity.Properties)
            {
                if (property.Ownership is not (PropertyOwnership.Editable or PropertyOwnership.WriteOnce))
                {
                    continue;
                }

                // The most derived declaration is the one the serializer reads.
                PropertyInfo? clr = entity.ClrType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(candidate => string.Equals(candidate.Name, property.Name, StringComparison.Ordinal))
                    .OrderByDescending(static candidate => Depth(candidate.DeclaringType))
                    .FirstOrDefault();
                if (clr?.GetCustomAttribute<JsonIgnoreAttribute>(inherit: true) is { Condition: JsonIgnoreCondition.Always })
                {
                    problems.Add(new CompositionProblem(
                        TellmaEndpointAudit.CheckName,
                        $"The mapped property {TypeDisplay.Short(entity.ClrType)}.{property.Name} is [JsonIgnore] but {(property.Ownership == PropertyOwnership.WriteOnce ? "write-once" : "editable")}, so a save would write its default value into the row.",
                        "Mark the property [ServerOwned] when the server sets it, or remove [JsonIgnore] so the wire carries it."));
                }
            }
        }

        /// <summary>How many base types a type has, so a redeclared property's most derived declaration wins.</summary>
        /// <param name="type">The declaring type.</param>
        /// <returns>The depth.</returns>
        private static int Depth(Type? type)
        {
            int depth = 0;
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                depth++;
            }

            return depth;
        }

        /// <summary>Resolves a type's JSON contract and, through its members, every type it serializes.</summary>
        /// <param name="type">The type.</param>
        /// <param name="options">The host's HTTP JSON options.</param>
        /// <param name="resolved">The types resolved so far.</param>
        /// <param name="problems">Receives the problems.</param>
        private static void Resolve(Type type, JsonSerializerOptions options, HashSet<Type> resolved, List<CompositionProblem> problems)
        {
            // A route without a body, a streamed workbook and an untyped value have no contract to resolve.
            Stack<Type> pending = new([type]);
            while (pending.TryPop(out Type? current))
            {
                if (current == typeof(void) || current == typeof(object) || current == typeof(ExportOutcome) || current.IsGenericParameter || !resolved.Add(current))
                {
                    continue;
                }

                JsonTypeInfo info;
                try
                {
                    info = options.GetTypeInfo(current);
                }
                catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or ArgumentException)
                {
                    problems.Add(new CompositionProblem(
                        TellmaEndpointAudit.CheckName,
                        $"The web surface's type {TypeDisplay.Short(current)} has no JSON contract under the host's JSON options: {exception.Message}",
                        "Make every member of the type serializable by System.Text.Json, or register a converter for the member's type."));
                    continue;
                }

                // --- A contract that resolves but writes nothing: System.Text.Json refuses the value only when it meets one ---
                if (IsUnsupported(current, info))
                {
                    problems.Add(new CompositionProblem(
                        TellmaEndpointAudit.CheckName,
                        $"The web surface's type {TypeDisplay.Short(current)} has no JSON contract under the host's JSON options: System.Text.Json reads and writes no value of it, so the first request that carries one fails.",
                        "Replace the member with a serializable type (a jagged array for a multi-dimensional one, a name for a type), or register a converter for it."));
                    continue;
                }

                // --- What the contract serializes in turn: its members, its elements and its keys ---
                switch (info.Kind)
                {
                    case JsonTypeInfoKind.Object:
                        foreach (JsonPropertyInfo property in info.Properties)
                        {
                            // An ignored member is never read or written, so its type needs no contract.
                            if (property.Get is null && property.Set is null)
                            {
                                continue;
                            }

                            pending.Push(property.PropertyType);
                        }

                        break;

                    case JsonTypeInfoKind.Enumerable:
                    case JsonTypeInfoKind.Dictionary:
                        if (info.ElementType is Type element)
                        {
                            pending.Push(element);
                        }

                        if (info.KeyType is Type key)
                        {
                            pending.Push(key);
                        }

                        break;

                    case JsonTypeInfoKind.None:
                    default:
                        // A converter owns the type's shape; its parts are its own concern.
                        break;
                }
            }
        }

        /// <summary>
        ///     Whether a resolved contract is System.Text.Json's refusal rather than a converter: the
        ///     serializer's own unsupported-type converter, or its built-in converter for a kind it
        ///     never writes (a delegate, a <see cref="Type" />, a pointer, a native-sized integer, an
        ///     array of rank above one, a by-ref-like type).
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="info">Its resolved contract.</param>
        /// <returns>True when no value of the type can be written.</returns>
        private static bool IsUnsupported(Type type, JsonTypeInfo info)
        {
            // A converter the host registered owns its type, whatever the kind.
            Type converter = info.Converter.GetType();
            return info.Kind == JsonTypeInfoKind.None
                && converter.Assembly == typeof(JsonSerializer).Assembly
                && (converter.Name.StartsWith(UnsupportedConverterName, StringComparison.Ordinal)
                    || typeof(Delegate).IsAssignableFrom(type)
                    || typeof(Type).IsAssignableFrom(type)
                    || type.IsPointer
                    || type == typeof(IntPtr)
                    || type == typeof(UIntPtr)
                    || (type.IsArray && type.GetArrayRank() > 1)
                    || type.IsByRefLike);
        }
    }
}
