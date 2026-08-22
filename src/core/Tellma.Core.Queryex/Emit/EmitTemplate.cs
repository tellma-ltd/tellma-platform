// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Globalization;

namespace Tellma.Core.Queryex.Emit
{
    /// <summary>Whether a fragment is consumed as a truth value or as a scalar.</summary>
    internal enum EmitShape
    {
        /// <summary>Consumed as a scalar.</summary>
        Value,

        /// <summary>Consumed as a truth value.</summary>
        Predicate,
    }

    /// <summary>What one piece of a template is.</summary>
    internal enum EmitSegmentKind
    {
        /// <summary>Engine-authored SQL text.</summary>
        Sql,

        /// <summary>One argument.</summary>
        Argument,

        /// <summary>A variadic tail, repeated once per matched argument.</summary>
        RepeatedArgument,
    }

    /// <summary>One piece of a template.</summary>
    /// <param name="Kind">What this piece is.</param>
    /// <param name="Text">The SQL text, or the separator between repeated arguments.</param>
    /// <param name="ArgumentIndex">Which argument, or -1 for literal SQL.</param>
    /// <param name="Shape">How the argument is consumed.</param>
    internal readonly record struct EmitSegment(
        EmitSegmentKind Kind,
        string Text,
        int ArgumentIndex,
        EmitShape Shape);

    /// <summary>
    ///     A parsed emission pattern.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The pattern is the single source of truth for two facts nothing else may guess: how
    ///         many times each argument appears in the produced SQL, and whether each is consumed as
    ///         a truth value or as a scalar. Lowering reads the first to decide which operands have
    ///         to become bindings before the emitter is allowed to repeat them, and the second is
    ///         the per-function half of position assignment. Deriving both from the pattern is what
    ///         makes it impossible for an edited template to start quietly duplicating an operand.
    ///     </para>
    ///     <para>
    ///         Pattern syntax: <c>{0}</c> is argument zero in value position, <c>{0:p}</c> the same
    ///         argument in predicate position, and <c>{2*, }</c> a variadic tail repeated once per
    ///         matched argument and joined by the text after the star. Everything else is literal,
    ///         engine-authored SQL.
    ///     </para>
    /// </remarks>
    internal sealed class EmitTemplate
    {
        /// <summary>Initializes a template from its parsed pieces.</summary>
        /// <param name="pattern">The pattern it was parsed from, for diagnostics.</param>
        /// <param name="segments">The pieces, in order.</param>
        /// <param name="useCounts">How many times each argument appears.</param>
        /// <param name="argumentShapes">How each argument is consumed.</param>
        /// <param name="result">The type the result is held in, when the pattern fixes it.</param>
        /// <param name="follows">The argument whose type the result takes, when it takes one.</param>
        private EmitTemplate(
            string pattern,
            ImmutableArray<EmitSegment> segments,
            ImmutableArray<int> useCounts,
            ImmutableArray<EmitShape> argumentShapes,
            QueryexStoreType? result,
            int? follows)
        {
            Pattern = pattern;
            Segments = segments;
            UseCounts = useCounts;
            ArgumentShapes = argumentShapes;
            Result = result;
            Follows = follows;
        }

        /// <summary>The pattern this was parsed from.</summary>
        internal string Pattern { get; }

        /// <summary>The pieces, in order.</summary>
        internal ImmutableArray<EmitSegment> Segments { get; }

        /// <summary>How many times each argument appears in the produced SQL.</summary>
        internal ImmutableArray<int> UseCounts { get; }

        /// <summary>How each argument is consumed.</summary>
        internal ImmutableArray<EmitShape> ArgumentShapes { get; }

        /// <summary>
        ///     The type the backend holds this pattern's result in, when the pattern fixes it.
        /// </summary>
        /// <remarks>
        ///     Declared here rather than worked out from the SQL, because it decides whether a total
        ///     over the result is widened before it is taken and whether a division of it keeps its
        ///     remainder — and both go wrong silently. A pattern that leaves it unstated is read as
        ///     following its operands, which is the answer that widens nothing.
        /// </remarks>
        internal QueryexStoreType? Result { get; }

        /// <summary>The argument whose type the result takes, when it takes one.</summary>
        internal int? Follows { get; }

