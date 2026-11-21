// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Queryex;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     A display export's columns expanded into the select terms they read, and folded back: a
    ///     navigation reads its target's display-name columns and writes the display name, a
    ///     multilingual group reads the content slot's twin and the first slot and writes the one
    ///     that is not blank, anything else reads and writes itself. Each term is selected once.
    /// </summary>
    internal sealed partial class DisplayColumns
    {
        /// <summary>The columns, parallel to the request's.</summary>
        private readonly List<ExcelColumnSpec> _specs = [];

        /// <summary>How each column folds its terms into one value, parallel to the columns.</summary>
        private readonly List<Func<IReadOnlyList<object?>, object?>> _folds = [];

        /// <summary>The select terms, each once.</summary>
        private readonly List<string> _selects = [];

        /// <summary>Each select term's index.</summary>
        private readonly Dictionary<string, int> _indexes = new(StringComparer.Ordinal);

        /// <summary>The sheet's columns, in request order.</summary>
        public IReadOnlyList<ExcelColumnSpec> Specs => _specs;

        /// <summary>The query's select terms, in first-use order, each once.</summary>
        public IReadOnlyList<string> SelectPaths => _selects;

        /// <summary>
        ///     The content slot of a request: the one-based slot whose tenant language is the request
        ///     culture's language, or one of its parents', else the first.
        /// </summary>
        /// <param name="context">The request's context.</param>
        /// <returns>The slot: 1, 2 or 3.</returns>
        public static int ContentSlot(ExcelContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            for (CultureInfo culture = context.Culture; !string.IsNullOrEmpty(culture.Name); culture = culture.Parent)
            {
                for (int index = 0; index < context.TenantLanguages.Count; index++)
                {
                    if (string.Equals(context.TenantLanguages[index], culture.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return index + 1;
                    }
                }
            }

            return 1;
        }

        /// <summary>Expands a display request's columns.</summary>
        /// <param name="entity">The exported entity.</param>
        /// <param name="columns">The request's columns, already checked for count and header length.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="engine">The Queryex engine, which types every column that is neither a navigation nor a group.</param>
        /// <param name="schema">The query schema in the tenant's content-language shape.</param>
        /// <param name="displayNames">The display-name renderer.</param>
        /// <returns>The expansion.</returns>
        /// <exception cref="InvalidQueryException">A column's expression does not compile or aggregates; each diagnostic is located at its column.</exception>
        /// <exception cref="BadRequestException">A column's expression is a list, not one value.</exception>
        public static DisplayColumns Expand(
            EntityMetadata entity,
            IReadOnlyList<ExportColumn> columns,
            ExcelContext context,
            QueryexEngine engine,
            QueryexSchema schema,
            IDisplayNames displayNames)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(columns);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(displayNames);

            DisplayColumns expansion = new();
            int slot = ContentSlot(context);
            var shape = (MultilingualShape)Math.Clamp(context.TenantLanguages.Count, 1, 3);
            EntityDescriptor root = schema.FindEntity(entity.Name)
                ?? throw new InvalidOperationException($"The query schema maps no entity named {entity.Name}.");

            List<QueryexDiagnostic> diagnostics = [];
            for (int index = 0; index < columns.Count; index++)
            {
                ExportColumn column = columns[index];
                string expression = column.Expression.Trim();
                string? header = string.IsNullOrWhiteSpace(column.Header) ? null : column.Header;
                switch (Resolve(entity, expression))
                {
                    // --- A navigation: its target's display name, rendered in the content slot ---
                    case Resolved { Kind: TermKind.Navigation } navigation:
                        expansion.AddNavigation(navigation, expression, header, slot, shape, context, displayNames);
                        break;

                    // --- A group anchor: the content slot's twin, else the first slot's ---
                    case Resolved { Kind: TermKind.Anchor } anchor:
                        expansion.AddAnchor(anchor, header, slot, context);
                        break;

                    // --- A scalar path or an expression: itself, typed by the engine ---
                    case var resolved:
                        if (Type(engine, schema, root, expression, index, diagnostics) is QueryexType type)
                        {
                            expansion.AddValue(resolved, expression, header, column.Expression, type, context);
                        }

                        break;
                }
            }

            return diagnostics.Count > 0 ? throw new InvalidQueryException(diagnostics) : expansion;
        }

        /// <summary>Folds one row of select terms into one value per column.</summary>
        /// <param name="row">The row, one value per select term.</param>
        /// <returns>The folded row, one value per column.</returns>
        public IReadOnlyList<object?> Fold(IReadOnlyList<object?> row)
        {
            ArgumentNullException.ThrowIfNull(row);
            object?[] folded = new object?[_folds.Count];
            for (int index = 0; index < folded.Length; index++)
            {
                folded[index] = _folds[index](row);
            }

            return folded;
        }

        /// <summary>The path grammar: identifiers joined by dots.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant)]
        private static partial Regex PathPattern();

        /// <summary>
        ///     Resolves an expression as a path over the entity's metadata: navigations, then a
        ///     navigation, a group anchor or a scalar.
        /// </summary>
        /// <param name="entity">The root entity.</param>
        /// <param name="expression">The trimmed expression.</param>
        /// <returns>The path; null when the expression is not one the metadata resolves.</returns>
        private static Resolved? Resolve(EntityMetadata entity, string expression)
        {
            if (!PathPattern().IsMatch(expression))
            {
                return null;
            }

            string[] segments = expression.Split('.');
            List<(EntityMetadata Owner, string Navigation)> navigations = [];
            EntityMetadata current = entity;

            // --- Every segment but the last is a navigation, walked to its target ---
            foreach (string segment in segments[..^1])
            {
                if (current.References.FirstOrDefault(reference => reference.Navigation == segment) is not ReferenceMetadata reference)
                {
                    return null;
                }

                navigations.Add((current, segment));
                current = reference.Target;
            }

            // --- The last: a navigation, a group's primary (its anchor), or a scalar ---
            string last = segments[^1];
            if (current.References.FirstOrDefault(reference => reference.Navigation == last) is ReferenceMetadata final)
            {
                navigations.Add((current, last));
                return new Resolved(TermKind.Navigation, segments, navigations, current, final.Target, Property: null, Group: null);
            }

            return current.MultilingualGroups.FirstOrDefault(group => group.Primary.Name == last) is MultilingualGroup group
                ? new Resolved(TermKind.Anchor, segments, navigations, current, Target: null, group.Primary, group)
                : current.Properties.FirstOrDefault(property => property.Name == last) is PropertyMetadata property
                ? new Resolved(TermKind.Scalar, segments, navigations, current, Target: null, property, Group: null)
                : null;
        }

        /// <summary>Types an expression as one value, recording its diagnostics at its column.</summary>
        /// <param name="engine">The engine.</param>
        /// <param name="schema">The schema.</param>
        /// <param name="root">The root entity's descriptor.</param>
        /// <param name="expression">The expression.</param>
        /// <param name="index">The column's index.</param>
        /// <param name="diagnostics">Receives the diagnostics.</param>
        /// <returns>The type; null when the expression does not compile.</returns>
        /// <exception cref="BadRequestException">The expression is a list, not one value.</exception>
        private static QueryexType? Type(QueryexEngine engine, QueryexSchema schema, EntityDescriptor root, string expression, int index, List<QueryexDiagnostic> diagnostics)
        {
            string location = string.Create(CultureInfo.InvariantCulture, $"columns[{index}]");
            QueryexResult<ValidatedExpression> result = engine.Validate(expression, new ValidationOptions
            {
                Schema = schema,
                Root = root,
                Mode = QueryexMode.Value,
                QueryexVersion = QueryexLanguage.Version,
                Directions = false,
            });
            if (!result.Succeeded)
            {
                diagnostics.AddRange(result.Diagnostics.Select(diagnostic => diagnostic with { Location = location }));
                return null;
            }

            // Row grouping refuses an aggregate as a diagnostic; a list is the one shape left to refuse.
            return result.Value.Items switch
            {
                [ValidatedItem item] => item.Type,
                _ => throw new BadRequestException(
                    $"The column at {location} is not one expression.",
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = location }),
            };
        }

        /// <summary>The derived header of a path: the label of each navigation, then the last segment's, joined by <c>" / "</c>.</summary>
        /// <param name="path">The path.</param>
        /// <param name="last">The last segment's label.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The header.</returns>
        private static string Header(Resolved path, string last, ExcelContext context)
        {
            int leading = path.Kind == TermKind.Navigation ? path.Navigations.Count - 1 : path.Navigations.Count;
            IEnumerable<string> labels = path.Navigations.Take(leading).Select(step => context.Labels.NavigationLabel(step.Owner.ClrType, step.Navigation));
            return string.Join(ExportPlanner.LabelSeparator, [.. labels, last]);
        }

        /// <summary>The prefix a path's navigations put before a term of the entity they reach: <c>Customer.</c>, or empty.</summary>
        /// <param name="path">The path.</param>
        /// <returns>The prefix.</returns>
        private static string Prefix(Resolved path)
        {
            return path.Segments.Count == 1 ? string.Empty : string.Join('.', path.Segments.Take(path.Segments.Count - 1)) + ".";
        }

        /// <summary>Adds a navigation column: the target's display name, from its display-name columns.</summary>
        /// <param name="path">The resolved navigation.</param>
        /// <param name="expression">The navigation's path text.</param>
        /// <param name="header">The request's header; null to derive one.</param>
        /// <param name="slot">The content slot.</param>
        /// <param name="shape">The tenant's content-language shape.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="displayNames">The renderer.</param>
        private void AddNavigation(Resolved path, string expression, string? header, int slot, MultilingualShape shape, ExcelContext context, IDisplayNames displayNames)
        {
            EntityMetadata target = path.Target!;
            (EntityMetadata owner, string navigation) = path.Navigations[^1];
            int[] terms = [.. displayNames.Columns(target.Name, shape, expression).Select(Term)];

            ExcelColumnSpec spec = new(header ?? Header(path, context.Labels.NavigationLabel(owner.ClrType, navigation), context), QueryexType.QxString, Scale: null, path.Segments)
            {
                Role = ExcelColumnRole.Reference,
                ReferenceEntity = target.Name,
            };

            // The first term is the target's id: no id, no row, no name.
            _specs.Add(spec);
            _folds.Add(row => row[terms[0]] is null
                ? null
                : displayNames.Render(target.Name, [.. terms.Select(term => row[term])], slot, context.Culture, context.Calendar));
        }

        /// <summary>Adds a group-anchor column: the content slot's twin, else the first slot's value.</summary>
        /// <param name="path">The resolved anchor.</param>
        /// <param name="header">The request's header; null to derive one.</param>
        /// <param name="slot">The content slot.</param>
        /// <param name="context">The request's context.</param>
        private void AddAnchor(Resolved path, string? header, int slot, ExcelContext context)
        {
            MultilingualGroup group = path.Group!;
            PropertyMetadata twin = (slot switch { 2 => group.Secondary, 3 => group.Tertiary, _ => null }) ?? group.Primary;
            string prefix = Prefix(path);
            int content = Term(prefix + twin.Name);
            int first = Term(prefix + group.Primary.Name);

            ExcelColumnSpec spec = new(header ?? Header(path, AnchorLabel(path.Owner, group, context), context), QueryexType.QxString, Scale: null, path.Segments)
            {
                Property = group.Primary,
            };

            _specs.Add(spec);
            _folds.Add(row => row[content] is string value && !string.IsNullOrWhiteSpace(value) ? value : row[first]);
        }

        /// <summary>Adds a scalar or expression column, which reads and writes itself.</summary>
        /// <param name="path">The resolved scalar; null for an expression.</param>
        /// <param name="expression">The trimmed expression.</param>
        /// <param name="header">The request's header; null to derive one.</param>
        /// <param name="source">The expression as the request sent it, an expression's derived header.</param>
        /// <param name="type">The engine's type.</param>
        /// <param name="context">The request's context.</param>
        private void AddValue(Resolved? path, string expression, string? header, string source, QueryexType type, ExcelContext context)
        {
            int term = Term(expression);
            ExcelColumnSpec spec;
            if (path is { Property: PropertyMetadata property })
            {
                // A two-segment path through a navigation reads a key of the target.
                bool reference = path.Navigations.Count == 1 && path.Segments.Count == 2;
                EntityMetadata owner = path.Owner;
                spec = new ExcelColumnSpec(header ?? Header(path, context.Labels.PropertyLabel(owner.ClrType, property.Name), context), type, property.Scale, path.Segments)
                {
                    Property = property,
                    Role = reference ? ExcelColumnRole.Reference : ExcelColumnRole.Property,
                    ReferenceEntity = reference ? owner.Name : null,
                    KeyKind = reference ? (property.Name == owner.Key.Name ? ExcelKeyKind.Surrogate : ExcelKeyKind.Natural) : null,
                };
            }
            else
            {
                spec = new ExcelColumnSpec(header ?? source, type, Scale: null, Path: null);
            }

            _specs.Add(spec);
            _folds.Add(row => row[term]);
        }

        /// <summary>A group anchor's label: the primary's, without the language suffix a multilingual tenant's labels carry.</summary>
        /// <param name="owner">The entity declaring the group.</param>
        /// <param name="group">The group.</param>
        /// <param name="context">The request's context.</param>
        /// <returns>The label.</returns>
        private static string AnchorLabel(EntityMetadata owner, MultilingualGroup group, ExcelContext context)
        {
            string label = context.Labels.PropertyLabel(owner.ClrType, group.Primary.Name);
            string? symbol = context.TenantLanguages.Count > 1 ? context.Catalog.Find(context.TenantLanguages[0])?.Symbol : null;
            string suffix = $" ({symbol})";
            return symbol is not null && label.EndsWith(suffix, StringComparison.Ordinal) ? label[..^suffix.Length] : label;
        }

        /// <summary>A select term's index, adding it the first time it is used.</summary>
        /// <param name="term">The term.</param>
        /// <returns>The index.</returns>
        private int Term(string term)
        {
            if (!_indexes.TryGetValue(term, out int index))
            {
                index = _selects.Count;
                _selects.Add(term);
                _indexes.Add(term, index);
            }

            return index;
        }

        /// <summary>What a resolved path ends at.</summary>
        private enum TermKind
        {
            /// <summary>A navigation to a target entity.</summary>
            Navigation,

            /// <summary>A multilingual group's anchor, its primary's name.</summary>
            Anchor,

            /// <summary>A scalar property.</summary>
            Scalar,
        }

        /// <summary>A path the metadata resolves.</summary>
        /// <param name="Kind">What it ends at.</param>
        /// <param name="Segments">Its segments.</param>
        /// <param name="Navigations">The navigations it steps through, each with the entity that declares it; the last is the path's own for a navigation.</param>
        /// <param name="Owner">The entity declaring the last segment.</param>
        /// <param name="Target">A navigation's target; null otherwise.</param>
        /// <param name="Property">The scalar, or the anchor's primary; null for a navigation.</param>
        /// <param name="Group">An anchor's group; null otherwise.</param>
        private sealed record Resolved(
            TermKind Kind,
            IReadOnlyList<string> Segments,
            IReadOnlyList<(EntityMetadata Owner, string Navigation)> Navigations,
            EntityMetadata Owner,
            EntityMetadata? Target,
            PropertyMetadata? Property,
            MultilingualGroup? Group);
    }
}
