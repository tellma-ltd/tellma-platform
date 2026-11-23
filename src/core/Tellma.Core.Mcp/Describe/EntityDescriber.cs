// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Mcp.Rendering;
using Tellma.Core.Mcp.Resources;
using Tellma.Core.Mcp.Tools;
using Tellma.Core.Queryex;

namespace Tellma.Core.Mcp.Describe
{
    /// <summary>One catalogue entry: an entity the tools reach, with its title and description.</summary>
    /// <param name="Name">The entity's name.</param>
    /// <param name="Title">The entity's plural label in the request's language.</param>
    /// <param name="Description">The stack's description, if any.</param>
    /// <param name="Owner">The owner of a child entity; null for a stack.</param>
    internal sealed record CatalogueEntry(string Name, string Title, string? Description, string? Owner);

    /// <summary>
    ///     Writes <c>tellma_describe</c>'s two answers as Markdown: the catalogue of the entities the
    ///     tools reach, and one entity's description, its columns merged from the entity's metadata and
    ///     its query schema under the tenant's multilingual shape, its navigations with the columns a
    ///     filtered reader reaches through them, its collections, operations and actions.
    /// </summary>
    internal sealed class EntityDescriber
    {
        /// <summary>The options action argument schemas are exported with: the wire's.</summary>
        private static readonly JsonSchemaExporterOptions ExporterOptions = new() { TreatNullObliviousAsNonNullable = true };

        /// <summary>The entities the tools reach.</summary>
        private readonly McpEntities _entities;

        /// <summary>Every registered stack and child surface, hidden ones included, for the navigations' targets.</summary>
        private readonly IStackRegistry _registry;

        /// <summary>The entities' metadata.</summary>
        private readonly IEntityMetadataProvider _metadata;

        /// <summary>The query schemas, one per multilingual shape.</summary>
        private readonly IQueryexSchemaProvider _schemas;

        /// <summary>The securables, which mark the sensitive operations.</summary>
        private readonly ISecurableRegistry _securables;

        /// <summary>Creates the describer.</summary>
        /// <param name="entities">The entities the tools reach.</param>
        /// <param name="registry">Every registered stack and child surface.</param>
        /// <param name="metadata">The entities' metadata.</param>
        /// <param name="schemas">The query schemas.</param>
        /// <param name="securables">The securables.</param>
        public EntityDescriber(
            McpEntities entities, IStackRegistry registry, IEntityMetadataProvider metadata, IQueryexSchemaProvider schemas, ISecurableRegistry securables)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(schemas);
            ArgumentNullException.ThrowIfNull(securables);
            _entities = entities;
            _registry = registry;
            _metadata = metadata;
            _schemas = schemas;
            _securables = securables;
        }

        /// <summary>The catalogue: each stack the tools reach, then each child entity under its owner.</summary>
        /// <param name="labels">The labels, in the request's language.</param>
        /// <returns>The entries, in the registry's order.</returns>
        public IReadOnlyList<CatalogueEntry> Catalogue(ILabelProvider labels)
        {
            ArgumentNullException.ThrowIfNull(labels);
            List<CatalogueEntry> entries = [];
            foreach (StackDescriptor stack in _entities.Stacks)
            {
                entries.Add(new CatalogueEntry(stack.Resource, labels.EntityLabel(stack.EntityType, plural: true), stack.Description, null));
                foreach (ChildQueryDescriptor child in _entities.Children.Where(child => string.Equals(child.OwnerResource, stack.Resource, StringComparison.Ordinal)))
                {
                    entries.Add(new CatalogueEntry(child.Resource, labels.EntityLabel(child.EntityType, plural: true), null, stack.Resource));
                }
            }

            return entries;
        }