        /// <summary>Parses a pattern.</summary>
        /// <param name="pattern">The pattern text.</param>
        /// <param name="result">The type the result is held in, when the pattern fixes it.</param>
        /// <param name="follows">The argument whose type the result takes, when it takes one.</param>
        /// <returns>The template.</returns>
        /// <exception cref="ArgumentException">The pattern is malformed, which is a defect in the
        ///     registry rather than anything a user could cause.</exception>
        internal static EmitTemplate Parse(
            string pattern,
            QueryexStoreType? result = null,
            int? follows = null)
        {
            ArgumentNullException.ThrowIfNull(pattern);

            List<EmitSegment> segments = [];
            Dictionary<int, int> counts = [];
            Dictionary<int, EmitShape> shapes = [];
            int index = 0;
            int literalStart = 0;

            while (index < pattern.Length)
            {
                if (pattern[index] != '{')
                {
                    index++;
                    continue;
                }

                if (index > literalStart)
                {
                    segments.Add(new EmitSegment(
                        EmitSegmentKind.Sql,
                        pattern[literalStart..index],
                        -1,
                        EmitShape.Value));
                }

                int close = pattern.IndexOf('}', index + 1);
                if (close < 0)
                {
                    throw Malformed(pattern, "a placeholder is not closed");
                }

                segments.Add(ParsePlaceholder(pattern, pattern[(index + 1)..close], counts, shapes));
                index = close + 1;
                literalStart = index;
            }

            if (literalStart < pattern.Length)
            {
                segments.Add(new EmitSegment(
                    EmitSegmentKind.Sql,
                    pattern[literalStart..],
                    -1,
                    EmitShape.Value));
            }

            if (counts.Count == 0)
            {
                return new EmitTemplate(pattern, [.. segments], [], [], result, follows);
            }

            int arity = counts.Keys.Max() + 1;
            int[] useCounts = new int[arity];
            var argumentShapes = new EmitShape[arity];
            foreach ((int argument, int count) in counts)
            {
                useCounts[argument] = count;
                argumentShapes[argument] = shapes[argument];
            }

            return new EmitTemplate(
                pattern,
                [.. segments],
                [.. useCounts],
                [.. argumentShapes],
                result,
                follows);
        }

        /// <summary>Parses one placeholder.</summary>
        /// <param name="pattern">The whole pattern, for diagnostics.</param>
        /// <param name="body">The placeholder's contents, without its braces.</param>
        /// <param name="counts">The running use counts.</param>
        /// <param name="shapes">The running argument shapes.</param>
        /// <returns>The segment.</returns>
        private static EmitSegment ParsePlaceholder(
            string pattern,
            string body,
            Dictionary<int, int> counts,
            Dictionary<int, EmitShape> shapes)
        {
            EmitShape shape = EmitShape.Value;
            EmitSegmentKind kind = EmitSegmentKind.Argument;
            string separator = string.Empty;
            string digits = body;

            int star = body.IndexOf('*', StringComparison.Ordinal);
            if (star >= 0)
            {
                kind = EmitSegmentKind.RepeatedArgument;
                digits = body[..star];
                separator = body[(star + 1)..];
            }
            else
            {
                int colon = body.IndexOf(':', StringComparison.Ordinal);
                if (colon >= 0)
                {
                    digits = body[..colon];
                    string modifier = body[(colon + 1)..];
                    shape = modifier switch
                    {
                        "p" => EmitShape.Predicate,
                        "v" => EmitShape.Value,
                        _ => throw Malformed(pattern, "an unknown placeholder modifier"),
                    };
                }
            }

            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int argument))
            {
                throw Malformed(pattern, "a placeholder without an argument number");
            }

            counts[argument] = counts.GetValueOrDefault(argument) + 1;
            if (shapes.TryGetValue(argument, out EmitShape existing) && existing != shape)
            {
                throw Malformed(pattern, "an argument consumed in two different positions");
            }

            shapes[argument] = shape;
            return new EmitSegment(kind, separator, argument, shape);
        }

        /// <summary>Builds the exception for a malformed pattern.</summary>
        /// <param name="pattern">The pattern.</param>
        /// <param name="problem">What is wrong with it.</param>
        /// <returns>The exception to throw.</returns>
        private static ArgumentException Malformed(string pattern, string problem)
        {
            return new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"The emit pattern '{pattern}' has {problem}."),
                nameof(pattern));
        }
    }
}
