// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Measures
{
    /// <summary>
    ///     Orders hierarchy positions by their paths, as the backend orders them: level by level,
    ///     each level as a number, with an ancestor before every one of its descendants.
    /// </summary>
    /// <remarks>
    ///     A path is written <c>/1/2/</c>; the root is <c>/</c>. A level may be dotted, <c>/1.5/</c>,
    ///     for a position inserted between two others, and its parts compare one after another as
    ///     numbers, a shorter run of parts first. Levels may be negative.
    /// </remarks>
    internal sealed class HierarchyPathOrder : IComparer<string?>
    {
        /// <summary>The one instance.</summary>
        internal static readonly HierarchyPathOrder Instance = new();

        /// <summary>Initializes the order.</summary>
        private HierarchyPathOrder()
        {
        }

        /// <summary>Compares two paths.</summary>
        /// <param name="x">One path.</param>
        /// <param name="y">The other.</param>
        /// <returns>Negative when the first comes first, positive when the second does, zero when they are one.</returns>
        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            int i = 0;
            int j = 0;
            while (true)
            {
                // Past the separator before the next level, on both sides.
                i = SkipSeparators(x, i);
                j = SkipSeparators(y, j);
                bool xEnded = i >= x.Length;
                bool yEnded = j >= y.Length;
                if (xEnded || yEnded)
                {
                    // An ancestor is a prefix of each of its descendants, and comes first.
                    return xEnded == yEnded ? 0 : xEnded ? -1 : 1;
                }

                // One level, part by part: the parts of a dotted level are compared in turn.
                while (true)
                {
                    long left = ReadNumber(x, ref i);
                    long right = ReadNumber(y, ref j);
                    if (left != right)
                    {
                        return left < right ? -1 : 1;
                    }

                    bool xMore = i < x.Length && x[i] == '.';
                    bool yMore = j < y.Length && y[j] == '.';
                    if (xMore != yMore)
                    {
                        // The level with fewer parts sorts before the one that refines it.
                        return xMore ? 1 : -1;
                    }

                    if (!xMore)
                    {
                        break;
                    }

                    i++;
                    j++;
                }
            }
        }

        /// <summary>Skips the slashes before a level.</summary>
        /// <param name="path">The path.</param>
        /// <param name="index">Where to start.</param>
        /// <returns>The position of the next level's first character, or the end.</returns>
        private static int SkipSeparators(string path, int index)
        {
            while (index < path.Length && path[index] == '/')
            {
                index++;
            }

            return index;
        }

        /// <summary>Reads one part of a level as a number.</summary>
        /// <param name="path">The path.</param>
        /// <param name="index">Where the part starts; left just past it.</param>
        /// <returns>The number.</returns>
        private static long ReadNumber(string path, ref int index)
        {
            bool negative = index < path.Length && path[index] == '-';
            if (negative)
            {
                index++;
            }

            long value = 0;
            while (index < path.Length && path[index] is >= '0' and <= '9')
            {
                value = unchecked((value * 10) + (path[index] - '0'));
                index++;
            }

            return negative ? -value : value;
        }
    }

    /// <summary>
    ///     Orders text ignoring case, as the platform collation does on ordinary business text.
    /// </summary>
    internal static class TextOrder
    {
        /// <summary>The comparison that decides which text is least or greatest.</summary>
        internal static readonly StringComparer Primary = StringComparer.InvariantCultureIgnoreCase;

        /// <summary>
        ///     The comparison that settles a tie: of two texts the primary comparison calls equal,
        ///     the ordinally smaller is kept, whichever extreme is being taken, so the result never
        ///     depends on the order rows arrive in.
        /// </summary>
        internal static readonly StringComparer Tie = StringComparer.Ordinal;
    }

    /// <summary>Orders instants by the moment they denote and then by their offset.</summary>
    internal sealed class InstantOrder : IComparer<DateTimeOffset>
    {
        /// <summary>The one instance.</summary>
        internal static readonly InstantOrder Instance = new();

        /// <summary>Initializes the order.</summary>
        private InstantOrder()
        {
        }

        /// <summary>Compares two instants.</summary>
        /// <param name="x">One instant.</param>
        /// <param name="y">The other.</param>
        /// <returns>Negative, zero or positive.</returns>
        public int Compare(DateTimeOffset x, DateTimeOffset y)
        {
            int moment = x.UtcTicks.CompareTo(y.UtcTicks);
            return moment != 0 ? moment : x.Offset.CompareTo(y.Offset);
        }
    }
}
