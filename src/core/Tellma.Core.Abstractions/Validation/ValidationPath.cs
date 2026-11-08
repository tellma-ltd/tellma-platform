// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using System.Text;

namespace Tellma.Core.Abstractions.Validation
{
    /// <summary>
    ///     Where a validation error points: a list of segments from the root of the payload down to
    ///     the value concerned. <c>[3].Lines[1].Quantity</c> is payload row 3, its child collection
    ///     <c>Lines</c>, child row 1, its <c>Quantity</c>; <c>Ids[2]</c> is a root-level name followed
    ///     by an index; <see cref="Root" /> is the whole payload.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A path is a segment list and is never parsed. The web edge renders property segments
    ///         through its JSON naming policy and a leading index under the request member that
    ///         carries the rows (<c>entities[3].lines[1].quantity</c>); the Excel codec maps segments
    ///         to cells; <see cref="ToString" /> renders the bracket form for logs and tests only.
    ///     </para>
    ///     <para>
    ///         Paths are immutable and compare by their segments, so two paths built separately to
    ///         the same value are equal and can key a dictionary.
    ///     </para>
    /// </remarks>
    /// <param name="Segments">The segments from the root; empty for the whole payload.</param>
    public sealed record ValidationPath(IReadOnlyList<PathSegment> Segments)
    {
        /// <inheritdoc cref="ValidationPath(IReadOnlyList{PathSegment})" />
        public IReadOnlyList<PathSegment> Segments { get; } = CopySegments(Segments);

        /// <summary>The path of the whole payload: no segments.</summary>
        public static ValidationPath Root { get; } = new([]);

        /// <summary>This path followed by a position in a list.</summary>
        /// <param name="index">The zero-based position; never negative.</param>
        /// <returns>The longer path.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is negative.</exception>
        public ValidationPath Index(int index)
        {
            return Append(new IndexSegment(index));
        }

        /// <summary>This path followed by a member of the value it has reached, or a key of a keyed collection.</summary>
        /// <param name="name">The CLR property name, or the key (a setting key such as <c>gl.posting.autoNumberOnPost</c>).</param>
        /// <returns>The longer path.</returns>
        /// <exception cref="ArgumentException"><paramref name="name" /> is null, empty or white space.</exception>
        public ValidationPath Property(string name)
        {
            return Append(new PropertySegment(name));
        }

        /// <summary>Whether another path has the same segments in the same order.</summary>
        /// <param name="other">The other path.</param>
        /// <returns>True when the two are equal.</returns>
        public bool Equals(ValidationPath? other)
        {
            // Compared segment by segment: the synthesised record equality would compare the two
            // lists by reference and never find two separately built paths equal.
            return other is not null && Segments.SequenceEqual(other.Segments);
        }

        /// <summary>A hash over the segments, consistent with <see cref="Equals(ValidationPath)" />.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            HashCode hash = default;
            foreach (PathSegment segment in Segments)
            {
                hash.Add(segment);
            }

            return hash.ToHashCode();
        }

        /// <summary>
        ///     The bracket form, for logs and tests only: an index as <c>[i]</c>, a property as its
        ///     name, separated from what precedes it by a dot (<c>[3].Lines[1].Quantity</c>,
        ///     <c>Ids[2]</c>). The root renders as the empty string.
        /// </summary>
        /// <returns>The bracket form.</returns>
        public override string ToString()
        {
            // Each kind appends itself: a property after another segment is joined with a dot, an
            // index never is.
            StringBuilder text = new();
            foreach (PathSegment segment in Segments)
            {
                segment.AppendTo(text);
            }

            return text.ToString();
        }

        /// <summary>Copies the segments into a list nobody else holds, refusing null entries.</summary>
        /// <param name="segments">The segments.</param>
        /// <returns>A read-only copy.</returns>
        private static ReadOnlyCollection<PathSegment> CopySegments(IReadOnlyList<PathSegment> segments)
        {
            ArgumentNullException.ThrowIfNull(segments);

            PathSegment[] copy = [.. segments];
            return Array.Exists(copy, static segment => segment is null)
                ? throw new ArgumentException("A validation path's segments must not contain null.", nameof(segments))
                : Array.AsReadOnly(copy);
        }

        /// <summary>This path with one more segment at its end.</summary>
        /// <param name="segment">The segment.</param>
        /// <returns>The longer path.</returns>
        private ValidationPath Append(PathSegment segment)
        {
            var segments = new PathSegment[Segments.Count + 1];
            for (int i = 0; i < Segments.Count; i++)
            {
                segments[i] = Segments[i];
            }

            segments[^1] = segment;
            return new ValidationPath(segments);
        }
    }
}
