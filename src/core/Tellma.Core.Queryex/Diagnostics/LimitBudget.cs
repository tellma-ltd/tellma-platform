// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Queryex.Diagnostics
{
    /// <summary>
    ///     The ceilings that apply while one expression text is lexed and parsed.
    /// </summary>
    /// <remarks>
    ///     Scoped to one text rather than to a whole query, because a parse is cached against its
    ///     text and its ceilings alone. Anything wider in scope would make a cached parse depend on
    ///     clauses it has never seen.
    /// </remarks>
    /// <param name="limits">The ceilings for this call site.</param>
    internal sealed class ParseBudget(QueryexLimits limits)
    {
        /// <summary>Tokens produced so far.</summary>
        private int _tokens;

        /// <summary>Current nesting depth.</summary>
        private int _depth;

        /// <summary>Whether some ceiling has been reported, which stops further reporting.</summary>
        internal bool Exceeded { get; private set; }

        /// <summary>Checks the input length before anything is allocated for it.</summary>
        /// <param name="text">The expression text.</param>
        /// <param name="scope">Where to report.</param>
        /// <returns>True when the text is within the ceiling.</returns>
        internal bool TryAcceptInput(string text, in DiagnosticScope scope)
        {
            return text.Length <= limits.MaxInputLength
                || Fail(
                    DiagnosticCodes.MaxInputLength,
                    new QueryexSpan(0, text.Length),
                    limits.MaxInputLength,
                    scope);
        }

        /// <summary>Counts one token.</summary>
        /// <param name="span">The token's range, for the diagnostic.</param>
        /// <param name="scope">Where to report.</param>
        /// <returns>True when the token is within the ceiling.</returns>
        internal bool TryConsumeToken(QueryexSpan span, in DiagnosticScope scope)
        {
            _tokens++;
            return _tokens <= limits.MaxTokens
                || Fail(DiagnosticCodes.MaxTokens, span, limits.MaxTokens, scope);
        }

        /// <summary>Checks how long one list has grown.</summary>
        /// <param name="count">The items in that one list so far, the new one included.</param>
        /// <param name="span">The newest item's range, for the diagnostic.</param>
        /// <param name="scope">Where to report.</param>
        /// <returns>True when the list is within the ceiling.</returns>
        /// <remarks>
        ///     Each list is measured on its own, which is what the ceiling says it means. The total
        ///     across an input needs no separate ceiling: every item costs at least one token, so the
        ///     token count already bounds it.
        /// </remarks>
        internal bool TryAcceptListLength(int count, QueryexSpan span, in DiagnosticScope scope)
        {
            return count <= limits.MaxListItems
                || Fail(DiagnosticCodes.MaxListItems, span, limits.MaxListItems, scope);
        }

        /// <summary>Enters one level of nesting.</summary>
        /// <param name="span">The range at that depth, for the diagnostic.</param>
        /// <param name="scope">Where to report.</param>
        /// <returns>True when the level is within the ceiling.</returns>
        /// <remarks>
        ///     Checked before descending rather than after, because this ceiling is also what keeps
        ///     a recursive-descent parser off the end of its stack.
        /// </remarks>
        internal bool TryEnterDepth(QueryexSpan span, in DiagnosticScope scope)
        {
            _depth++;
            return _depth <= limits.MaxSyntaxDepth
                || Fail(DiagnosticCodes.MaxSyntaxDepth, span, limits.MaxSyntaxDepth, scope);
        }

        /// <summary>Leaves one level of nesting.</summary>
        internal void ExitDepth()
        {
            _depth--;
        }

        /// <summary>Charges a finished subtree's own height against the ceiling.</summary>
        /// <param name="height">The subtree's own height.</param>
        /// <param name="span">The subtree's range in the input, for the diagnostic.</param>
        /// <param name="scope">Where to report.</param>
        /// <returns>True when the tree is within the ceiling.</returns>
        /// <remarks>
        ///     Entering and leaving levels measures how deep the parser went, which is not how deep
        ///     the tree is. A run of one operator at one precedence is consumed by a loop that
        ///     re-wraps what it has so far, so a chain of a thousand terms is one level of descent
        ///     and a thousand levels of tree. Every later pass — binding, nullity, lowering, the
        ///     structural comparison, and the emitter — walks that tree by recursion, so the tree's
        ///     own height is what the ceiling has to bound. Added to the levels above it, because a
        ///     chain nested inside groups is as deep as both together.
        /// </remarks>
        internal bool TryAcceptDepth(int height, QueryexSpan span, in DiagnosticScope scope)
        {
            return _depth - 1 + height <= limits.MaxSyntaxDepth
                || Fail(DiagnosticCodes.MaxSyntaxDepth, span, limits.MaxSyntaxDepth, scope);
        }

        /// <summary>Reports one ceiling, once.</summary>
        /// <param name="code">The diagnostic code.</param>
        /// <param name="span">The offending range.</param>
        /// <param name="limit">The ceiling that was exceeded.</param>
        /// <param name="scope">Where to report.</param>
        /// <returns>Always false, so callers can return it directly.</returns>
        private bool Fail(string code, QueryexSpan span, int limit, in DiagnosticScope scope)
        {
            if (!Exceeded)
            {
                Exceeded = true;
                scope.Report(
                    code,
                    span,
                    DiagnosticArgumentNames.Limit,
                    limit.ToString(CultureInfo.InvariantCulture));
            }

            return false;
        }
    }

    /// <summary>
    ///     The ceilings that apply across a whole compilation, spanning every clause.
    /// </summary>
    /// <param name="limits">The ceilings for this call site.</param>
    internal sealed class CompilationBudget(QueryexLimits limits)
    {
        /// <summary>Bound nodes counted so far, across every clause.</summary>
        private int _typedNodes;

        /// <summary>Joins counted so far, after pruning.</summary>
        private int _joins;

        /// <summary>Parameter slots counted so far, after deduplication.</summary>
        private int _parameters;

        /// <summary>Whether some ceiling has been reported, which stops further reporting.</summary>
        internal bool Exceeded { get; private set; }

        /// <summary>Adds a clause's bound-node count to the running total.</summary>
        /// <param name="count">The nodes that clause bound.</param>
        /// <param name="span">The clause's range, for the diagnostic.</param>
        /// <param name="scope">Where to report.</param>
        /// <returns>True when the total is within the ceiling.</returns>
        /// <remarks>
        ///     Added per clause rather than per node because a cached clause is not re-bound, so its
        ///     nodes cannot be counted again by walking it.
        /// </remarks>
        internal bool TryConsumeTypedNodes(int count, QueryexSpan span, in DiagnosticScope scope)
        {
            _typedNodes += count;
            return _typedNodes <= limits.MaxTypedNodes
                || Fail(DiagnosticCodes.MaxTypedNodes, span, limits.MaxTypedNodes, scope);
        }

        /// <summary>Counts one join.</summary>
        /// <param name="span">The range of the path that needed it.</param>
        /// <param name="scope">Where to report.</param>
        /// <returns>True when the join is within the ceiling.</returns>
        internal bool TryConsumeJoin(QueryexSpan span, in DiagnosticScope scope)
        {
            _joins++;
            return _joins <= limits.MaxJoins
                || Fail(DiagnosticCodes.MaxJoins, span, limits.MaxJoins, scope);
        }

        /// <summary>Counts one parameter slot.</summary>
        /// <param name="span">The range of the node that needed it.</param>
        /// <param name="scope">Where to report.</param>
        /// <returns>True when the slot is within the ceiling.</returns>
        internal bool TryConsumeParameter(QueryexSpan span, in DiagnosticScope scope)
        {
            _parameters++;
            return _parameters <= limits.MaxParameters
                || Fail(DiagnosticCodes.MaxParameters, span, limits.MaxParameters, scope);
        }

        /// <summary>Reports one ceiling, once.</summary>
        /// <param name="code">The diagnostic code.</param>
        /// <param name="span">The offending range.</param>
        /// <param name="limit">The ceiling that was exceeded.</param>
        /// <param name="scope">Where to report.</param>
        /// <returns>Always false, so callers can return it directly.</returns>
        private bool Fail(string code, QueryexSpan span, int limit, in DiagnosticScope scope)
        {
            if (!Exceeded)
            {
                Exceeded = true;
                scope.Report(
                    code,
                    span,
                    DiagnosticArgumentNames.Limit,
                    limit.ToString(CultureInfo.InvariantCulture));
            }

            return false;
        }
    }
}
