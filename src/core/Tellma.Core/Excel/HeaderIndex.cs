// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;

namespace Tellma.Core.Excel
{
    /// <summary>What a header names: a column path as the grammar writes it, language-relative for a multilingual column, with its language.</summary>
    /// <param name="Path">The path (<c>Name</c>, <c>Parent.Code</c>, <c>Center.Id</c>).</param>
    /// <param name="Language">The language a header's symbol names; null for a header of none.</param>
    internal readonly record struct HeaderTarget(string Path, string? Language);

    /// <summary>One header a sheet may carry, normalised, with what it names.</summary>
    /// <param name="Key">The header, normalised.</param>
    /// <param name="Target">What it names.</param>
    internal readonly record struct HeaderCandidate(string Key, HeaderTarget Target);

    // Spec 0019 §8.1 step 2: every header an export could write, in every candidate culture, normalised
    // the same way as the header it is matched with, so that a header matches exactly or not at all. A
    // key two different paths produce is no match: the header goes unmapped with both paths among its
    // suggestions, never a first-match guess.
    /// <summary>
    ///     The header index of one sheet's entity: each normalised header an export could write,
    ///     with the path it names; the keys two paths produce, kept apart; and the nearest keys to
    ///     a header that matches none.
    /// </summary>
    internal sealed class HeaderIndex
    {
        /// <summary>The longest header, in characters, whose nearest keys are searched; a longer one is cut.</summary>
        public const int MaxSuggestedHeaderLength = 64;

        /// <summary>The largest edit distance a suggestion may be at.</summary>
        public const int MaxSuggestionDistance = 4;

        /// <summary>The keys that name one path, without regard to case.</summary>
        private readonly Dictionary<string, HeaderTarget> _unique;

        /// <summary>The keys that name two paths or more, with every path, without regard to case.</summary>
        private readonly Dictionary<string, List<HeaderTarget>> _collisions;

        /// <summary>Every key, upper-cased, with what it names, for the nearest-key search.</summary>
        private readonly (string Key, IReadOnlyList<HeaderTarget> Targets)[] _search;

        /// <summary>Creates the index.</summary>
        /// <param name="unique">The keys that name one path.</param>
        /// <param name="collisions">The keys that name more.</param>
        private HeaderIndex(Dictionary<string, HeaderTarget> unique, Dictionary<string, List<HeaderTarget>> collisions)
        {
            _unique = unique;
            _collisions = collisions;
            _search =
            [
                .. unique.Select(static entry => (entry.Key.ToUpperInvariant(), (IReadOnlyList<HeaderTarget>)[entry.Value])),
                .. collisions.Select(static entry => (entry.Key.ToUpperInvariant(), (IReadOnlyList<HeaderTarget>)entry.Value)),
            ];
        }

        /// <summary>How many keys the index holds, colliding ones included.</summary>
        public int Count => _unique.Count + _collisions.Count;

        /// <summary>
        ///     Normalises a header as the index's keys are: composed to NFC, trimmed, each run of
        ///     white space one space, and no space beside <c>/</c>, <c>(</c> or <c>)</c>.
        /// </summary>
        /// <param name="header">The header.</param>
        /// <returns>The key; compared without regard to case.</returns>
        public static string Normalize(string header)
        {
            ArgumentNullException.ThrowIfNull(header);

            // A header that is not well-formed UTF-16 cannot be composed and is matched as written.
            string text;
            try
            {
                text = header.IsNormalized(NormalizationForm.FormC) ? header : header.Normalize(NormalizationForm.FormC);
            }
            catch (ArgumentException)
            {
                text = header;
            }

            // --- One pass: white space collapsed, dropped at the ends and beside the separators ---
            StringBuilder builder = new(text.Length);
            bool pending = false;
            bool afterSeparator = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    pending = builder.Length > 0;
                    continue;
                }

                bool separator = c is '/' or '(' or ')';
                if (pending && !separator && !afterSeparator)
                {
                    _ = builder.Append(' ');
                }

                _ = builder.Append(c);
                pending = false;
                afterSeparator = separator;
            }

