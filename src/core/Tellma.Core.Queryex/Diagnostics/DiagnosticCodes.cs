// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Reflection;

namespace Tellma.Core.Queryex.Diagnostics
{
    /// <summary>
    ///     Every diagnostic code the engine can produce.
    /// </summary>
    /// <remarks>
    ///     The single catalogue: nothing anywhere constructs a diagnostic from a string literal, so
    ///     the set of reachable codes is enumerable, and a test can prove that every one of them is
    ///     actually reachable by an expression rather than merely declared.
    /// </remarks>
    internal static class DiagnosticCodes
    {
        /// <summary>A string literal was opened and never closed.</summary>
        internal const string UnterminatedString = "QX1001";

        /// <summary>A bracketed identifier was opened and never closed.</summary>
        internal const string UnterminatedBracketedIdentifier = "QX1002";

        /// <summary>A number is malformed, such as a missing digit beside the decimal point.</summary>
        internal const string MalformedNumber = "QX1003";

        /// <summary>A number exceeds the supported decimal precision.</summary>
        internal const string NumberPrecisionExceeded = "QX1004";

        /// <summary>A character outside the language appeared.</summary>
        internal const string UnexpectedCharacter = "QX1005";

        /// <summary>A token appeared where the grammar expected something else.</summary>
        internal const string UnexpectedToken = "QX2001";

        /// <summary>A parenthesis was opened and never closed.</summary>
        internal const string UnbalancedParenthesis = "QX2002";

        /// <summary>A list item is empty, as with a leading, trailing, or doubled comma.</summary>
        internal const string EmptyListItem = "QX2003";

        /// <summary>A call argument is empty.</summary>
        internal const string EmptyArgument = "QX2004";

        /// <summary>A parenthesis pair encloses nothing and does not follow a function name.</summary>
        internal const string EmptyParentheses = "QX2005";

        /// <summary>Comparisons were chained, which the grammar does not allow.</summary>
        internal const string ChainedComparison = "QX2006";

        /// <summary>A direction suffix appeared where directions are not accepted.</summary>
        internal const string DirectionNotPermitted = "QX2007";

        /// <summary>A direction suffix is not the final token of its item at depth zero.</summary>
        internal const string DirectionMustTerminateItem = "QX2008";

        /// <summary>A predicate position was given a list rather than a single expression.</summary>
        internal const string PredicateListNotPermitted = "QX2009";

        /// <summary>A path segment names nothing the entity declares.</summary>
        internal const string UnknownProperty = "QX3001";

        /// <summary>A path stops at a navigation rather than at a scalar property.</summary>
        internal const string PathEndsAtNavigation = "QX3002";

        /// <summary>No function goes by that name.</summary>
        internal const string UnknownFunction = "QX3003";

        /// <summary>The function exists, but no overload accepts that many arguments.</summary>
        internal const string NoOverloadForArgumentCount = "QX3004";

        /// <summary>The function exists with that arity, but no overload accepts those types.</summary>
        internal const string NoOverloadForArgumentTypes = "QX3005";

        /// <summary>More than one overload matches equally well.</summary>
        internal const string AmbiguousOverload = "QX3006";

        /// <summary>A parameter was used but never declared.</summary>
        internal const string UndeclaredParameter = "QX3007";

        /// <summary>An argument that has to be known at compile time is not a literal.</summary>
        internal const string ArgumentMustBeLiteral = "QX3100";

        /// <summary>A literal argument's value is not one this position accepts.</summary>
        internal const string ArgumentValueNotAccepted = "QX3101";

        /// <summary>The requested conversion does not exist.</summary>
        internal const string UnsupportedCast = "QX3102";

        /// <summary>
        ///     A calendar operation was given a value carrying its own offset. A calendar boundary is
        ///     a position in someone's local time, so the zone has to be made explicit first.
        /// </summary>
        internal const string ZoneResolutionRequired = "QX3103";

        /// <summary>Operands that have to agree on a type do not.</summary>
        internal const string IncompatibleOperandTypes = "QX3200";

        /// <summary>The operand's type is not valid for this operator or position.</summary>
        internal const string OperandTypeNotValid = "QX3201";

        /// <summary>The expression has no determinable type, as with a bare absent value.</summary>
        internal const string NoDeterminableType = "QX3202";

        /// <summary>A hierarchy predicate's first argument is not a bare path.</summary>
        internal const string HierarchyKeyMustBePath = "QX3300";

