// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Query
{
    /// <summary>
    ///     What a query reads from the root it runs on, a stack or a child query surface: its
    ///     defaults, its search columns, its display name, its capabilities and its ceilings.
    /// </summary>
    internal sealed class QuerySurface
    {
        /// <summary>The ordering of a root that declares none, so every page has a stable order.</summary>
        public const string FallbackOrderBy = "Id desc";

        /// <summary>The default select each tenant schema admits, by schema instance.</summary>
        private readonly ConcurrentDictionary<QueryexSchema, string> _defaultSelects = new(ReferenceEqualityComparer.Instance);

        /// <summary>Creates a surface.</summary>
        /// <param name="resource">The resource, which is also the root's entity name.</param>
        /// <param name="isChild">Whether the root is a child query surface.</param>
        /// <param name="defaultSelect">The select of a request that sends none.</param>
        /// <param name="defaultOrderBy">The ordering of a request that sends none; null for <see cref="FallbackOrderBy" />.</param>
        /// <param name="searchable">The searchable columns, the convention already applied.</param>
        /// <param name="displayName">The root's display-name rule.</param>
        /// <param name="keyType">The root's key type, <see cref="int" /> or <see cref="long" />.</param>
        /// <param name="isActivatable">Whether the root has the active flag.</param>
        /// <param name="isTree">Whether a page may include its matches' ancestors.</param>
        /// <param name="namedFilters">The named filters, by name; empty for a child query surface.</param>
        /// <param name="limits">The ceilings.</param>
        public QuerySurface(
            string resource,
            bool isChild,
            string defaultSelect,
            string? defaultOrderBy,
            IReadOnlyList<(string Property, SearchKind Kind)> searchable,
            DisplayNameMetadata displayName,
            Type keyType,
            bool isActivatable,
            bool isTree,
            IReadOnlyDictionary<string, FilterTree> namedFilters,
            StackLimits limits)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(resource);
            ArgumentException.ThrowIfNullOrWhiteSpace(defaultSelect);
            Resource = resource;
            IsChild = isChild;
            DefaultSelect = defaultSelect;
            DeclaredOrderBy = string.IsNullOrWhiteSpace(defaultOrderBy) ? null : defaultOrderBy;
            DefaultOrderBy = DeclaredOrderBy ?? FallbackOrderBy;
            Searchable = searchable ?? throw new ArgumentNullException(nameof(searchable));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            KeyType = keyType ?? throw new ArgumentNullException(nameof(keyType));
            IsActivatable = isActivatable;
            IsTree = isTree;
            NamedFilters = namedFilters ?? throw new ArgumentNullException(nameof(namedFilters));
            Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        }

        /// <summary>The resource, which is also the root's entity name.</summary>
        public string Resource { get; }

        /// <summary>Whether the root is a child query surface.</summary>
        public bool IsChild { get; }

        /// <summary>The select of a request that sends none, as declared.</summary>
        public string DefaultSelect { get; }

        /// <summary>The ordering of a request that sends none.</summary>
        public string DefaultOrderBy { get; }

        /// <summary>The ordering the root declares; null when it declares none.</summary>
        public string? DeclaredOrderBy { get; }

        /// <summary>The searchable columns, the convention already applied; empty when the root has none.</summary>
        public IReadOnlyList<(string Property, SearchKind Kind)> Searchable { get; }

        /// <summary>The root's display-name rule.</summary>
        public DisplayNameMetadata DisplayName { get; }

        /// <summary>The root's key type, <see cref="int" /> or <see cref="long" />.</summary>
        public Type KeyType { get; }

        /// <summary>Whether the root has the active flag, so a request sees active rows only unless it asks for every row.</summary>
        public bool IsActivatable { get; }

        /// <summary>Whether a page may include its matches' ancestors: a stack with the tree capability.</summary>
        public bool IsTree { get; }

        /// <summary>The named filters, by name; empty for a child query surface.</summary>
        public IReadOnlyDictionary<string, FilterTree> NamedFilters { get; }

        /// <summary>The ceilings.</summary>
        public StackLimits Limits { get; }

        /// <summary>
        ///     The default select a tenant's schema admits. A default that is a plain list of property
        ///     paths drops the paths the schema does not declare, such as the multilingual twins the
        ///     tenant's languages leave out; any other default stands as declared.
        /// </summary>
        /// <param name="schema">The tenant's schema.</param>
        /// <param name="root">The root, of that schema.</param>
        /// <returns>The select text.</returns>
        public string DefaultSelectFor(QueryexSchema schema, EntityDescriptor root)
        {
            ArgumentNullException.ThrowIfNull(schema);
            ArgumentNullException.ThrowIfNull(root);
            return _defaultSelects.GetOrAdd(schema, static (_, state) => Admitted(state.Text, state.Root), (Text: DefaultSelect, Root: root));
        }

        /// <summary>The items of a plain list of paths that resolve against a root; the text itself when it is not such a list.</summary>
        /// <param name="text">The select text.</param>
        /// <param name="root">The root.</param>
        /// <returns>The admitted text.</returns>
        private static string Admitted(string text, EntityDescriptor root)
        {
            // --- Only a list of bare paths can be judged item by item without the engine ---
            string[] items = text.Split(',', StringSplitOptions.TrimEntries);
            if (!items.All(IsPath))
            {
                return text;
            }

            // --- Each path walks its navigations and ends at a property ---
            List<string> kept = [.. items.Where(item => SchemaLookup.Resolves(root, item.Split('.')))];
            return kept.Count == 0 || kept.Count == items.Length ? text : string.Join(", ", kept);
        }

        /// <summary>Whether a select item is a bare path: identifiers joined by dots.</summary>
        /// <param name="item">The trimmed item.</param>
        /// <returns>True for a bare path.</returns>
        private static bool IsPath(string item)
        {
            if (item.Length == 0)
            {
                return false;
            }

            foreach (string segment in item.Split('.'))
            {
                if (segment.Length == 0 || !(char.IsAsciiLetter(segment[0]) || segment[0] == '_') || !segment.All(static c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
