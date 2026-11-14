// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud
{
    /// <summary>
    ///     Derives one stack's descriptor and its child query surfaces from the leaf's entity metadata,
    ///     its annotations, its service, its companions and its components, with no tenant scope.
    ///     Each problem it finds names the stack, the class or the method, and is sourced to the
    ///     check that reports it.
    /// </summary>
    /// <param name="root">The root service provider, from which each named filter contributor is created once.</param>
    /// <param name="problems">Receives the problems.</param>
    internal sealed class StackDescriptorBuilder(IServiceProvider root, List<CompositionProblem> problems)
    {
        /// <summary>The platform's details extra: the abilities each row refuses.</summary>
        public const string PermissionsExtra = "permissions";

        /// <summary>The name of the multilingual group, or property, the default projections carry.</summary>
        private const string NameProperty = "Name";

        /// <summary>The code column the default projections carry when the entity has one.</summary>
        private const string CodeProperty = "Code";

        /// <summary>The audit columns of a top-level entity, in the order the default select lists them.</summary>
        private static readonly string[] AuditColumns = ["CreatedAt", "CreatedById", "ModifiedAt", "ModifiedById"];

        /// <summary>The standard segments an Excel companion projects, which a companion's actions may take.</summary>
        private static readonly HashSet<string> ExcelSegments = new(StringComparer.Ordinal)
        {
            "export", "export/start", "export-for-import", "export-for-import/start", "inspect-import", "import", "import/start",
        };

        /// <summary>The method behind each declared action, keyed by the descriptor instance the registry owns.</summary>
        public Dictionary<ActionDescriptor, ActionMethod> Methods { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Derives a stack's descriptor.</summary>
        /// <param name="stack">The stack, its service closed.</param>
        /// <param name="metadata">The leaf's entity metadata.</param>
        /// <returns>The descriptor.</returns>
        public StackDescriptor Build(ComposedStack stack, EntityMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(metadata);
            Type leaf = stack.Leaf;
            Type service = stack.ServiceType ?? throw new ArgumentException("The stack's service is not closed.", nameof(stack));
            Type key = stack.KeyType!;
            string resource = metadata.Name;

            StackAttribute? declared = EntityShapes.GetAttribute<StackAttribute>(leaf);
            ApiResourceAttribute? api = EntityShapes.GetAttribute<ApiResourceAttribute>(leaf);
            StackOperations operations = stack.DeclaredOperations ?? declared?.Operations ?? StackOperations.All;

            // --- The shape: capabilities, searchable columns, natural keys ---
            List<(string Property, SearchKind Kind)> searchable = [.. metadata.Properties
                .Where(static property => property.IsSearchable)
                .Select(static property => (property.Name, property.SearchKind ?? SearchKind.Contains))];
            IReadOnlySet<string> capabilities = Capabilities(metadata, operations, searchable.Count > 0);

            // --- The actions: built-ins, the service's own, then each companion's ---
            List<ActionDescriptor> actions = Actions(stack, metadata, operations, capabilities);

            // --- The extras and the named filters, read from the classes with no tenant scope ---
            IReadOnlyList<string> extras = Extras(stack, operations, metadata.IsActivatable, actions);
            IReadOnlyDictionary<string, FilterTree> namedFilters = NamedFilters(stack, resource);

            return new StackDescriptor(
                Resource: resource,
                ResourceSegment: api?.Segment ?? StackNames.Kebab(metadata.Table.Name),
                EntityType: leaf,
                ServiceType: service,
                KeyType: key,
                Description: api?.Description,
                Operations: operations,
                Capabilities: capabilities,
                Actions: actions.AsReadOnly(),
                Properties: metadata.Properties,
                Children: [.. metadata.Children.Select(static child => new ChildCollectionDescriptor(child.Property, child.Child.ClrType, child.ParentKey.Name, child.MaxCount))],
                SearchableProperties: searchable.AsReadOnly(),
                NaturalKeys: [.. metadata.NaturalKeys.Select(static natural => natural.Property.Name)],
                DefaultSelect: EntityShapes.GetAttribute<DefaultSelectAttribute>(leaf)?.Select ?? DefaultSelect(metadata),
                RelatedSelect: RelatedSelect(metadata),
                DetailsExpand: EntityShapes.GetAttribute<DetailsExpandAttribute>(leaf)?.Navigations ?? DetailsExpand(metadata),
                DefaultOrderBy: declared?.DefaultOrderBy,
                DisplayName: metadata.DisplayName,
                Extras: extras,
                NamedFilters: namedFilters,
                Limits: Limits(declared, resource),
                Mcp: api?.Mcp ?? McpExposure.Full,
                ContractRevision: api?.ContractRevision ?? 0,
                Feature: stack.Owner.Name);
        }

        /// <summary>
        ///     The child query surfaces of a stack that serves queries: one per child collection at any
        ///     depth, each owned by the surface above it, except under a child type that opts out.
        /// </summary>
        /// <param name="stack">The stack's descriptor.</param>
        /// <param name="metadata">The leaf's entity metadata.</param>
        /// <returns>The surfaces, depth first in collection order.</returns>
        public IReadOnlyList<ChildQueryDescriptor> ChildQueries(StackDescriptor stack, EntityMetadata metadata)
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(metadata);
            List<ChildQueryDescriptor> surfaces = [];
            if (stack.Operations.HasFlag(StackOperations.Query))
            {
                AddChildQueries(stack, metadata, stack.Resource, surfaces);
            }

            return surfaces;
        }

        /// <summary>The closed vocabulary of a stack's capabilities.</summary>
        /// <param name="metadata">The leaf's entity metadata.</param>
        /// <param name="operations">The stack's operations.</param>
        /// <param name="searchable">Whether a property is searchable.</param>
        /// <returns>The capabilities, in the vocabulary's order.</returns>
        private static OrderedNameSet Capabilities(EntityMetadata metadata, StackOperations operations, bool searchable)
        {
            // The vocabulary is closed; assignable joins it with the assignments feature's annotation.
            List<string> capabilities = [];
            AddIf(capabilities, metadata.IsActivatable, "activatable");
            AddIf(capabilities, metadata.Tree is not null, "tree");
            AddIf(capabilities, metadata.IsTemporal, "temporal");
            AddIf(capabilities, metadata.MultilingualGroups.Count > 0, "multilingual");
            AddIf(capabilities, metadata.IsCacheable, "cacheable");
            AddIf(capabilities, CarriesBlobs(metadata), "blobs");
            AddIf(capabilities, metadata.IsJobEntity, "job-entity");
            AddIf(capabilities, searchable, "searchable");
            AddIf(capabilities, (operations & (StackOperations.Export | StackOperations.Import)) != 0, "excel");
            return new OrderedNameSet(capabilities);

            static void AddIf(List<string> capabilities, bool condition, string capability)
            {
                if (condition)
                {
                    capabilities.Add(capability);
                }
            }

            static bool CarriesBlobs(EntityMetadata entity)
            {
                return entity.BlobReferences.Count > 0 || entity.Children.Any(static child => CarriesBlobs(child.Child));
            }
        }

        /// <summary>
        ///     The standard segments a stack projects (spec 0015 §2.4's table), which its declared
        ///     actions may not take.
        /// </summary>
        /// <param name="operations">The stack's operations.</param>
        /// <param name="capabilities">The stack's capabilities.</param>
        /// <returns>The segments.</returns>
        internal static HashSet<string> ProjectedSegments(StackOperations operations, IReadOnlySet<string> capabilities)
        {
            HashSet<string> segments = new(StringComparer.Ordinal);
            bool tree = capabilities.Contains("tree");
            if (operations.HasFlag(StackOperations.Query))
            {
                segments.Add("query");
                AddIf(segments, tree, "get-by-parent-ids");
            }

            if (operations.HasFlag(StackOperations.Details))
            {
                segments.UnionWith(["get", "get-by-ids"]);
                AddIf(segments, capabilities.Contains("cacheable"), "all");
            }

            if (operations.HasFlag(StackOperations.Save))
            {
                segments.Add("save");
                if (capabilities.Contains("activatable"))
                {
                    segments.UnionWith(["activate", "deactivate"]);
                }
            }

            if (operations.HasFlag(StackOperations.Delete))
            {
                segments.UnionWith(["delete", "delete-by-query"]);
                AddIf(segments, tree, "delete-with-descendants");
            }

            if (operations.HasFlag(StackOperations.Export))
            {
                segments.UnionWith(["export", "export/start"]);
            }

            if (operations.HasFlag(StackOperations.Import))
            {
                segments.UnionWith(["export-for-import", "export-for-import/start", "inspect-import", "import", "import/start"]);
            }

            return segments;

            static void AddIf(HashSet<string> segments, bool condition, string segment)
            {
                if (condition)
                {
                    segments.Add(segment);
                }
            }
        }

        /// <summary>
        ///     Every action of the stack, in descriptor order: the built-in activate and deactivate,
        ///     the service's entity and API actions in declaration order, then each companion's API
        ///     actions in registration order. Reports a name that is malformed, repeated or reserved.
        /// </summary>
        /// <param name="stack">The stack.</param>
        /// <param name="metadata">The leaf's entity metadata.</param>
        /// <param name="operations">The stack's operations.</param>
        /// <param name="capabilities">The stack's capabilities.</param>
        /// <returns>The actions.</returns>
        private List<ActionDescriptor> Actions(ComposedStack stack, EntityMetadata metadata, StackOperations operations, IReadOnlySet<string> capabilities)
        {
            Type service = stack.ServiceType!;
            string resource = metadata.Name;
            HashSet<string> projected = ProjectedSegments(operations, capabilities);
            Dictionary<string, string> taken = new(StringComparer.Ordinal);
            List<ActionDescriptor> actions = [];

            // --- Built-in: one securable, Activate, for both directions (spec 0015 §11.1) ---
            if (operations.HasFlag(StackOperations.Save) && metadata.IsActivatable)
            {
                foreach (string name in (string[])["activate", "deactivate"])
                {
                    actions.Add(new EntityActionDescriptor(
                        name,
                        Description: null,
                        Mutation: true,
                        AllowWhenReadOnly: false,
                        Idempotent: true,
                        Long: false,
                        Destructive: false,
                        McpExposure.Full,
                        ContractRevision: 0,
                        ResultType: typeof(EntitiesResult<>).MakeGenericType(stack.Leaf),
                        Action: AccessActions.Activate,
                        SupportsFilter: true,
                        LoadChildren: false,
                        PostCheck: false,
                        SingleTarget: false,
                        IsBuiltIn: true,
                        ArgumentsType: null));
                    taken[name] = "the built-in action";
                }
            }

            // --- The service's entity and API actions, in declaration order ---
            foreach (MethodInfo method in ActionMethods.Declared(service))
            {
                EntityActionAttribute? entityAction = method.GetCustomAttribute<EntityActionAttribute>(inherit: true);
                ApiActionAttribute? apiAction = method.GetCustomAttribute<ApiActionAttribute>(inherit: true);
                string described = ActionMethods.Describe(service, method);
                if (entityAction is not null && apiAction is not null)
                {
                    ReportAction($"The method {described} declares both [EntityAction] and [ApiAction]; a method is one action.", "Keep the attribute of the action's kind: [EntityAction] for an id-shaped bulk action, [ApiAction] for a single-body operation.");
                    continue;
                }

                string name = entityAction?.Name ?? apiAction!.Name;
                if (!Admit(name, described, projected, taken))
                {
                    continue;
                }

                ActionDescriptor? action;
                if (entityAction is not null)
                {
                    action = ActionMethods.Entity(service, method, entityAction, stack.Leaf, stack.KeyType!, problems);
                }
                else
                {
                    if (apiAction!.Resource is not null)
                    {
                        ReportAction(
                            $"The API action '{apiAction.Name}' of {described} names the resource '{apiAction.Resource}'; an action of a stack's service is secured by the stack's resource, {resource}.",
                            "Remove Resource from the attribute; name a different securable action with Action if the operation needs one.");
                        continue;
                    }

                    SecurableRef? securable = apiAction.MemberOnly ? null : new SecurableRef(resource, apiAction.Action ?? StackNames.ActionOf(apiAction.Name));
                    action = ActionMethods.Api(service, method, apiAction, securable, problems);
                }

                if (action is not null)
                {
                    actions.Add(action);
                    Methods[action] = new ActionMethod(service, method);
                }
            }

            // --- Each companion's API actions, in registration order ---
            foreach (Type companion in stack.Companions)
            {
                IReadOnlyList<MethodInfo> methods = ActionMethods.Declared(companion);
                if (!methods.Any(static method => method.IsDefined(typeof(ApiActionAttribute), inherit: true)))
                {
                    ReportAction(
                        $"The stack companion {TypeDisplay.Qualified(companion)} of {resource} declares no [ApiAction] method, so it adds nothing to the stack.",
                        "Mark the companion's operations with [ApiAction], or remove the companion.");
                }

                HashSet<string> reserved = [.. projected.Where(static segment => !ExcelSegments.Contains(segment))];
                foreach (MethodInfo method in methods)
                {
                    string described = ActionMethods.Describe(companion, method);
                    if (method.GetCustomAttribute<ApiActionAttribute>(inherit: true) is not ApiActionAttribute apiAction)
                    {
                        ReportAction(
                            $"The method {described} declares [EntityAction], but {TypeDisplay.Short(companion)} is a stack companion; an entity action is declared on the stack's service.",
                            $"Move the action to {TypeDisplay.Short(stack.ServiceType!)}, or declare it as an [ApiAction].");
                        continue;
                    }

                    if (!Admit(apiAction.Name, described, reserved, taken))
                    {
                        continue;
                    }

                    if (apiAction.Resource is not null)
                    {
                        ReportAction(
                            $"The API action '{apiAction.Name}' of {described} names the resource '{apiAction.Resource}'; an action of a stack companion is secured by the stack's resource, {resource}.",
                            "Remove Resource from the attribute; name a different securable action with Action if the operation needs one.");
                        continue;
                    }

                    SecurableRef? securable = apiAction.MemberOnly ? null : new SecurableRef(resource, apiAction.Action ?? StackNames.ActionOf(apiAction.Name));
                    if (ActionMethods.Api(companion, method, apiAction, securable, problems) is ApiActionDescriptor action)
                    {
                        actions.Add(action);
                        Methods[action] = new ActionMethod(companion, method);
                    }
                }
            }

            return actions;
        }

        /// <summary>
        ///     Whether an action's name is admitted on its stack: well formed, not a standard segment
        ///     the stack projects, and not another action's. Reports the name otherwise.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="described">The method, as problems name it.</param>
        /// <param name="reserved">The segments the name may not take.</param>
        /// <param name="taken">The names already taken, with what took each.</param>
        /// <returns>True when admitted; the name is then taken.</returns>
        private bool Admit(string name, string described, HashSet<string> reserved, Dictionary<string, string> taken)
        {
            if (!StackNames.IsActionName(name))
            {
                ReportAction(
                    $"The action of {described} is named '{name}', which is not one or more kebab-case segments joined by '/'.",
                    "Name the action like 'invite', 'take-over' or 'preferences/set'.");
                return false;
            }

            if (reserved.Contains(name))
            {
                ReportAction(
                    $"The action of {described} is named '{name}', a standard segment the stack projects.",
                    $"Rename the action; '{name}' belongs to the stack's standard operation.");
                return false;
            }

            if (taken.TryGetValue(name, out string? earlier))
            {
                ReportAction(
                    $"The action of {described} is named '{name}', which {earlier} already takes; an action's name is unique per stack.",
                    "Give each action of the stack its own name.");
                return false;
            }

            taken[name] = described;
            return true;
        }

        /// <summary>
        ///     The details extras a stack serves: the names its service and each details contributor
        ///     declare, then the platform's <c>permissions</c> when the stack serves details and has
        ///     something to refuse. Reports a contributor declaring a name twice, and any producer
        ///     declaring <c>permissions</c>.
        /// </summary>
        /// <param name="stack">The stack.</param>
        /// <param name="operations">The stack's operations.</param>
        /// <param name="activatable">Whether the entity is activatable.</param>
        /// <param name="actions">The stack's actions.</param>
        /// <returns>The extras, in first-declared order.</returns>
        private ReadOnlyCollection<string> Extras(ComposedStack stack, StackOperations operations, bool activatable, List<ActionDescriptor> actions)
        {
            List<string> extras = [];
            foreach (Type producer in (Type[])[stack.ServiceType!, .. stack.Components.Where(static c => c.Kind == ComponentKind.DetailsContributor).Select(static c => c.ComponentType)])
            {
                // A class's extras are the names on it and on its base classes.
                List<string> names = [.. producer.GetCustomAttributes<DetailsExtrasAttribute>(inherit: true).SelectMany(static attribute => attribute.Names)];
                bool contributor = producer != stack.ServiceType;
                foreach (IGrouping<string, string> repeated in names.GroupBy(static name => name, StringComparer.Ordinal).Where(static group => group.Count() > 1))
                {
                    if (contributor)
                    {
                        problems.Add(new CompositionProblem(
                            CoreCheckNames.StackComponents,
                            $"The details contributor {TypeDisplay.Qualified(producer)} declares the extra '{repeated.Key}' {repeated.Count()} times.",
                            "Declare each extra once across the class and its base classes."));
                    }
                }

                if (names.Contains(PermissionsExtra, StringComparer.Ordinal))
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.StackComponents,
                        $"The {(contributor ? "details contributor" : "service")} {TypeDisplay.Qualified(producer)} declares the extra '{PermissionsExtra}', which the platform produces itself.",
                        "Rename the extra; 'permissions' is the platform's record permissions."));
                }

                extras.AddRange(names.Where(static name => !string.Equals(name, PermissionsExtra, StringComparison.Ordinal)));
            }

            // --- The platform's extra, on a stack whose rows can refuse an ability (spec 0015 §5.3 step 4) ---
            bool refusable = operations.HasFlag(StackOperations.Save)
                || operations.HasFlag(StackOperations.Delete)
                || activatable
                || actions.Exists(static action => action is EntityActionDescriptor { IsBuiltIn: false });
            if (operations.HasFlag(StackOperations.Details) && refusable)
            {
                extras.Add(PermissionsExtra);
            }

            return extras.Distinct(StringComparer.Ordinal).ToList().AsReadOnly();
        }

        /// <summary>
        ///     The stack's named filters: each contributor created once from the root provider, its
        ///     trees read once. Reports a contributor that cannot be created or read, a name outside the
        ///     grammar, a missing tree, and a name two contributors declare.
        /// </summary>
        /// <param name="stack">The stack.</param>
        /// <param name="resource">The stack's resource, as problems name it.</param>
        /// <returns>The filters by name, in declaration order.</returns>
        private ReadOnlyDictionary<string, FilterTree> NamedFilters(ComposedStack stack, string resource)
        {
            Dictionary<string, FilterTree> filters = new(StringComparer.Ordinal);
            Dictionary<string, Type> declaredBy = new(StringComparer.Ordinal);
            foreach (ComposedComponent component in stack.Components.Where(static component => component.Kind == ComponentKind.NamedFilterContributor))
            {
                string contributor = TypeDisplay.Qualified(component.ComponentType);
                IReadOnlyDictionary<string, FilterTree>? declared;
                try
                {
                    // Created from the root provider, so a contributor takes singletons only.
                    object instance = ActivatorUtilities.CreateInstance(root, component.ComponentType);
                    declared = (IReadOnlyDictionary<string, FilterTree>?)component.ComponentType
                        .GetInterfaceMap(ComponentContracts.For(ComponentKind.NamedFilterContributor, stack.Leaf))
                        .TargetMethods.Single(static method => method.ReturnType == typeof(IReadOnlyDictionary<string, FilterTree>))
                        .Invoke(instance, null);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    Exception cause = exception is TargetInvocationException { InnerException: Exception inner } ? inner : exception;
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.StackComponents,
                        $"The named filter contributor {contributor} of {resource} could not be created or read at startup: {cause.Message}",
                        "Give the contributor a constructor that takes singletons only, and build its filters without a tenant scope."));
                    continue;
                }

                foreach ((string name, FilterTree tree) in declared ?? new Dictionary<string, FilterTree>())
                {
                    if (!StackNames.IsNamedFilterName(name))
                    {
                        problems.Add(new CompositionProblem(
                            CoreCheckNames.StackComponents,
                            $"The named filter contributor {contributor} of {resource} declares the filter '{name}', a name outside ^[a-z][a-z0-9-]*$.",
                            "Name the filter in lowercase letters, digits and hyphens, starting with a letter: 'mine', 'shared-with-me'."));
                        continue;
                    }

                    if (tree is null)
                    {
                        problems.Add(new CompositionProblem(
                            CoreCheckNames.StackComponents,
                            $"The named filter contributor {contributor} of {resource} declares the filter '{name}' with no tree.",
                            "Give every named filter its tree."));
                        continue;
                    }

                    if (declaredBy.TryGetValue(name, out Type? earlier))
                    {
                        problems.Add(new CompositionProblem(
                            CoreCheckNames.StackComponents,
                            $"The named filter '{name}' of {resource} is declared by both {TypeDisplay.Qualified(earlier)} and {contributor}.",
                            "Give each of the stack's named filters one contributor."));
                        continue;
                    }

                    declaredBy[name] = component.ComponentType;
                    filters[name] = tree;
                }
            }

            return filters.AsReadOnly();
        }

        /// <summary>The stack's ceilings: the platform's defaults, or those its <see cref="StackAttribute" /> declares, each positive.</summary>
        /// <param name="declared">The stack's attribute, if any.</param>
        /// <param name="resource">The stack's resource, as problems name it.</param>
        /// <returns>The ceilings; the defaults when a declared one is refused.</returns>
        private StackLimits Limits(StackAttribute? declared, string resource)
        {
            if (declared is null)
            {
                return StackLimits.Default;
            }

            try
            {
                return new StackLimits(
                    declared.MaxTake,
                    declared.CountCap,
                    declared.MaxSkipWindow,
                    declared.MaxSaveCount,
                    declared.MaxRowsPerSave,
                    declared.MaxIds,
                    declared.MaxDeleteByQueryRows,
                    declared.MaxExpandDepth,
                    declared.MaxValidationRounds,
                    declared.MaxSearchLength,
                    declared.MaxExtras);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                problems.Add(new CompositionProblem(
                    CoreCheckNames.Stacks,
                    $"The stack of {resource} declares [Stack({exception.ParamName} = {exception.ActualValue})]; every ceiling is positive, since a ceiling of zero would refuse every request.",
                    $"Give {exception.ParamName} a positive value, or leave it at the platform's default."));
                return StackLimits.Default;
            }
        }

        /// <summary>
        ///     The platform's default select: the id, the natural keys, the name, the code, the active
        ///     flag and the four audit columns, each that exists, once.
        /// </summary>
        /// <param name="metadata">The entity metadata.</param>
        /// <returns>The select text.</returns>
        private static string DefaultSelect(EntityMetadata metadata)
        {
            List<string> columns = [metadata.Key.Name, .. metadata.NaturalKeys.Select(static natural => natural.Property.Name), .. NameColumns(metadata)];
            columns.AddRange(((string[])[CodeProperty, nameof(IActivatable.IsActive), .. AuditColumns]).Where(column => Has(metadata, column)));
            return string.Join(", ", columns.Distinct(StringComparer.Ordinal));
        }

        /// <summary>
        ///     The related projection: the <see cref="RelatedSelectAttribute" /> columns, else the id,
        ///     the display-name columns, the code and every avatar blob column (spec 0012 §2.5).
        /// </summary>
        /// <param name="metadata">The entity metadata.</param>
        /// <returns>The projection's columns, in order.</returns>
        private static OrderedNameSet RelatedSelect(EntityMetadata metadata)
        {
            if (EntityShapes.GetAttribute<RelatedSelectAttribute>(metadata.ClrType) is RelatedSelectAttribute declared)
            {
                return new OrderedNameSet((declared.Select ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            }

            // --- The display name: its source or every twin of its group, then its prefix ---
            DisplayNameMetadata display = metadata.DisplayName;
            List<string> columns = [metadata.Key.Name];
            columns.AddRange(display.Group is MultilingualGroup group
                ? ((PropertyMetadata?[])[group.Primary, group.Secondary, group.Tertiary]).OfType<PropertyMetadata>().Select(static property => property.Name)
                : [display.Source.Name]);
            if (display.Prefix is PropertyMetadata prefix)
            {
                columns.Add(prefix.Name);
            }

            if (Has(metadata, CodeProperty))
            {
                columns.Add(CodeProperty);
            }

            // --- Every avatar, so a navigation cell can show its picture ---
            columns.AddRange(metadata.BlobReferences
                .Where(blob => metadata.ClrType.GetProperty(blob.Property.Name) is PropertyInfo member
                    && EntityShapes.GetAttribute<BlobReferenceAttribute>(member)?.Preset == BlobPreset.Avatar)
                .Select(static blob => blob.Property.Name));
            return new OrderedNameSet(columns);
        }

        /// <summary>
        ///     The default details expansion: every foreign-key navigation of the entity, and of each
        ///     child collection at any depth as <c>Collection.Navigation</c>, the navigation back to the
        ///     parent excluded.
        /// </summary>
        /// <param name="metadata">The entity metadata.</param>
        /// <returns>The navigation paths.</returns>
        private static ReadOnlyCollection<string> DetailsExpand(EntityMetadata metadata)
        {
            List<string> paths = [];
            Collect(metadata, prefix: string.Empty, parentKey: null, paths);
            return paths.AsReadOnly();

            static void Collect(EntityMetadata entity, string prefix, string? parentKey, List<string> paths)
            {
                paths.AddRange(entity.References
                    .Where(reference => reference.ForeignKey.Name != parentKey)
                    .Select(reference => prefix + reference.Navigation));
                foreach (ChildCollectionMetadata child in entity.Children)
                {
                    Collect(child.Child, $"{prefix}{child.Property}.", child.ParentKey.Name, paths);
                }
            }
        }

        /// <summary>
        ///     Adds the surfaces of an entity's child collections, then of theirs: each owned by the
        ///     surface above it. A child type that opts out gets no surface, and neither do its own
        ///     children, whose owner would be missing.
        /// </summary>
        /// <param name="stack">The root stack's descriptor.</param>
        /// <param name="parent">The entity whose collections are surfaced.</param>
        /// <param name="owner">The resource of the parent's surface: the stack's, or the child surface above.</param>
        /// <param name="surfaces">Receives the surfaces.</param>
        private void AddChildQueries(StackDescriptor stack, EntityMetadata parent, string owner, List<ChildQueryDescriptor> surfaces)
        {
            foreach (ChildCollectionMetadata collection in parent.Children)
            {
                EntityMetadata child = collection.Child;
                ApiResourceAttribute? api = EntityShapes.GetAttribute<ApiResourceAttribute>(child.ClrType);
                if (api is { Queryable: false })
                {
                    continue;
                }

                // --- The owner rule: the child reaches its owner's row through its parent key's navigation ---
                if (child.References.FirstOrDefault(reference => reference.ForeignKey.Name == collection.ParentKey.Name) is not ReferenceMetadata navigation)
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.Stacks,
                        $"The child entity {child.Name} of the stack of {stack.Resource} has no navigation through its parent key {collection.ParentKey.Name}, so its query surface cannot reach its owner's row.",
                        $"Name the parent key after its target with an Id suffix, so its navigation derives from it."));
                    continue;
                }

                List<(string Property, SearchKind Kind)> searchable = [.. child.Properties
                    .Where(static property => property.IsSearchable)
                    .Select(static property => (property.Name, property.SearchKind ?? SearchKind.Contains))];
                if (searchable.Count == 0)
                {
                    // The search convention: the name group and the code, all by contains (spec 0015 §5.2.1).
                    searchable.AddRange(((string[])["Name", "Name2", "Name3", CodeProperty])
                        .Where(column => child.Properties.Any(property => property.Name == column && property.ClrType == typeof(string)))
                        .Select(static column => (column, SearchKind.Contains)));
                }

                List<string> select = [child.Key.Name, collection.ParentKey.Name, .. child.NaturalKeys.Select(static natural => natural.Property.Name), .. NameColumns(child)];
                if (Has(child, CodeProperty))
                {
                    select.Add(CodeProperty);
                }

                surfaces.Add(new ChildQueryDescriptor(
                    Resource: child.Name,
                    ResourceSegment: api?.Segment ?? StackNames.Kebab(child.Table.Name),
                    EntityType: child.ClrType,
                    OwnerResource: owner,
                    OwnerNavigation: navigation.Navigation,
                    Collection: collection.Property,
                    SearchableProperties: searchable.AsReadOnly(),
                    DefaultSelect: string.Join(", ", select.Distinct(StringComparer.Ordinal)),
                    RelatedSelect: RelatedSelect(child),
                    DefaultOrderBy: "Id desc",
                    Exportable: stack.Operations.HasFlag(StackOperations.Export),
                    Limits: stack.Limits,
                    Mcp: stack.Mcp,

                    // The two child exports join here when the Excel feature's ChildExportOperations exists.
                    Actions: []));

                AddChildQueries(stack, child, child.Name, surfaces);
            }
        }

        /// <summary>The name columns the default projections carry: the multilingual group <c>Name</c>, else a plain <c>Name</c>.</summary>
        /// <param name="metadata">The entity metadata.</param>
        /// <returns>The columns.</returns>
        private static IEnumerable<string> NameColumns(EntityMetadata metadata)
        {
            return metadata.MultilingualGroups.FirstOrDefault(static group => group.Name == NameProperty) is MultilingualGroup group
                ? ((PropertyMetadata?[])[group.Primary, group.Secondary, group.Tertiary]).OfType<PropertyMetadata>().Select(static property => property.Name)
                : Has(metadata, NameProperty) ? [NameProperty] : [];
        }

        /// <summary>Whether an entity maps a property.</summary>
        /// <param name="metadata">The entity metadata.</param>
        /// <param name="name">The property's name.</param>
        /// <returns>True when it does.</returns>
        private static bool Has(EntityMetadata metadata, string name)
        {
            return metadata.Properties.Any(property => property.Name == name);
        }

        /// <summary>Adds an action problem.</summary>
        /// <param name="problem">What is wrong.</param>
        /// <param name="fix">The remedy.</param>
        private void ReportAction(string problem, string fix)
        {
            problems.Add(new CompositionProblem(CoreCheckNames.StackActions, problem, fix));
        }
    }

    /// <summary>The method behind a declared action, and the class it is reached through.</summary>
    /// <param name="Handler">The class the invoker resolves: the stack's service, the closed companion or the routed service.</param>
    /// <param name="Method">The method.</param>
    internal sealed record ActionMethod(Type Handler, MethodInfo Method);
}