        /// <summary>Renders the catalogue, narrowed by search words when given.</summary>
        /// <param name="entries">The catalogue.</param>
        /// <param name="search">The words every listed entry must contain, ignoring case, in its name, title or description; null for every entry.</param>
        /// <returns>The Markdown text.</returns>
        public static string RenderCatalogue(IReadOnlyList<CatalogueEntry> entries, string? search)
        {
            ArgumentNullException.ThrowIfNull(entries);
            string[] words = search?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
            List<CatalogueEntry> matched = [.. entries.Where(entry => Matches(entry, words))];

            // --- One line per entity; a child entity's line sits under its owner's ---
            StringBuilder text = new();
            _ = text.Append(words.Length == 0 ? "# Entities\n" : $"# Entities matching \"{string.Join(' ', words)}\"\n");
            foreach (CatalogueEntry entry in matched)
            {
                _ = entry.Owner is null
                    ? text.Append(CultureInfo.InvariantCulture, $"\n- {entry.Name} — {entry.Title}")
                    : text.Append(CultureInfo.InvariantCulture, $"\n  - {entry.Name} — {entry.Title} (child entity of {entry.Owner}; query it with {McpToolNames.Query})");
                if (entry.Description is { Length: > 0 } description)
                {
                    _ = text.Append(": ").Append(OneLine(description));
                }
            }

            _ = matched.Count == 0
                ? text.Append("\nNo entity matches. Call ").Append(McpToolNames.Describe).Append(" without search for every entity.")
                : text.Append("\n\nCall ").Append(McpToolNames.Describe).Append(" with entity for one entity's columns, operations and actions.");
            return text.ToString();
        }

        /// <summary>Renders one entity's description.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="shape">The tenant's multilingual shape, which gates the secondary and tertiary columns.</param>
        /// <param name="labels">The labels, in the request's language.</param>
        /// <returns>The Markdown text.</returns>
        public string Describe(McpEntity entity, MultilingualShape shape, ILabelProvider labels)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(labels);
            EntityMetadata metadata = _metadata.Get(entity.EntityType);
            EntityDescriptor? schema = _schemas.GetSchema(shape).FindEntity(metadata.Name);
            HashSet<string> gated = Gated(metadata, shape);
            CultureInfo invariant = CultureInfo.InvariantCulture;
            StringBuilder text = new();

            // --- The header: name, title, description, and what the entity is ---
            _ = text.Append(invariant, $"# {entity.Name} — {labels.EntityLabel(entity.EntityType, plural: true)}\n");
            if (entity.Stack?.Description is { Length: > 0 } description)
            {
                _ = text.Append('\n').Append(description).Append('\n');
            }

            _ = text.Append('\n');
            if (entity.Child is ChildQueryDescriptor child)
            {
                _ = text.Append(invariant, $"- A child entity: the {child.Collection} of {child.OwnerResource}, reached through {child.OwnerNavigation}. Query it with {McpToolNames.Query} or {McpToolNames.Aggregate} under {child.OwnerResource}'s read permission; read whole records through {child.OwnerResource} with {McpToolNames.Get}.\n");
            }

            Facts(text, entity, metadata, gated);

