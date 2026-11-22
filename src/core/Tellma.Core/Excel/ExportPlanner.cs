// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Reflection;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Plans the two entity workbooks. A display plan wraps the columns its caller expanded in one
    ///     data sheet; an editable plan derives the parent sheet's columns from the entity's editable
    ///     properties and references, adds a sheet per selected collection at every depth, and builds
    ///     the one parent query whose dependents carry every child sheet's rows. Pure: it reads only
    ///     the metadata, the request and the context it is handed.
    /// </summary>
    /// <param name="valueSets">The value sets, whose stored values an editable value-set column offers as its validation list.</param>
    /// <param name="platformVersion">The platform version the manifest records.</param>
    internal sealed class ExportPlanner(IValueSetRegistry valueSets, string platformVersion)
    {
        /// <summary>The header and path of the identity column.</summary>
        public const string IdColumn = "Id";

        /// <summary>The header and path of the concurrency-stamp column.</summary>
        public const string StampColumn = "Stamp";

        /// <summary>The property the stamp column reads.</summary>
        public const string StampSource = "ModifiedAt";

        /// <summary>What joins the labels of a reference column's header.</summary>
        public const string LabelSeparator = " / ";

        /// <summary>What joins the labels of a child sheet's name.</summary>
        public const string SheetSeparator = " · ";

        /// <summary>The value sets.</summary>
        private readonly IValueSetRegistry _valueSets = valueSets ?? throw new ArgumentNullException(nameof(valueSets));

        /// <summary>The platform version the manifest records.</summary>
        private readonly string _platformVersion = !string.IsNullOrWhiteSpace(platformVersion)
            ? platformVersion
            : throw new ArgumentException("The platform version is required.", nameof(platformVersion));

        /// <summary>The Queryex type a property's CLR type reads as.</summary>
        /// <param name="clrType">The CLR type, nullable or not.</param>
        /// <returns>The type.</returns>
        public static QueryexType TypeOf(Type clrType)
        {
            ArgumentNullException.ThrowIfNull(clrType);
            Type type = Nullable.GetUnderlyingType(clrType) ?? clrType;
            return type switch
            {
                _ when type == typeof(bool) => QueryexType.QxBool,
                _ when type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
                    || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
                    || type == typeof(decimal) || type == typeof(double) || type == typeof(float) => QueryexType.QxNumeric,
                _ when type == typeof(Guid) => QueryexType.QxGuid,
                _ when type == typeof(DateOnly) => QueryexType.QxDate,
                _ when type == typeof(DateTime) => QueryexType.QxDateTime,
                _ when type == typeof(DateTimeOffset) => QueryexType.QxDateTimeOffset,
                _ when type == typeof(HierarchyId) => QueryexType.QxHierarchyId,

                // Text, enums and value sets, times of day: all read as text.
                _ => QueryexType.QxString,
            };
        }

        /// <summary>Plans a display export: one data sheet of the expanded columns over their query, and the manifest.</summary>
        /// <param name="entity">The exported entity.</param>
        /// <param name="columns">The sheet's columns, in request order.</param>
        /// <param name="query">The sheet's query, whose rows its caller folds to one value per column.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The plan.</returns>
        public ExcelWorkbookPlan PlanDisplay(EntityMetadata entity, IReadOnlyList<ExcelColumnSpec> columns, ExcelQuery query, ExcelContext context)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(columns);
            ArgumentNullException.ThrowIfNull(query);
            ArgumentNullException.ThrowIfNull(context);

            ExcelSheetPlan sheet = new(EntityPlural(entity, context), Collection: null, columns, new string?[columns.Count], query);
            return new ExcelWorkbookPlan(entity, ExportKind.Display, FileName(entity, context), [sheet], ExcelManifest.Header(entity, ExportKind.Display, context, _platformVersion));
        }

        /// <summary>
        ///     Plans an editable export: the parent sheet's identity, stamp, editable and reference
        ///     columns, a sheet per selected collection at every depth, and the one parent query whose
        ///     dependents carry the child sheets' rows.
        /// </summary>
        /// <param name="entity">The exported entity.</param>
        /// <param name="request">The request's columns, reference keys, collections, ids and ordering.</param>
        /// <param name="cap">The most parent rows the export writes.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="BadRequestException">
        ///     A listed column is not editable, a reference key names no reference or no scalar of its
        ///     target, or a collection is unknown or repeated among its siblings.
        /// </exception>
        public ExcelWorkbookPlan PlanEditable(EntityMetadata entity, ExportForImportRequest request, int cap, ExcelContext context)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cap, ExcelOptions.MaxWorksheetDataRows);

            // --- The child sheets first, depth first, so the parent query can carry their queries ---
            string parentLabel = EntityPlural(entity, context);
            List<ExcelSheetPlan> children = [];
            List<ExcelQuery> dependents = ChildSheets(entity, request.Children, [], parentLabel, string.Empty, "children", context, children);

            // --- The parent sheet: Id, Stamp, then the editable columns ---
            var columns = SheetColumns.ForParent(entity);
            AddEditable(columns, entity, collection: null, request.Columns, request.ReferenceKeys, "columns", "referenceKeys", context);

            // A by-query export counts its parents on the first page; ids are capped by their own limit.
            bool byQuery = request.Ids is null;
            ExcelQuery query = new(
                entity.Name,
                columns.Selects,
                request.OrderBy ?? entity.Key.Name,
                Restriction: null,
                RestrictionValues: null,
                Take: cap + 1,
                CountCap: byQuery ? cap : null,
                Dependents: dependents.Count == 0 ? null : dependents);

            IReadOnlyList<ExcelColumnSpec> specs = columns.Finish();
            ExcelSheetPlan parent = new(parentLabel, Collection: null, specs, new string?[specs.Count], query);
            return new ExcelWorkbookPlan(entity, ExportKind.ForImport, FileName(entity, context), [parent, .. children], ExcelManifest.Header(entity, ExportKind.ForImport, context, _platformVersion));
        }

        /// <summary>
        ///     The child sheet's name: the parent's plural label and each collection's label joined by
        ///     <c>" · "</c>, its leading segments dropped first (never the last) while the cleaned name
        ///     is longer than a sheet name may be. The writer cuts what is still too long and makes it unique.
        /// </summary>
        /// <param name="segments">The parent's plural label, then each collection's label from the parent down.</param>
        /// <returns>The name.</returns>
        public static string ChildSheetName(IReadOnlyList<string> segments)
        {
            ArgumentNullException.ThrowIfNull(segments);
            ArgumentOutOfRangeException.ThrowIfLessThan(segments.Count, 2, nameof(segments));
            int first = 0;
            while (first < segments.Count - 1 && SheetNames.Clean(string.Join(SheetSeparator, segments.Skip(first))).Length > SheetNames.MaxLength)
            {
                first++;
            }

            return string.Join(SheetSeparator, segments.Skip(first));
        }

        /// <summary>The download name of an entity workbook: its plural label and the request's instant in the display zone.</summary>
        /// <param name="entity">The exported entity.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The file name.</returns>
        private static string FileName(EntityMetadata entity, ExcelContext context)
        {
            return ExcelFileNames.Compose(context.Labels.EntityLabel(entity.ClrType, plural: true), entity.Name, context.Now, context.TimeZone);
        }

        /// <summary>An entity's plural label in the request culture, which names its sheet.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The label.</returns>
        private static string EntityPlural(EntityMetadata entity, ExcelContext context)
        {
            return context.Labels.EntityLabel(entity.ClrType, plural: true);
        }

        /// <summary>
        ///     Plans the sheets of an entity's selected collections, each followed by its own, and
        ///     returns their queries as the dependents of the entity's query.
        /// </summary>
        /// <param name="owner">The entity whose collections are planned.</param>
        /// <param name="selected">The selection: null for every collection at every depth, empty for none.</param>
        /// <param name="ownerLabels">The labels of the sheet name down to the owner: empty for the parent.</param>
        /// <param name="parentLabel">The parent's plural label, which every child sheet's name starts with.</param>
        /// <param name="ownerPath">The owner's collection path; empty for the parent.</param>
        /// <param name="location">Where the selection sits in the request, for a fault's path.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="sheets">Receives the sheets, depth first.</param>
        /// <returns>The owner's dependents, in collection order.</returns>
        private List<ExcelQuery> ChildSheets(
            EntityMetadata owner,
            IReadOnlyList<ExportCollection>? selected,
            IReadOnlyList<string> ownerLabels,
            string parentLabel,
            string ownerPath,
            string location,
            ExcelContext context,
            List<ExcelSheetPlan> sheets)
        {
            // --- The selection: every collection when null, else the listed ones, each once and known ---
            List<(ChildCollectionMetadata Collection, ExportCollection? Selection, string Location)> chosen = [];
            if (selected is null)
            {
                chosen.AddRange(owner.Children.Select(collection => (collection, (ExportCollection?)null, location)));
            }
            else
            {
                HashSet<string> seen = new(StringComparer.Ordinal);
                for (int index = 0; index < selected.Count; index++)
                {
                    string at = string.Create(CultureInfo.InvariantCulture, $"{location}[{index}]");
                    ExportCollection selection = selected[index] ?? throw Fault($"The collection at {at} is missing.", at);
                    ChildCollectionMetadata collection = owner.Children.FirstOrDefault(candidate => string.Equals(candidate.Property, selection.Collection, StringComparison.Ordinal))
                        ?? throw Fault($"The collection '{selection.Collection}' at {at} is not a child collection of {owner.Name}.", at, selection.Collection);
                    if (!seen.Add(collection.Property))
                    {
                        throw Fault($"The collection '{selection.Collection}' at {at} is selected twice under {owner.Name}.", at, selection.Collection);
                    }

                    chosen.Add((collection, selection, at));
                }

                // Sheets follow the entity's collection order, whatever order the request lists them in.
                chosen.Sort((x, y) => IndexOf(owner, x.Collection).CompareTo(IndexOf(owner, y.Collection)));
            }

            // --- Each chosen collection: its sheet, then its own collections' sheets ---
            List<ExcelQuery> dependents = [];
            foreach ((ChildCollectionMetadata collection, ExportCollection? selection, string at) in chosen)
            {
                EntityMetadata child = collection.Child;
                string path = ownerPath.Length == 0 ? collection.Property : $"{ownerPath}.{collection.Property}";
                List<string> labels = [.. ownerLabels, context.Labels.CollectionLabel(owner.ClrType, collection.Property)];

                // The sheet's place is held before its descendants', so the sheets stay depth first.
                int position = sheets.Count;
                List<ExcelQuery> grandchildren = ChildSheets(child, selection?.Children, labels, parentLabel, path, $"{at}.children", context, sheets);

                var columns = SheetColumns.ForChild(owner, collection, context);
                AddEditable(columns, child, collection, selection?.Columns, selection?.ReferenceKeys, $"{at}.columns", $"{at}.referenceKeys", context);
                ExcelQuery query = new(
                    child.Name,
                    columns.Selects,
                    $"{collection.ParentKey.Name}, {child.Key.Name}",
                    Restriction: null,
                    RestrictionValues: null,
                    Take: null,
                    CountCap: null,
                    Dependents: grandchildren.Count == 0 ? null : grandchildren);

                IReadOnlyList<ExcelColumnSpec> specs = columns.Finish();
                string name = ChildSheetName([parentLabel, .. labels]);
                sheets.Insert(position, new ExcelSheetPlan(name, path, specs, new string?[specs.Count], query));
                dependents.Add(query);
            }

            return dependents;

            static int IndexOf(EntityMetadata owner, ChildCollectionMetadata collection)
            {
                for (int index = 0; index < owner.Children.Count; index++)
                {
                    if (ReferenceEquals(owner.Children[index], collection))
                    {
                        return index;
                    }
                }

                return -1;
            }
        }

        /// <summary>
        ///     Adds an entity's editable columns in declaration order: each scalar, each multilingual
        ///     group as one column per tenant language, each foreign key as one reference column.
        ///     A subset keeps the listed columns and the natural key's.
        /// </summary>
        /// <param name="columns">The sheet's columns, receiving them.</param>
        /// <param name="entity">The sheet's entity.</param>
        /// <param name="collection">The collection a child sheet stands for; null for the parent sheet.</param>
        /// <param name="listed">The subset: null for every editable column.</param>
        /// <param name="referenceKeys">Per foreign key, the target property it is expressed in; null for the defaults.</param>
        /// <param name="columnsLocation">Where the subset sits in the request, for a fault's path.</param>
        /// <param name="keysLocation">Where the reference keys sit in the request, for a fault's path.</param>
        /// <param name="context">The request's context.</param>
        private void AddEditable(
            SheetColumns columns,
            EntityMetadata entity,
            ChildCollectionMetadata? collection,
            IReadOnlyList<string>? listed,
            IReadOnlyDictionary<string, string>? referenceKeys,
            string columnsLocation,
            string keysLocation,
            ExcelContext context)
        {
            // --- Every editable candidate, in declaration order ---
            List<Candidate> candidates = Candidates(entity, collection, referenceKeys ?? new Dictionary<string, string>(StringComparer.Ordinal), keysLocation, context);

            // --- The subset: each listed path names a candidate; the natural key's columns stay ---
            HashSet<Candidate>? kept = null;
            if (listed is not null)
            {
                kept = [];
                for (int index = 0; index < listed.Count; index++)
                {
                    string at = string.Create(CultureInfo.InvariantCulture, $"{columnsLocation}[{index}]");
                    string path = listed[index];
                    List<Candidate> named = [.. candidates.Where(candidate => path is not null && candidate.Names.Contains(path))];
                    if (named.Count == 0)
                    {
                        throw Fault($"The column '{path}' at {at} is not an editable column of {entity.Name}.", at, path);
                    }

                    kept.UnionWith(named);
                }
            }

            PropertyMetadata? natural = entity.NaturalKey;
            foreach (Candidate candidate in candidates)
            {
                if (kept is null || kept.Contains(candidate) || (natural is not null && candidate.Names.Contains(natural.Name)))
                {
                    columns.Add(candidate.Spec, candidate.Select);
                }
            }
        }

        /// <summary>
        ///     The editable columns an entity offers: every property the import writes, never one
        ///     the server owns or derives, a binary, a hierarchy path, a JSON document, a blob
        ///     reference, the child's parent key or one excluded from Excel.
        /// </summary>
        /// <param name="entity">The sheet's entity.</param>
        /// <param name="collection">The collection a child sheet stands for; null for the parent sheet.</param>
        /// <param name="referenceKeys">Per foreign key, the target property it is expressed in.</param>
        /// <param name="keysLocation">Where the reference keys sit in the request, for a fault's path.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The candidates, in declaration order.</returns>
        private List<Candidate> Candidates(
            EntityMetadata entity,
            ChildCollectionMetadata? collection,
            IReadOnlyDictionary<string, string> referenceKeys,
            string keysLocation,
            ExcelContext context)
        {
            List<Candidate> candidates = [];
            HashSet<string> referenced = new(StringComparer.Ordinal);
            foreach (PropertyMetadata property in entity.Properties)
            {
                if (!IsEditable(entity, property, collection))
                {
                    continue;
                }

                // --- A foreign key: one reference column in the key of its target ---
                if (entity.References.FirstOrDefault(reference => reference.ForeignKey.Name == property.Name) is ReferenceMetadata reference)
                {
                    referenced.Add(property.Name);
                    candidates.Add(ReferenceColumn(entity, reference, referenceKeys.GetValueOrDefault(property.Name), keysLocation, context));
                    continue;
                }

                // --- A multilingual group: one column per tenant language, listed at its primary ---
                if (property.MultilingualGroup is not null)
                {
                    if (entity.MultilingualGroups.FirstOrDefault(group => group.Primary.Name == property.Name) is MultilingualGroup group)
                    {
                        candidates.AddRange(GroupColumns(entity, group, context));
                    }

                    continue;
                }

                // --- Any other scalar: itself ---
                ExcelColumnSpec spec = new(context.Labels.PropertyLabel(entity.ClrType, property.Name), TypeOf(property.ClrType), Scale: null, [property.Name])
                {
                    Property = property,
                    Choices = Choices(property),
                };
                candidates.Add(new Candidate(spec, property.Name, new HashSet<string>(StringComparer.Ordinal) { property.Name }));
            }

            // --- A reference key that names no editable foreign key is a fault ---
            foreach (string foreignKey in referenceKeys.Keys)
            {
                if (!referenced.Contains(foreignKey))
                {
                    string at = $"{keysLocation}.{foreignKey}";
                    throw Fault($"The reference key '{foreignKey}' at {at} is not an editable foreign key of {entity.Name}.", at, foreignKey);
                }
            }

            return candidates;
        }

        /// <summary>The columns of a multilingual group: one per tenant language whose slot the group carries.</summary>
        /// <param name="entity">The entity.</param>
        /// <param name="group">The group.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The columns, in slot order.</returns>
        private static IEnumerable<Candidate> GroupColumns(EntityMetadata entity, MultilingualGroup group, ExcelContext context)
        {
            PropertyMetadata?[] slots = [group.Primary, group.Secondary, group.Tertiary];
            for (int slot = 0; slot < context.TenantLanguages.Count && slot < slots.Length; slot++)
            {
                if (slots[slot] is not PropertyMetadata twin)
                {
                    continue;
                }

                // The column path is language-relative: the group's name with the slot's language.
                ExcelColumnSpec spec = new(context.Labels.PropertyLabel(entity.ClrType, twin.Name), TypeOf(twin.ClrType), Scale: null, [group.Primary.Name])
                {
                    Property = twin,
                    Language = context.TenantLanguages[slot],
                };
                yield return new Candidate(spec, twin.Name, new HashSet<string>(StringComparer.Ordinal) { group.Primary.Name, twin.Name });
            }
        }

        /// <summary>
        ///     A foreign key's reference column, in the key the request names, else the target's
        ///     natural key, else its surrogate id. A multilingual key reads the slot of its language,
        ///     the primary language's for the group's primary.
        /// </summary>
        /// <param name="entity">The entity carrying the foreign key.</param>
        /// <param name="reference">The reference.</param>
        /// <param name="requested">The target property the request names; null for the default.</param>
        /// <param name="keysLocation">Where the reference keys sit in the request, for a fault's path.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The column.</returns>
        /// <exception cref="BadRequestException">The requested key is not a scalar property of the target, or a language slot the tenant lacks.</exception>
        private Candidate ReferenceColumn(EntityMetadata entity, ReferenceMetadata reference, string? requested, string keysLocation, ExcelContext context)
        {
            EntityMetadata target = reference.Target;
            string navigation = reference.Navigation;
            string at = $"{keysLocation}.{reference.ForeignKey.Name}";

            // --- The key: the requested scalar, the natural key, else the surrogate id ---
            PropertyMetadata key = requested is not null
                ? target.Properties.FirstOrDefault(property => property.Name == requested && IsKeyable(target, property))
                    ?? throw Fault($"The reference key '{requested}' at {at} is not a scalar property of {target.Name}.", at, requested)
                : target.NaturalKey ?? target.Key;

            bool surrogate = key.Name == target.Key.Name;
            string keyName = key.Name;
            string? language = null;

            // --- A multilingual key: its slot's language, and the language-relative path ---
            if (key.MultilingualGroup is not null && target.MultilingualGroups.FirstOrDefault(group => Slot(group, key.Name) > 0) is MultilingualGroup keyGroup)
            {
                int slot = Slot(keyGroup, key.Name);
                if (slot > context.TenantLanguages.Count)
                {
                    throw Fault($"The reference key '{key.Name}' at {at} is a language the tenant does not configure.", at, key.Name);
                }

                language = context.TenantLanguages[slot - 1];
                keyName = keyGroup.Primary.Name;
            }

            string header = string.Concat(
                context.Labels.NavigationLabel(entity.ClrType, navigation),
                LabelSeparator,
                surrogate ? IdColumn : context.Labels.PropertyLabel(target.ClrType, key.Name));
            ExcelColumnSpec spec = new(header, TypeOf(key.ClrType), Scale: null, [navigation, keyName])
            {
                Property = key,
                Role = ExcelColumnRole.Reference,
                Language = language,
                ReferenceEntity = target.Name,
                KeyKind = surrogate ? ExcelKeyKind.Surrogate : ExcelKeyKind.Natural,
                Choices = Choices(key),
            };

            string column = $"{navigation}.{keyName}";
            return new Candidate(spec, $"{navigation}.{key.Name}", new HashSet<string>(StringComparer.Ordinal) { reference.ForeignKey.Name, navigation, column });
        }

        /// <summary>A twin's one-based slot in its group; 0 when the group does not hold it.</summary>
        /// <param name="group">The group.</param>
        /// <param name="name">The property's name.</param>
        /// <returns>The slot.</returns>
        private static int Slot(MultilingualGroup group, string name)
        {
            return group.Primary.Name == name ? 1 : group.Secondary?.Name == name ? 2 : group.Tertiary?.Name == name ? 3 : 0;
        }

        /// <summary>The stored values a value-set column offers; null for a property outside every value set.</summary>
        /// <param name="property">The property.</param>
        /// <returns>The values, in the set's order.</returns>
        private IReadOnlyList<string>? Choices(PropertyMetadata property)
        {
            return property.ValueSet is string set && _valueSets.Find(set) is ValueSetDescriptor descriptor
                ? [.. descriptor.Values.Select(static entry => entry.Value)]
                : null;
        }

        /// <summary>Whether a property is an editable column of its sheet.</summary>
        /// <param name="entity">The sheet's entity.</param>
        /// <param name="property">The property.</param>
        /// <param name="collection">The collection a child sheet stands for; null for the parent sheet.</param>
        /// <returns>True when the import writes it from the sheet.</returns>
        private static bool IsEditable(EntityMetadata entity, PropertyMetadata property, ChildCollectionMetadata? collection)
        {
            return property.Ownership is PropertyOwnership.Editable or PropertyOwnership.WriteOnce
                && property.Name != entity.Key.Name
                && !property.ExcludedFromExcel
                && (collection is null || property.Name != collection.ParentKey.Name)
                && !entity.BlobReferences.Any(blob => blob.Property.Name == property.Name)
                && IsKeyable(entity, property);
        }

        /// <summary>Whether a property's values fit a cell: never a binary, a hierarchy path or a JSON document.</summary>
        /// <param name="entity">The entity declaring it.</param>
        /// <param name="property">The property.</param>
        /// <returns>True when they do.</returns>
        private static bool IsKeyable(EntityMetadata entity, PropertyMetadata property)
        {
            Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            return type != typeof(byte[])
                && type != typeof(HierarchyId)
                && !(entity.ClrType.GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance) is PropertyInfo info
                    && EntityShapes.GetAttribute<JsonColumnAttribute>(info) is not null);
        }

        /// <summary>A request fault, with the path at fault and the name it carried.</summary>
        /// <param name="detail">The English sentence.</param>
        /// <param name="path">The path in the request.</param>
        /// <param name="name">The name at fault; null when there is none.</param>
        /// <returns>The exception.</returns>
        private static BadRequestException Fault(string detail, string path, string? name = null)
        {
            Dictionary<string, object?> arguments = new(StringComparer.Ordinal) { ["path"] = path };
            if (name is not null)
            {
                arguments["name"] = name;
            }

            return new BadRequestException(detail, arguments);
        }

        /// <summary>One editable column a sheet may carry: its spec, its select term and the paths that name it in a subset.</summary>
        /// <param name="Spec">The column.</param>
        /// <param name="Select">The select term its values come from.</param>
        /// <param name="Names">The paths a request's subset names it by.</param>
        private sealed record Candidate(ExcelColumnSpec Spec, string Select, IReadOnlySet<string> Names);

        /// <summary>A sheet's columns as they are planned, each with its select term, its headers made unique at the end.</summary>
        private sealed class SheetColumns
        {
            /// <summary>The columns, in order.</summary>
            private readonly List<ExcelColumnSpec> _columns = [];

            /// <summary>The select terms, parallel to the columns.</summary>
            public List<string> Selects { get; } = [];

            /// <summary>A parent sheet's leading columns: the identity, then the stamp when the entity has one.</summary>
            /// <param name="entity">The parent entity.</param>
            /// <returns>The columns.</returns>
            public static SheetColumns ForParent(EntityMetadata entity)
            {
                SheetColumns columns = new();
                columns.Add(IdSpec(entity), entity.Key.Name);
                if (entity.Properties.Any(static property => property.Name == StampSource))
                {
                    columns.Add(new ExcelColumnSpec(StampColumn, QueryexType.QxString, Scale: null, [StampColumn]) { Role = ExcelColumnRole.Stamp }, StampSource);
                }

                return columns;
            }

            /// <summary>A child sheet's leading columns: the reference to its owner row, then its own identity.</summary>
            /// <param name="owner">The entity owning the collection.</param>
            /// <param name="collection">The collection.</param>
            /// <param name="context">The request's context.</param>
            /// <returns>The columns.</returns>
            public static SheetColumns ForChild(EntityMetadata owner, ChildCollectionMetadata collection, ExcelContext context)
            {
                EntityMetadata child = collection.Child;
                string navigation = child.References.FirstOrDefault(reference => reference.ForeignKey.Name == collection.ParentKey.Name)?.Navigation
                    ?? collection.ParentKey.Name;
                ExcelColumnSpec link = new(
                    string.Concat(context.Labels.NavigationLabel(child.ClrType, navigation), LabelSeparator, IdColumn),
                    TypeOf(collection.ParentKey.ClrType),
                    Scale: null,
                    [navigation, IdColumn])
                {
                    Property = collection.ParentKey,
                    Role = ExcelColumnRole.ParentReference,
                    ReferenceEntity = owner.Name,
                };

                SheetColumns columns = new();
                columns.Add(link, collection.ParentKey.Name);
                columns.Add(IdSpec(child), child.Key.Name);
                return columns;
            }

            /// <summary>Adds a column.</summary>
            /// <param name="column">The column.</param>
            /// <param name="select">The select term its values come from.</param>
            public void Add(ExcelColumnSpec column, string select)
            {
                _columns.Add(column);
                Selects.Add(select);
            }

            /// <summary>
            ///     The columns with their headers made unique: headers that coincide, ignoring case,
            ///     each get the column's path in parentheses.
            /// </summary>
            /// <returns>The columns.</returns>
            public IReadOnlyList<ExcelColumnSpec> Finish()
            {
                Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);
                foreach (ExcelColumnSpec column in _columns)
                {
                    counts[column.Header] = counts.GetValueOrDefault(column.Header) + 1;
                }

                return [.. _columns.Select(column => counts[column.Header] > 1 && column.PathText is string path
                    ? column with { Header = $"{column.Header} ({path})" }
                    : column)];
            }

            /// <summary>An entity's identity column: a number for an <c>int</c> key, text for a <c>long</c> one.</summary>
            /// <param name="entity">The entity.</param>
            /// <returns>The column.</returns>
            private static ExcelColumnSpec IdSpec(EntityMetadata entity)
            {
                return new ExcelColumnSpec(IdColumn, TypeOf(entity.Key.ClrType), Scale: null, [IdColumn])
                {
                    Property = entity.Key,
                    Role = ExcelColumnRole.Id,
                };
            }
        }
    }
}
