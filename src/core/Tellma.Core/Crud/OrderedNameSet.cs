// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;

namespace Tellma.Core.Crud
{
    /// <summary>
    ///     A read-only set of names that enumerates in the order the names were first added, so a
    ///     descriptor's projection lists its columns as declared and renders the same on every host.
    ///     Names compare ordinally.
    /// </summary>
    internal sealed class OrderedNameSet : IReadOnlySet<string>
    {
        private readonly List<string> _order = [];
        private readonly HashSet<string> _members = new(StringComparer.Ordinal);

        /// <summary>Creates a set of names, in first-occurrence order, dropping repeats.</summary>
        /// <param name="names">The names.</param>
        public OrderedNameSet(IEnumerable<string> names)
        {
            ArgumentNullException.ThrowIfNull(names);
            foreach (string name in names)
            {
                if (_members.Add(name))
                {
                    _order.Add(name);
                }
            }
        }

        /// <inheritdoc />
        public int Count => _order.Count;

        /// <inheritdoc />
        public bool Contains(string item)
        {
            return _members.Contains(item);
        }

        /// <inheritdoc />
        public IEnumerator<string> GetEnumerator()
        {
            return _order.GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <inheritdoc />
        public bool IsProperSubsetOf(IEnumerable<string> other)
        {
            return _members.IsProperSubsetOf(other);
        }

        /// <inheritdoc />
        public bool IsProperSupersetOf(IEnumerable<string> other)
        {
            return _members.IsProperSupersetOf(other);
        }

        /// <inheritdoc />
        public bool IsSubsetOf(IEnumerable<string> other)
        {
            return _members.IsSubsetOf(other);
        }

        /// <inheritdoc />
        public bool IsSupersetOf(IEnumerable<string> other)
        {
            return _members.IsSupersetOf(other);
        }

        /// <inheritdoc />
        public bool Overlaps(IEnumerable<string> other)
        {
            return _members.Overlaps(other);
        }

        /// <inheritdoc />
        public bool SetEquals(IEnumerable<string> other)
        {
            return _members.SetEquals(other);
        }
    }
}