            return builder.ToString();
        }

        /// <summary>Merges candidates into an index: a key every candidate gives one path keeps it, any other key collides.</summary>
        /// <param name="candidates">The candidates, of every culture.</param>
        /// <returns>The index.</returns>
        public static HeaderIndex Merge(IEnumerable<HeaderCandidate> candidates)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            Dictionary<string, HeaderTarget> unique = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<HeaderTarget>> collisions = new(StringComparer.OrdinalIgnoreCase);
            foreach (HeaderCandidate candidate in candidates)
            {
                if (candidate.Key.Length == 0)
                {
                    continue;
                }

                // --- A key already colliding takes one more path; a key naming another path starts colliding ---
                if (collisions.TryGetValue(candidate.Key, out List<HeaderTarget>? targets))
                {
                    if (!targets.Contains(candidate.Target))
                    {
                        targets.Add(candidate.Target);
                    }
                }
                else if (unique.TryGetValue(candidate.Key, out HeaderTarget existing))
                {
                    if (existing != candidate.Target)
                    {
                        _ = unique.Remove(candidate.Key);
                        collisions[candidate.Key] = [existing, candidate.Target];
                    }
                }
                else
                {
                    unique[candidate.Key] = candidate.Target;
                }
            }

            return new HeaderIndex(unique, collisions);
        }

        /// <summary>Matches a header exactly, after normalisation.</summary>
        /// <param name="header">The header as the sheet holds it.</param>
        /// <param name="target">What it names, when it matches.</param>
        /// <returns>True when the header names exactly one path.</returns>
        public bool TryMatch(string header, out HeaderTarget target)
        {
            ArgumentNullException.ThrowIfNull(header);
            return _unique.TryGetValue(Normalize(header), out target);
        }

        /// <summary>The paths a header names when two or more export headers normalise to it.</summary>
        /// <param name="header">The header as the sheet holds it.</param>
        /// <returns>The paths; empty when the header collides with nothing.</returns>
        public IReadOnlyList<HeaderTarget> Collisions(string header)
        {
            ArgumentNullException.ThrowIfNull(header);
            return _collisions.TryGetValue(Normalize(header), out List<HeaderTarget>? targets) ? targets : [];
        }

        /// <summary>
        ///     What the keys nearest a header name, nearest first: keys within an edit distance of a
        ///     third of the header's length (at least 1, at most <see cref="MaxSuggestionDistance" />),
        ///     by optimal string alignment without regard to case, ties in key order.
        /// </summary>
        /// <param name="header">The header as the sheet holds it.</param>
        /// <returns>What each key names, nearest key first; a colliding key's paths together.</returns>
        public IEnumerable<HeaderTarget> Nearest(string header)
        {
            ArgumentNullException.ThrowIfNull(header);
            string key = Normalize(header).ToUpperInvariant();
            if (key.Length == 0)
            {
                return [];
            }

            // --- The bound grows with the header, and a key whose length is beyond it is never measured ---
            if (key.Length > MaxSuggestedHeaderLength)
            {
                key = key[..MaxSuggestedHeaderLength];
            }

            int bound = Math.Min(MaxSuggestionDistance, Math.Max(1, key.Length / 3));
            List<(int Distance, string Key, IReadOnlyList<HeaderTarget> Targets)> near = [];
            foreach ((string candidate, IReadOnlyList<HeaderTarget> targets) in _search)
            {
                if (Math.Abs(candidate.Length - key.Length) <= bound
                    && Distance(key, candidate, bound) is int distance and > 0)
                {
                    near.Add((distance, candidate, targets));
                }
            }

            return near
                .OrderBy(static entry => entry.Distance)
                .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
                .SelectMany(static entry => entry.Targets);
        }

        /// <summary>
        ///     The optimal string alignment distance of two texts, adjacent transpositions counting
        ///     one edit, computed only within a band of the bound around the diagonal.
        /// </summary>
        /// <param name="a">The first text.</param>
        /// <param name="b">The second text.</param>
        /// <param name="bound">The largest distance of interest.</param>
        /// <returns>The distance; null when it is above the bound.</returns>
        internal static int? Distance(string a, string b, int bound)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            if (Math.Abs(a.Length - b.Length) > bound)
            {
                return null;
            }

            // --- Three rows of the matrix, cells outside the band held above the bound ---
            int over = bound + 1;
            int width = b.Length + 1;
            int[] previous2 = new int[width];
            int[] previous = new int[width];
            int[] current = new int[width];
            for (int j = 0; j < width; j++)
            {
                previous[j] = j <= bound ? j : over;
            }

            for (int i = 1; i <= a.Length; i++)
            {
                Array.Fill(current, over);
                current[0] = i <= bound ? i : over;
                int from = Math.Max(1, i - bound);
                int to = Math.Min(b.Length, i + bound);
                int least = current[0];
                for (int j = from; j <= to; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int value = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    {
                        value = Math.Min(value, previous2[j - 2] + 1);
                    }

                    current[j] = Math.Min(value, over);
                    least = Math.Min(least, current[j]);
                }

                // Every path through this row is already above the bound.
                if (least > bound)
                {
                    return null;
                }

                (previous2, previous, current) = (previous, current, previous2);
            }

            return previous[b.Length] <= bound ? previous[b.Length] : null;
        }
    }
}
