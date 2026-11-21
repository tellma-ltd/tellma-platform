// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Functions;
using Tellma.Core.Queryex.Syntax;

namespace Tellma.Core.Queryex.Binding
{
    /// <summary>The rules a measure has to satisfy beyond those of a select item.</summary>
    internal sealed partial class Binder
    {
        /// <summary>The construct reported for a comparison the evaluator cannot make exactly.</summary>
        private const string TextComparison = "text comparison";

        /// <summary>
        ///     Checks that a measure aggregates, and that outside its aggregations it uses only what a
        ///     host can evaluate over rolled-up partials exactly.
        /// </summary>
        /// <param name="item">The parsed item.</param>
        /// <param name="bound">The bound expression.</param>
        private void CheckMeasureRules(ExpressionItemSyntax item, TypedExpr bound)
        {
            if (!bound.ContainsAggregate || HasPathOutsideAggregate(bound))
            {
                // A measure is computed over groups that are combined after the statement has run,
                // so everything it reads of the rows has to arrive inside an aggregation. One rule
                // for both the stray path and the missing aggregation, so the select list's mixing
                // rule never fires beside it.
                _scope.Report(DiagnosticCodes.MeasureMustAggregate, item.Expression.Span);
                return;
            }

            ReportUncomputable(bound);

            if (bound.Type == BoundType.Null)
            {
                // A measure whose only possible value is absent has no type to report it at.
                _scope.Report(DiagnosticCodes.NoDeterminableType, item.Expression.Span);
            }
        }

        /// <summary>Reports every outermost construct outside the aggregations that the evaluator cannot compute.</summary>
        /// <param name="node">The node.</param>
        private void ReportUncomputable(TypedExpr node)
        {
            string? construct = UncomputableConstruct(node);
            if (construct is not null)
            {
                // Reported once, at the outermost node: what it contains would only repeat it.
                _scope.Report(DiagnosticCodes.NotComputableOverRollups, node.Span, DiagnosticArgumentNames.Construct, construct);
                return;
            }

            if (node is TypedCall { IsAggregate: true })
            {
                // An aggregation is a partial, computed by the backend: inside it the whole
                // language is available.
                return;
            }

            foreach (TypedExpr child in node.Children)
            {
                ReportUncomputable(child);
            }
        }

        /// <summary>The construct a node is, when the evaluator cannot compute it exactly.</summary>
        /// <param name="node">The node, outside every aggregation.</param>
        /// <returns>The construct's name, or null when the node is computable.</returns>
        private static string? UncomputableConstruct(TypedExpr node)
        {
            string? construct = node switch
            {
                // Remainders have no reporting use and a type rule of their own; joining text
                // belongs in dimensions and attributes.
                TypedArithmetic { Operator: ArithmeticOperator.Remainder } => "%",
                TypedArithmetic { Operator: ArithmeticOperator.Concat } => "||",

                // Text equality depends on the collation, which the evaluator only approximates.
                TypedComparison comparison when ComparesText(comparison.Left.Type, comparison.Right.Type) => TextComparison,
                TypedIn membership when ComparesText(membership.Value.Type, MemberType(membership)) => TextComparison,
                TypedCall call => UncomputableCall(call),
                _ => null,
            };

            if (construct is not null)
            {
                return construct;
            }

            // The evaluator has no value of either type to compute with.
            return node.Type switch
            {
                BoundType.Guid => "guid",
                BoundType.Geography => "geography",
                BoundType.Bool or BoundType.Numeric or BoundType.String or BoundType.Date or BoundType.DateTime
                    or BoundType.DateTimeOffset or BoundType.HierarchyId or BoundType.Null or BoundType.Error => null,
                _ => null,
            };
        }

        /// <summary>The construct a call is, when the evaluator cannot compute it exactly.</summary>
        /// <param name="call">The call, outside every aggregation.</param>
        /// <returns>The function's name, or null when the call is computable.</returns>
        private static string? UncomputableCall(TypedCall call)
        {
            string name = call.Definition.Name;
            if (call.IsAggregate)
            {
                // The partials are closed: an aggregation that does not decompose is refused until
                // it is designed.
                return name.ToUpperInvariant() is "COUNT" or "SUM" or "AVG" or "MIN" or "MAX" ? null : name;
            }

            bool computable = name.ToUpperInvariant() switch
            {
                "IF" or "COALESCE" or "ABS" or "ROUND" or "FLOOR" or "CEILING" or "TODAY" or "NOW" or "ME" => true,

                // Only the identity: converting to the operand's own type, or giving the absent
                // value a type. Every other conversion renders or parses text.
                "CAST" => call.Arguments[0].Type is BoundType.Null || call.Arguments[0].Type == call.Type,

                // Gregorian components of a calendar date; the other calendars need the month
                // table, and a date and time needs the tenant's zone.
                "YEAR" or "QUARTER" or "MONTH" or "DAY" => IsDate(call.Arguments[0].Type)
                    && (call.Selectors.Length < 2
                        || call.Selectors[1] is null
                        || string.Equals(call.Selectors[1], CalendarCodes.Gregorian, StringComparison.OrdinalIgnoreCase)),

                // Elapsed whole days between two midnights; a date and time needs the backend's
                // boundary counting.
                "DIFFDAYS" or "DIFFHOURS" or "DIFFMINUTES" or "DIFFSECONDS" =>
                    IsDate(call.Arguments[0].Type) && IsDate(call.Arguments[1].Type),

                // Every other date function, and every text function.
                _ => false,
            };

            return computable ? null : name;
        }

        /// <summary>Whether a comparison between two types compares text, identifiers or positions.</summary>
        /// <param name="left">The left operand's type.</param>
        /// <param name="right">The right operand's type.</param>
        /// <returns>True when it does.</returns>
        private static bool ComparesText(BoundType left, BoundType right)
        {
            BoundType compared = left == BoundType.Null ? right : left;
            return compared is BoundType.String or BoundType.Guid or BoundType.HierarchyId;
        }

        /// <summary>The type of a membership test's elements: the first one's that has a type.</summary>
        /// <param name="membership">The test.</param>
        /// <returns>The type, or <see cref="BoundType.Null" /> when every element is the absent value.</returns>
        private static BoundType MemberType(TypedIn membership)
        {
            foreach (TypedExpr element in membership.Elements)
            {
                if (element.Type != BoundType.Null)
                {
                    return element.Type;
                }
            }

            return BoundType.Null;
        }

        /// <summary>Whether a type is a calendar date, or the absent value read as one.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True when it is.</returns>
        private static bool IsDate(BoundType type)
        {
            return type is BoundType.Date or BoundType.Null;
        }
    }
}