            // --- The columns ---
            _ = text.Append("\n## Properties\n\n| Name | Label | Type | Store type | Nullable | Editable | Max length | Precision, scale | Values | Multilingual |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (PropertyMetadata property in metadata.Properties.Where(property => !gated.Contains(property.Name)))
            {
                PropertyDescriptor? column = schema?.Properties.FirstOrDefault(candidate => string.Equals(candidate.Name, property.Name, StringComparison.Ordinal));
                _ = text.Append(invariant, $"\n| {property.Name} | {Cell(labels.PropertyLabel(entity.EntityType, property.Name))} | {TypeName(column, property)} | {StoreTypeName(column?.StoreType)} | {(property.IsNullable ? "yes" : "no")} | {Editable(property.Ownership)} | {property.MaxLength?.ToString(invariant) ?? string.Empty} | {Precision(property)} | {Cell(property.EnumValues is { Count: > 0 } values ? string.Join(", ", values) : string.Empty)} | {property.MultilingualGroup ?? string.Empty} |");
            }

            // --- The navigations, with the columns reachable through each under a filtered or absent grant ---
            if (metadata.References.Count > 0)
            {
                _ = text.Append("\n\n## Navigations\n\nA path through a navigation reaches every column of its target only under an unrestricted read of the target; otherwise only the reachable columns.\n\n| Name | Target | Foreign key | Reachable |\n| --- | --- | --- | --- |");
                foreach (ReferenceMetadata reference in metadata.References)
                {
                    _ = text.Append(invariant, $"\n| {reference.Navigation} | {reference.Target.Name} | {reference.ForeignKey.Name} | {string.Join(", ", Reachable(reference.Target))} |");
                }
            }

            // --- The child collections ---
            if (metadata.Children.Count > 0)
            {
                _ = text.Append(invariant, $"\n\n## Child collections\n\n{McpToolNames.Get} returns each collection cut to a window; page one with collection, skip and top.\n\n| Name | Entity | Most rows |\n| --- | --- | --- |");
                foreach (ChildCollectionMetadata collection in metadata.Children)
                {
                    _ = text.Append(invariant, $"\n| {JsonName(collection.Property)} | {collection.Child.Name} | {collection.MaxCount} |");
                }
            }

            // --- The actions, each with its flags and its argument schema ---
            Actions(text, entity, labels);

            // --- Three example filters, and the syntax reference ---
            _ = text.Append("\n\n## Example filters\n");
            foreach (string example in ExampleFilters(metadata, schema, gated))
            {
                _ = text.Append("\n- `").Append(example).Append('`');
            }

            _ = text.Append(invariant, $"\n\nThe expression syntax of select, filter, having and orderBy is the resource {QueryexSyntaxResource.ResourceUri}.");
            return text.ToString();
        }

        /// <summary>The columns a navigation's target exposes to a reader without an unrestricted read of it.</summary>
        /// <param name="target">The target.</param>
        /// <returns>The columns; the key alone when no surface declares more.</returns>
        internal IReadOnlyList<string> Reachable(EntityMetadata target)
        {
            ArgumentNullException.ThrowIfNull(target);
            IReadOnlySet<string>? related = _registry.Stacks.FirstOrDefault(stack => stack.EntityType == target.ClrType)?.RelatedSelect
                ?? _registry.ChildQueries.FirstOrDefault(child => child.EntityType == target.ClrType)?.RelatedSelect;
            return related is null ? [target.Key.Name] : [.. related];
        }

        /// <summary>Three filters an agent can adapt, built from the entity's own columns.</summary>
        /// <param name="metadata">The entity's metadata.</param>
        /// <param name="schema">The entity's query schema, if it declares one.</param>
        /// <param name="gated">The columns the tenant's shape leaves out.</param>
        /// <returns>The filters.</returns>
        internal static IReadOnlyList<string> ExampleFilters(EntityMetadata metadata, EntityDescriptor? schema, IReadOnlySet<string> gated)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(gated);
            IReadOnlyList<PropertyDescriptor> columns = [.. (schema?.Properties ?? []).Where(column => !gated.Contains(column.Name))];
            PropertyDescriptor? text = columns.FirstOrDefault(static column => column.Type == QueryexType.QxString);
            PropertyDescriptor? date = columns.FirstOrDefault(static column => column.Type is QueryexType.QxDate or QueryexType.QxDateTime);
            PropertyDescriptor? number = columns.FirstOrDefault(column => column.Type == QueryexType.QxNumeric && !ReferenceEquals(column, schema?.Key));
            NavigationDescriptor? navigation = schema is { Navigations.Count: > 0 } ? schema.Navigations[0] : null;
            string key = metadata.Key.Name;

            List<string> examples = [];