        /// <summary>A hierarchy predicate was applied to an entity that is not hierarchical.</summary>
        internal const string EntityNotHierarchical = "QX3301";

        /// <summary>A hierarchy predicate's lookup property is not unique, so a key could match twice.</summary>
        internal const string HierarchyKeyNotUnique = "QX3302";

        /// <summary>A hierarchy predicate's key argument reads a path, so it is not row-invariant.</summary>
        internal const string HierarchyKeyContainsPath = "QX3303";

        /// <summary>A parameter's uses demand incompatible types.</summary>
        internal const string ConflictingParameterTypes = "QX3400";

        /// <summary>The position requires a truth value and the expression is not one.</summary>
        internal const string MustBePredicate = "QX4001";

        /// <summary>Aggregations are not permitted in this position.</summary>
        internal const string AggregationNotPermitted = "QX4002";

        /// <summary>An aggregation appears inside another aggregation.</summary>
        internal const string NestedAggregation = "QX4003";

        /// <summary>A group-level predicate reads a path from outside an aggregation.</summary>
        internal const string PathOutsideAggregation = "QX4004";

        /// <summary>An ordering term would widen the grouping of a grouped statement.</summary>
        internal const string OrderingAltersGrouping = "QX4005";

        /// <summary>Paging was requested without an explicit ordering, so pages would not be reproducible.</summary>
        internal const string PagingRequiresOrdering = "QX4006";

        /// <summary>An item reads paths both inside and outside an aggregation, so it is neither key nor measure.</summary>
        internal const string MixedPathsInsideAndOutsideAggregation = "QX4007";

        /// <summary>Two ordering terms are the same expression.</summary>
        internal const string DuplicateOrderingTerm = "QX4008";

        /// <summary>The expression text is longer than this call site permits.</summary>
        internal const string MaxInputLength = "QX5001";

        /// <summary>The expression has more tokens than this call site permits.</summary>
        internal const string MaxTokens = "QX5002";

        /// <summary>The expression nests more deeply than this call site permits.</summary>
        internal const string MaxSyntaxDepth = "QX5003";

        /// <summary>The query binds more nodes than this call site permits.</summary>
        internal const string MaxTypedNodes = "QX5004";

        /// <summary>The list has more items than this call site permits.</summary>
        internal const string MaxListItems = "QX5005";

        /// <summary>The query needs more joins than this call site permits.</summary>
        internal const string MaxJoins = "QX5006";

        /// <summary>The query needs more parameter slots than this call site permits.</summary>
        internal const string MaxParameters = "QX5007";

        /// <summary>Every declared code, for the coverage gate and for host message catalogues.</summary>
        /// <remarks>
        ///     Read off the declarations rather than listed beside them. A list maintained by hand
        ///     is a list that goes stale, and the one thing this set is for is telling whether a
        ///     code the engine can report has a test and a message — which it cannot do if adding
        ///     the code and adding it here are two separate steps.
        /// </remarks>
        internal static FrozenSet<string> All { get; } = FrozenSet.ToFrozenSet(
            typeof(DiagnosticCodes)
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue()!),
            StringComparer.Ordinal);
    }

    /// <summary>
    ///     The argument names diagnostics carry.
    /// </summary>
    /// <remarks>
    ///     Constants rather than inline strings, so a host's message catalogue and the engine cannot
    ///     drift on the spelling of a key.
    /// </remarks>
    internal static class DiagnosticArgumentNames
    {
        /// <summary>The offending name, as the author wrote it.</summary>
        internal const string Name = "name";

        /// <summary>The entity a name was looked for on.</summary>
        internal const string Entity = "entity";

        /// <summary>A type involved in the problem.</summary>
        internal const string Type = "type";

        /// <summary>A second type, where two are involved.</summary>
        internal const string OtherType = "otherType";

        /// <summary>The function involved.</summary>
        internal const string Function = "function";

        /// <summary>What was expected, as a machine-readable list.</summary>
        internal const string Expected = "expected";

        /// <summary>What was found.</summary>
        internal const string Actual = "actual";

        /// <summary>The ceiling that was exceeded.</summary>
        internal const string Limit = "limit";

        /// <summary>The operator involved.</summary>
        internal const string Operator = "operator";

        /// <summary>The candidate signatures, for an overload failure.</summary>
        internal const string Signatures = "signatures";

        /// <summary>The accepted values, for a constrained literal argument.</summary>
        internal const string Accepted = "accepted";
    }
}
