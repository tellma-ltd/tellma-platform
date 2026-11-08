// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Core.Abstractions.Validation
{
    /// <summary>
    ///     One step of a <see cref="ValidationPath" />: an <see cref="IndexSegment" /> into a list or a
    ///     <see cref="PropertySegment" /> naming a member. These two are the only kinds.
    /// </summary>
    /// <remarks>
    ///     The set is closed by construction: a record's copy constructor is always reachable by a
    ///     derived type, but <see cref="AppendTo" /> is an internal abstract member, which a type
    ///     outside this assembly cannot implement. Every renderer can therefore switch on the two
    ///     kinds.
    /// </remarks>
    public abstract record PathSegment
    {
        /// <summary>
        ///     Appends the segment's bracket form to the text rendered so far: an index as <c>[i]</c>,
        ///     a property as its name, after a dot when something precedes it.
        /// </summary>
        /// <param name="text">The text rendered so far.</param>
        internal abstract void AppendTo(StringBuilder text);
    }

    /// <summary>
    ///     A position in a list: a payload row (<c>[3]</c>) or a child row (<c>Lines[1]</c>).
    /// </summary>
    /// <remarks>
    ///     <see cref="Index" /> is get-only and validated, so a <c>with</c> expression cannot produce a
    ///     negative index.
    /// </remarks>
    /// <param name="Index">The zero-based position; never negative.</param>
    public sealed record IndexSegment(int Index) : PathSegment
    {
        /// <inheritdoc cref="IndexSegment(int)" />
        public int Index { get; } = Index >= 0
            ? Index
            : throw new ArgumentOutOfRangeException(nameof(Index), Index, "A path index is zero or positive.");

        /// <summary>The segment in bracket form, <c>[3]</c>.</summary>
        /// <returns>The bracket form.</returns>
        public override string ToString()
        {
            return string.Create(CultureInfo.InvariantCulture, $"[{Index}]");
        }

        /// <inheritdoc />
        internal override void AppendTo(StringBuilder text)
        {
            // An index follows what precedes it directly: Lines[1].
            text.Append('[').Append(Index.ToString(CultureInfo.InvariantCulture)).Append(']');
        }
    }

    /// <summary>
    ///     A member of the value the path has reached: a CLR property name (<c>Lines</c>,
    ///     <c>Quantity</c>, <c>Arguments</c>), never a JSON name, since each renderer applies its own
    ///     naming; or a key of a keyed collection, such as a setting key under <c>Entries</c>
    ///     (<c>gl.posting.autoNumberOnPost</c>).
    /// </summary>
    /// <remarks>
    ///     <see cref="Name" /> is get-only and validated, so a <c>with</c> expression cannot produce an
    ///     empty name. Names compare ordinally, as CLR member names do. A name may hold any
    ///     character: a path is a segment list that no renderer parses, so a dotted key stays one
    ///     segment even though the bracket form of <see cref="ValidationPath.ToString" /> shows it
    ///     with its dots.
    /// </remarks>
    /// <param name="Name">The property name or key; not null, empty or white space.</param>
    public sealed record PropertySegment(string Name) : PathSegment
    {
        /// <inheritdoc cref="PropertySegment(string)" />
        public string Name { get; } = ValidateName(Name);

        /// <summary>The segment's name.</summary>
        /// <returns>The name.</returns>
        public override string ToString()
        {
            return Name;
        }

        /// <inheritdoc />
        internal override void AppendTo(StringBuilder text)
        {
            // A property is joined to what precedes it with a dot: [3].Lines.
            if (text.Length > 0)
            {
                text.Append('.');
            }

            text.Append(Name);
        }

        /// <summary>Refuses a null, empty or white-space name.</summary>
        /// <param name="name">The candidate name.</param>
        /// <returns>The name.</returns>
        private static string ValidateName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            return name;
        }
    }
}