            // --- A text match, a range, and a path through a navigation, each when the entity has one ---
            examples.Add(text is not null ? $"startsWith({text.Name}, 'A')" : $"{key} in (@first, @second)");
            examples.Add(
                date is not null ? $"{date.Name} >= startOfYear(today())"
                : number is not null ? $"{number.Name} > 0 or {number.Name} is null"
                : $"{key} = @id");
            if (navigation is not null)
            {
                PropertyDescriptor? targetText = navigation.Target.Properties.FirstOrDefault(static column => column.Type == QueryexType.QxString);
                examples.Add(targetText is not null ? $"{navigation.Name}.{targetText.Name} = @value" : $"{navigation.Name} is not null");
            }
            else
            {
                examples.Add(metadata.IsActivatable ? "IsActive = true" : text is not null ? $"{text.Name} is not null" : $"{key} != @id");
            }

            return examples;
        }

        /// <summary>The columns the tenant's multilingual shape leaves out: the secondary and tertiary columns it has no language for.</summary>
        /// <param name="metadata">The entity's metadata.</param>
        /// <param name="shape">The tenant's shape.</param>
        /// <returns>The columns' names.</returns>
        internal static HashSet<string> Gated(EntityMetadata metadata, MultilingualShape shape)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            HashSet<string> gated = new(StringComparer.Ordinal);
            foreach (MultilingualGroup group in metadata.MultilingualGroups)
            {
                if (shape < MultilingualShape.PrimaryAndSecondary && group.Secondary is PropertyMetadata secondary)
                {
                    _ = gated.Add(secondary.Name);
                }

                if (shape < MultilingualShape.All && group.Tertiary is PropertyMetadata tertiary)
                {
                    _ = gated.Add(tertiary.Name);
                }
            }

            return gated;
        }

        /// <summary>Whether a catalogue entry contains every search word.</summary>
        /// <param name="entry">The entry.</param>
        /// <param name="words">The words.</param>
        /// <returns>True when it does, or when there are none.</returns>
        private static bool Matches(CatalogueEntry entry, string[] words)
        {
            return words.All(word =>
                entry.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
                || entry.Title.Contains(word, StringComparison.OrdinalIgnoreCase)
                || (entry.Description?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        /// <summary>Text on one line: its line breaks as spaces.</summary>
        /// <param name="value">The text.</param>
        /// <returns>The line.</returns>
        private static string OneLine(string value)
        {
            return value.ReplaceLineEndings(" ");
        }

        /// <summary>Text safe in a Markdown table cell.</summary>
        /// <param name="value">The text.</param>
        /// <returns>The cell.</returns>
        private static string Cell(string value)
        {
            return RowRendering.Escape(value);
        }

        /// <summary>A member's name as the wire writes it.</summary>
        /// <param name="name">The CLR name.</param>
        /// <returns>The JSON name.</returns>
        private static string JsonName(string name)
        {
            return TellmaJson.Options.PropertyNamingPolicy?.ConvertName(name) ?? name;
        }

        /// <summary>A column's Queryex type, or its CLR type's name when the schema declares no column.</summary>
        /// <param name="column">The column, if the schema declares one.</param>
        /// <param name="property">The property.</param>
        /// <returns>The type's name.</returns>
        private static string TypeName(PropertyDescriptor? column, PropertyMetadata property)
        {
            return column is not null
                ? column.Type.ToString()[2..].ToLowerInvariant()
                : (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType).Name.ToLowerInvariant();
        }

        /// <summary>A column's store type, such as <c>nvarchar(255)</c>.</summary>
        /// <param name="storeType">The store type, if known.</param>
        /// <returns>The store type's name, or empty.</returns>
        private static string StoreTypeName(QueryexStoreType? storeType)
        {
            if (storeType is not QueryexStoreType type)
            {
                return string.Empty;
            }

            string family = type.Family.ToString()[2..].ToLowerInvariant();
            return (type.Size, type.Scale) switch
            {
                (int size, int scale) => string.Create(CultureInfo.InvariantCulture, $"{family}({size},{scale})"),
                (int size, null) => string.Create(CultureInfo.InvariantCulture, $"{family}({(size < 0 ? "max" : size.ToString(CultureInfo.InvariantCulture))})"),
                _ => family,
            };
        }

        /// <summary>Whether a save writes a property.</summary>
        /// <param name="ownership">The property's ownership.</param>
        /// <returns><c>yes</c>, <c>on insert</c>, or <c>no</c>.</returns>
        private static string Editable(PropertyOwnership ownership)
        {
            return ownership switch
            {
                PropertyOwnership.Editable => "yes",
                PropertyOwnership.WriteOnce => "on insert",
                PropertyOwnership.ServerOwned or PropertyOwnership.Derived or PropertyOwnership.DatabaseOwned => "no",
                _ => "no",
            };
        }

        /// <summary>A decimal property's precision and scale.</summary>
        /// <param name="property">The property.</param>
        /// <returns>The pair, or empty.</returns>
        private static string Precision(PropertyMetadata property)
        {
            return property.Precision is int precision
                ? string.Create(CultureInfo.InvariantCulture, $"{precision}, {property.Scale ?? 0}")
                : string.Empty;
        }

        /// <summary>Writes the entity's facts: display name, order, select, search, keys, operations and capabilities.</summary>
        /// <param name="text">The text so far.</param>
        /// <param name="entity">The entity.</param>
        /// <param name="metadata">Its metadata.</param>
        /// <param name="gated">The columns the tenant's shape leaves out.</param>
        private void Facts(StringBuilder text, McpEntity entity, EntityMetadata metadata, HashSet<string> gated)
        {
            CultureInfo invariant = CultureInfo.InvariantCulture;
            DisplayNameMetadata display = metadata.DisplayName;
            string displayName = display.Prefix is PropertyMetadata prefix
                ? $"{prefix.Name}{DisplayNameMetadata.Separator}{display.Source.Name}"
                : display.Source.Name;
            _ = text.Append(invariant, $"- Key: {metadata.Key.Name}\n- Display name: {displayName}\n");

            // --- What a query takes by default, and how search matches ---
            IReadOnlyList<(string Property, SearchKind Kind)> searchable = entity.Stack?.SearchableProperties ?? entity.Child!.SearchableProperties;
            string? defaultOrder = entity.Stack?.DefaultOrderBy ?? entity.Child?.DefaultOrderBy;
            _ = text.Append(invariant, $"- Default select: {entity.Stack?.DefaultSelect ?? entity.Child!.DefaultSelect}\n");
            if (defaultOrder is { Length: > 0 })
            {
                _ = text.Append(invariant, $"- Default order: {defaultOrder}\n");
            }

            if (searchable.Count > 0)
            {
                _ = text.Append("- Searchable: ").AppendJoin(", ", searchable.Where(column => !gated.Contains(column.Property)).Select(static column => $"{column.Property} ({(column.Kind == SearchKind.Prefix ? "prefix" : "contains")})")).Append('\n');
            }

            if (entity.Stack is StackDescriptor stack)
            {
                if (stack.NaturalKeys.Count > 0)
                {
                    _ = text.Append("- Natural keys: ").AppendJoin(", ", stack.NaturalKeys).Append('\n');
                }

                // --- The operations a caller may be granted, the sensitive ones left out ---
                _ = text.Append("- Operations: ").AppendJoin(", ", Operations(stack)).Append('\n');
                if (stack.Capabilities.Count > 0)
                {
                    _ = text.Append("- Capabilities: ").AppendJoin(", ", stack.Capabilities.Order(StringComparer.Ordinal)).Append('\n');
                }

                if (stack.Extras.Count > 0)
                {
                    _ = text.Append(invariant, $"- Extras ({McpToolNames.Get}'s include): ").AppendJoin(", ", stack.Extras).Append('\n');
                }

                _ = text.Append(invariant, $"- MCP: {(stack.Mcp == McpExposure.ReadOnly ? "read-only: the write tools refuse it" : "read and write")}\n");
            }

            if (metadata.IsActivatable)
            {
                _ = text.Append(invariant, $"- Activatable: {McpToolNames.Query} and {McpToolNames.Aggregate} read only active rows unless includeInactive is true.\n");
            }

            if (metadata.Tree is not null)
            {
                _ = text.Append("- A tree: filter with descendantOf and ancestorOf.\n");
            }
        }

        /// <summary>The standard operations a stack serves, each by the tool that runs it, the sensitive ones left out.</summary>
        /// <param name="stack">The stack.</param>
        /// <returns>The operations.</returns>
        private List<string> Operations(StackDescriptor stack)
        {
            (StackOperations Operation, string Name, string Action)[] standard =
            [
                (StackOperations.Query, "query", AccessActions.Read),
                (StackOperations.Details, "get", AccessActions.Read),
                (StackOperations.Save, "save", AccessActions.Save),
                (StackOperations.Delete, "delete", AccessActions.Delete),
            ];
            return [.. standard
                .Where(operation => stack.Operations.HasFlag(operation.Operation) && !IsSensitive(stack.Resource, operation.Action))
                .Select(static operation => operation.Name)];
        }

        /// <summary>Writes the entity's actions with their flags and argument schemas; hidden and sensitive ones left out.</summary>
        /// <param name="text">The text so far.</param>
        /// <param name="entity">The entity.</param>
        /// <param name="labels">The labels.</param>
        private void Actions(StringBuilder text, McpEntity entity, ILabelProvider labels)
        {
            IEnumerable<ActionDescriptor> declared = entity.Stack?.Actions ?? (IEnumerable<ActionDescriptor>)entity.Child!.Actions;
            List<ActionDescriptor> actions = [.. declared.Where(action => action.Mcp != McpExposure.Hidden && !IsSensitive(entity.Name, action))];
            if (actions.Count == 0)
            {
                return;
            }

            _ = text.Append("\n\n## Actions\n");
            foreach (ActionDescriptor action in actions)
            {
                // --- The action's name, label and flags ---
                List<string> flags = [action.Mutation ? "mutation" : "read", action.Idempotent ? "idempotent" : "not idempotent"];
                if (action.Destructive)
                {
                    flags.Add("destructive");
                }

                if (action is EntityActionDescriptor { SingleTarget: true })
                {
                    flags.Add("singleTarget");
                }

                if (action.Mcp == McpExposure.ReadOnly && action.Mutation)
                {
                    flags.Add("not served over MCP");
                }

                string label = action is EntityActionDescriptor entityAction ? labels.ActionLabel(entity.EntityType, entityAction.Action) : action.Name;
                _ = text.Append(CultureInfo.InvariantCulture, $"\n### {action.Name}\n\n{label}: {string.Join(", ", flags)}.");
                if (action.Description is { Length: > 0 } description)
                {
                    _ = text.Append(' ').Append(OneLine(description));
                }

                // --- The arguments, as the wire's JSON schema ---
                Type? arguments = action switch
                {
                    EntityActionDescriptor described => described.ArgumentsType,
                    ApiActionDescriptor api => api.RequestType,
                    _ => null,
                };
                if (arguments is not null)
                {
                    JsonNode schema = TellmaJson.Options.GetJsonSchemaAsNode(arguments, ExporterOptions);
                    _ = text.Append("\n\n```json\n").Append(schema.ToJsonString(TellmaJson.Options)).Append("\n```");
                }

                _ = text.Append('\n');
            }
        }

        /// <summary>Whether an action's securable is sensitive, so no agent may be told of it.</summary>
        /// <param name="resource">The entity's name.</param>
        /// <param name="action">The action.</param>
        /// <returns>True when it is.</returns>
        private bool IsSensitive(string resource, ActionDescriptor action)
        {
            return action switch
            {
                EntityActionDescriptor described => IsSensitive(resource, described.Action),
                ApiActionDescriptor { Securable: SecurableRef securable } => IsSensitive(securable.Resource, securable.Action),
                _ => false,
            };
        }

        /// <summary>Whether a securable is sensitive.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <returns>True when it is.</returns>
        private bool IsSensitive(string resource, string action)
        {
            return _securables.Find(resource, action) is { IsSensitive: true };
        }
    }
}
