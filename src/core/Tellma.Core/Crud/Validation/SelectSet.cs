// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Crud.Validation
{
    // Spec 0015 §7.2: requests differing only in their select are merged by unioning the selects. A
    // null select reads every property, so it covers every other select.
    /// <summary>
    ///     The paths a context load selects: a set of bare paths compared ignoring case, or every
    ///     property of the entity.
    /// </summary>
    internal sealed class SelectSet
    {
        /// <summary>The paths, in ordinal order ignoring case; null for every property.</summary>
        private readonly SortedSet<string>? _paths;

        /// <summary>Creates a set.</summary>
        /// <param name="paths">The paths; null for every property.</param>
        private SelectSet(SortedSet<string>? paths)
        {
            _paths = paths;
        }

        /// <summary>Every property of the entity.</summary>
        public static SelectSet All { get; } = new(null);

        /// <summary>Whether the set reads every property.</summary>
        public bool IsAll => _paths is null;

        /// <summary>The paths, in order; empty for every property.</summary>
        public IReadOnlyCollection<string> Paths => (IReadOnlyCollection<string>?)_paths ?? [];

        /// <summary>Reads a select: comma-separated bare paths, or null for every property.</summary>
        /// <param name="select">The select.</param>
        /// <returns>The set.</returns>
        /// <exception cref="ArgumentException">The select names no path.</exception>
        public static SelectSet Parse(string? select)
        {
            if (select is null)
            {
                return All;
            }

            SortedSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            foreach (string path in select.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                _ = paths.Add(path);
            }

            return paths.Count > 0
                ? new SelectSet(paths)
                : throw new ArgumentException("A context load's select names at least one path; pass null to read every property.", nameof(select));
        }

        /// <summary>Whether this set reads everything another reads.</summary>
        /// <param name="other">The other set.</param>
        /// <returns>True when it does.</returns>
        public bool Covers(SelectSet other)
        {
            ArgumentNullException.ThrowIfNull(other);
            return _paths is null || (other._paths is not null && other._paths.IsSubsetOf(_paths));
        }

        /// <summary>The union of this set and another.</summary>
        /// <param name="other">The other set.</param>
        /// <returns>The union.</returns>
        public SelectSet Union(SelectSet other)
        {
            ArgumentNullException.ThrowIfNull(other);
            if (Covers(other))
            {
                return this;
            }

            if (other.Covers(this))
            {
                return other;
            }

            SortedSet<string> union = new(_paths!, StringComparer.OrdinalIgnoreCase);
            union.UnionWith(other._paths!);
            return new SelectSet(union);
        }

        /// <summary>This set with one more path, which a load needs to key its rows.</summary>
        /// <param name="path">The path.</param>
        /// <returns>The set.</returns>
        public SelectSet With(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            return Union(Parse(path));
        }

        /// <summary>The select text: the paths joined, or every declared property when the set reads them all.</summary>
        /// <param name="declared">The entity's declared property names, for a set that reads them all.</param>
        /// <returns>The text.</returns>
        public string Render(IEnumerable<string> declared)
        {
            ArgumentNullException.ThrowIfNull(declared);
            return string.Join(", ", (IEnumerable<string>?)_paths ?? declared);
        }
    }
}
