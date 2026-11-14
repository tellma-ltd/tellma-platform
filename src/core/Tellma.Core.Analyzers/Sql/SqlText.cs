// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Tellma.Core.Analyzers.Sql
{
    /// <summary>
    ///     The text of an <c>IDataBatch.Sql</c> statement as the parser reads it: the interpolated
    ///     string's literal parts, with a stand-in for each hole, and the way back from an offset in it
    ///     to the source.
    /// </summary>
    internal sealed class SqlText
    {
        /// <summary>The shortest piece of text searched for in a literal part whose source differs from its value.</summary>
        private const int MinimumSnippet = 3;

        /// <summary>The longest piece of text searched for.</summary>
        private const int MaximumSnippet = 40;

        /// <summary>The literal parts and the holes in text order, each with where it starts in <see cref="Text" />.</summary>
        private readonly List<Segment> _segments;

        /// <summary>Creates the text.</summary>
        /// <param name="text">The text with the stand-ins.</param>
        /// <param name="holes">The holes, by index.</param>
        /// <param name="segments">The segments, in text order.</param>
        /// <param name="location">The whole interpolated string.</param>
        private SqlText(string text, IReadOnlyList<SqlHole> holes, List<Segment> segments, Location location)
        {
            Text = text;
            Holes = holes;
            _segments = segments;
            Location = location;
        }

        /// <summary>The text with the stand-ins.</summary>
        public string Text { get; }

        /// <summary>The holes, by index.</summary>
        public IReadOnlyList<SqlHole> Holes { get; }

        /// <summary>The whole interpolated string.</summary>
        public Location Location { get; }

        /// <summary>
        ///     Reads an argument as statement text, or null when it is not an interpolated string
        ///     written at the call: text built by concatenation or held in a variable cannot be parsed.
        /// </summary>
        /// <param name="argument">The <c>sql</c> argument.</param>
        /// <returns>The text, or null.</returns>
        public static SqlText? From(IOperation argument)
        {
            if (argument.WithoutConversions() is not IInterpolatedStringOperation interpolated)
            {
                return null;
            }

            StringBuilder text = new();
            List<SqlHole> holes = [];
            List<Segment> segments = [];
            foreach (IInterpolatedStringContentOperation part in interpolated.Parts)
            {
                switch (part)
                {
                    // --- Literal text, with its escapes already resolved ({{ is one brace) ---
                    case IInterpolatedStringTextOperation { Text.ConstantValue: { HasValue: true, Value: string literal } }:
                        segments.Add(new Segment(text.Length, literal.Length, part.Syntax, literal, hole: null));
                        _ = text.Append(literal);
                        break;

                    // --- A hole, read for what it binds as and replaced by its stand-in ---
                    case IInterpolationOperation interpolation:
                        SqlHole hole = new(holes.Count, interpolation);
                        holes.Add(hole);
                        segments.Add(new Segment(text.Length, hole.Placeholder.Length, part.Syntax, literal: null, hole));
                        _ = text.Append(hole.Placeholder);
                        break;

                    default:
                        return null;
                }
            }

            return new SqlText(text.ToString(), holes, segments, interpolated.Syntax.GetLocation());
        }

        /// <summary>The hole a stand-in names, or null when the name is no stand-in.</summary>
        /// <param name="name">The name as the parser read it.</param>
        /// <returns>The hole.</returns>
        public SqlHole? HoleNamed(string name)
        {
            int index = SqlHole.IndexOf(name);
            return index >= 0 && index < Holes.Count ? Holes[index] : null;
        }

        /// <summary>Where a hole's stand-in starts and ends in <see cref="Text" />.</summary>
        /// <param name="hole">The hole.</param>
        /// <returns>The start and the end, exclusive.</returns>
        public (int Start, int End) SpanOf(SqlHole hole)
        {
            Segment segment = _segments.First(candidate => ReferenceEquals(candidate.Hole, hole));
            return (segment.Start, segment.Start + segment.Length);
        }

        /// <summary>
        ///     The source location of a piece of the text: the hole for a stand-in, the exact characters
        ///     when the literal part reads as written, else the literal part, else the whole string.
        /// </summary>
        /// <param name="offset">Where the piece starts in <see cref="Text" />.</param>
        /// <param name="length">Its length.</param>
        /// <returns>The location.</returns>
        public Location LocationOf(int offset, int length)
        {
            // --- The exact characters, when both ends map to the source ---
            int last = offset + Math.Max(length, 1) - 1;
            if (SourcePosition(offset, end: false) is int first && SourcePosition(last, end: true) is int final && final >= first)
            {
                return Location.Create(Location.SourceTree!, TextSpan.FromBounds(first, final + 1));
            }

            // --- Otherwise the segment the piece starts in ---
            if (SegmentAt(offset) is not Segment segment)
            {
                return Location;
            }

            if (segment.Literal is null)
            {
                return segment.Syntax.GetLocation();
            }

            int within = offset - segment.Start;
            int available = Math.Max(1, Math.Min(length, segment.Literal.Length - within));
            SyntaxNode syntax = segment.Syntax;
            string source = syntax.ToString();

            // --- The piece's first line, when it occurs exactly once in the literal's source ---
            string snippet = segment.Literal.Substring(within, Math.Min(available, MaximumSnippet));
            int newline = snippet.IndexOfAny(['\r', '\n']);
            snippet = newline >= 0 ? snippet.Substring(0, newline) : snippet;
            int found = snippet.Length >= MinimumSnippet ? source.IndexOf(snippet, StringComparison.Ordinal) : -1;
            return found >= 0 && source.IndexOf(snippet, found + 1, StringComparison.Ordinal) < 0
                ? Location.Create(syntax.SyntaxTree, new TextSpan(syntax.SpanStart + found, snippet.Length))
                : syntax.GetLocation();
        }

        /// <summary>The segment a text offset falls in, or null past the end.</summary>
        /// <param name="offset">The offset in <see cref="Text" />.</param>
        /// <returns>The segment.</returns>
        private Segment? SegmentAt(int offset)
        {
            foreach (Segment segment in _segments)
            {
                if (offset >= segment.Start && offset < segment.Start + segment.Length)
                {
                    return segment;
                }
            }

            return null;
        }

        /// <summary>
        ///     The source position of one character of the text: within a literal part whose source
        ///     spells its value as is, or the first or last character of a hole.
        /// </summary>
        /// <param name="offset">The character's offset in <see cref="Text" />.</param>
        /// <param name="end">Whether the character ends a piece, which maps a hole to its last character.</param>
        /// <returns>The position, or null when the source does not spell the character as is.</returns>
        private int? SourcePosition(int offset, bool end)
        {
            return SegmentAt(offset) switch
            {
                null => null,
                { Literal: null } hole => end ? hole.Syntax.Span.End - 1 : hole.Syntax.SpanStart,
                Segment literal when string.Equals(literal.Syntax.ToString(), literal.Literal, StringComparison.Ordinal) =>
                    literal.Syntax.SpanStart + (offset - literal.Start),
                _ => null,
            };
        }

        /// <summary>One literal part or one hole of the text.</summary>
        /// <param name="start">Where it starts in the text.</param>
        /// <param name="length">Its length in the text.</param>
        /// <param name="syntax">Its source.</param>
        /// <param name="literal">The literal's value, or null for a hole.</param>
        /// <param name="hole">The hole, or null for a literal.</param>
        private sealed class Segment(int start, int length, SyntaxNode syntax, string? literal, SqlHole? hole)
        {
            /// <summary>Where it starts in the text.</summary>
            public int Start { get; } = start;

            /// <summary>Its length in the text.</summary>
            public int Length { get; } = length;

            /// <summary>Its source.</summary>
            public SyntaxNode Syntax { get; } = syntax;

            /// <summary>The literal's value, or null for a hole.</summary>
            public string? Literal { get; } = literal;

            /// <summary>The hole, or null for a literal.</summary>
            public SqlHole? Hole { get; } = hole;
        }
    }
}
