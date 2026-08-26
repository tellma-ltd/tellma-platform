// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using System.Globalization;
using Tellma.Core.Queryex.Diagnostics;
using Tellma.Core.Queryex.Functions;
using Tellma.Core.Queryex.Syntax;
using Tellma.Core.Queryex.Time;

namespace Tellma.Core.Queryex.Binding
{
    /// <summary>Resolving a call to one overload of one function.</summary>
    internal sealed partial class Binder
    {
        /// <summary>One overload under consideration.</summary>
        /// <param name="Signature">The overload.</param>
        /// <param name="Viable">Whether every argument could be read as it wants.</param>
        /// <param name="Arguments">The bound arguments, when viable.</param>
        /// <param name="Score">How well the arguments fitted.</param>
        /// <param name="FirstFailing">The first argument that would not fit, when one would not.</param>
        /// <param name="ResultType">The result type this overload would give.</param>
        /// <param name="RestStart">Where the variadic tail begins, or -1.</param>
        private sealed record Candidate(
            FunctionSignature Signature,
            bool Viable,
            ImmutableArray<TypedExpr> Arguments,
            int Score,
            int FirstFailing,
            BoundType ResultType,
            int RestStart);

        /// <summary>Resolves a call.</summary>
        /// <param name="call">The call.</param>
        /// <param name="expected">The type the surrounding expression wants, when it wants one.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr ResolveCall(CallSyntax call, BoundType? expected)
        {
            FunctionDefinition? definition = FunctionRegistry.Find(call.Name);
            if (definition is null)
            {
                // Under the function key, the same as every other diagnostic about a call. A
                // catalogue keyed one way and a report written the other way silently compose a
                // message with a hole in it.
                Report(
                    DiagnosticCodes.UnknownFunction,
                    call.NameSpan,
                    DiagnosticArgumentNames.Function,
                    call.Name,
                    always: true);

                return new TypedError(call.Span);
            }

            if (definition.Category == FunctionCategory.Aggregate && !CheckAggregatePlacement(call))
            {
                return new TypedError(call.Span);
            }

            List<FunctionSignature> byArity = [.. definition.Signatures.Where(
                signature => call.Arguments.Count >= signature.MinArity
                    && call.Arguments.Count <= signature.MaxArity)];

            if (byArity.Count == 0)
            {
                // Told apart from "no overload matches these types", because the two mean quite
                // different things to whoever has to fix the expression.
                Report(
                    DiagnosticCodes.NoOverloadForArgumentCount,
                    call.Span,
                    always: true,
                    new KeyValuePair<string, string>(DiagnosticArgumentNames.Function, definition.Name),
                    new KeyValuePair<string, string>(
                        DiagnosticArgumentNames.Actual,
                        call.Arguments.Count.ToString(CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, string>(
                        DiagnosticArgumentNames.Expected,
                        string.Join(", ", definition.Signatures.Select(
                            static signature => signature.Describe(string.Empty)))));

                return new TypedError(call.Span);
            }

            bool aggregate = definition.Category == FunctionCategory.Aggregate;
            if (aggregate)
            {
                _aggregateDepth++;
            }

            try
            {
                List<Candidate> candidates = [];
                _speculation++;
                try
                {
                    foreach (FunctionSignature signature in byArity)
                    {
                        candidates.Add(TryCandidate(call, signature, expected));
                    }
                }
                finally
                {
                    _speculation--;
                }

                return Choose(call, definition, candidates);
            }
            finally
            {
                if (aggregate)
                {
                    _aggregateDepth--;
                }
            }
        }

        /// <summary>Checks that an aggregation is allowed where it was written.</summary>
        /// <param name="call">The call.</param>
        /// <returns>True when it is allowed.</returns>
        private bool CheckAggregatePlacement(CallSyntax call)
        {
            if (_aggregateDepth > 0)
            {
                // Reported against the inner call: the outer one is fine, it is this one that has
                // nothing left to aggregate over.
                Report(DiagnosticCodes.NestedAggregation, call.Span, always: true);
                return false;
            }

            if (!_context.AllowsAggregation)
            {
                Report(DiagnosticCodes.AggregationNotPermitted, call.Span, always: true);
                return false;
            }

            return true;
        }

        /// <summary>Picks the overload that fits best, or reports why none does.</summary>
        /// <param name="call">The call.</param>
        /// <param name="definition">The function.</param>
        /// <param name="candidates">The overloads that were tried.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr Choose(
            CallSyntax call,
            FunctionDefinition definition,
            List<Candidate> candidates)
        {
            List<Candidate> viable = [.. candidates.Where(static candidate => candidate.Viable)];
            if (viable.Count == 0)
            {
                ReportNoOverload(call, definition, candidates);
                return new TypedError(call.Span);
            }

            int best = viable.Max(static candidate => candidate.Score);
            List<Candidate> winners = [.. viable.Where(candidate => candidate.Score == best)];
            if (winners.Count > 1)
            {
                // Two overloads nothing could tell apart is a defect in the function library rather
                // than in the expression, and a test over the library itself catches it first.
                Report(
                    DiagnosticCodes.AmbiguousOverload,
                    call.Span,
                    DiagnosticArgumentNames.Function,
                    definition.Name,
                    always: true);

                return new TypedError(call.Span);
            }

            return Build(call, definition, winners[0]);
        }

        /// <summary>Reports that nothing matched, as specifically as it can.</summary>
        /// <param name="call">The call.</param>
        /// <param name="definition">The function.</param>
        /// <param name="candidates">The overloads that were tried.</param>
        private void ReportNoOverload(
            CallSyntax call,
            FunctionDefinition definition,
            List<Candidate> candidates)
        {
            // One mistake is common enough, and specific enough, to deserve its own answer: giving a
            // calendar operation an instant that carries its own offset. A calendar boundary is a
            // position in someone's local time, so the zone has to be named first — and saying that
            // is far more use than listing every overload that did not match.
            foreach (Candidate candidate in candidates)
            {
                ImmutableArray<FunctionParameter> parameters = candidate.Signature.Parameters;
                for (int index = 0; index < call.Arguments.Count && index < parameters.Length; index++)
                {
                    if (parameters[index].Type.RequiresZoneResolution
                        && Synth(call.Arguments[index]).Type == BoundType.DateTimeOffset)
                    {
                        Report(
                            DiagnosticCodes.ZoneResolutionRequired,
                            call.Arguments[index].Span,
                            DiagnosticArgumentNames.Function,
                            definition.Name,
                            always: true);

                        return;
                    }
                }
            }

            Report(
                DiagnosticCodes.NoOverloadForArgumentTypes,
                call.Span,
                always: true,
                new KeyValuePair<string, string>(DiagnosticArgumentNames.Function, definition.Name),
                new KeyValuePair<string, string>(
                    DiagnosticArgumentNames.Signatures,
                    string.Join(
                        "; ",
                        candidates.Select(candidate => Describe(definition.Name, candidate)))));
        }

        /// <summary>Renders one overload and the argument that stopped it.</summary>
        /// <param name="name">The function name.</param>
        /// <param name="candidate">The overload.</param>
        /// <returns>The rendered overload.</returns>
        private static string Describe(string name, Candidate candidate)
        {
            string signature = candidate.Signature.Describe(name);
            return candidate.FirstFailing < 0
                ? signature
                : string.Create(CultureInfo.InvariantCulture, $"{signature}@{candidate.FirstFailing}");
        }

        /// <summary>Tries to read every argument the way one overload wants it.</summary>
        /// <param name="call">The call.</param>
        /// <param name="signature">The overload.</param>
        /// <param name="expected">The type the surrounding expression wants, when it wants one.</param>
        /// <returns>What it concluded.</returns>
        private Candidate TryCandidate(CallSyntax call, FunctionSignature signature, BoundType? expected)
        {
            ImmutableArray<FunctionParameter> parameters = signature.Parameters;
            int restStart = parameters.Length > 0 && parameters[^1].Rest ? parameters.Length - 1 : -1;
            var bound = new TypedExpr[call.Arguments.Count];
            Dictionary<string, BoundType> variables = new(StringComparer.Ordinal);
            int score = 0;

            // Arguments that share a type variable are read together rather than one at a time, so
            // a written literal on one side can take its type from a column on the other.
            foreach (IGrouping<string, int> group in GroupByVariable(call, parameters, restStart))
            {
                BoundType? seed = signature.Returns.Variable == group.Key ? expected : null;
                List<SyntaxNode> operands = [.. group.Select(index => call.Arguments[index])];
                TypeMask mask = parameters[ParameterOf(group.First(), parameters, restStart)].Type.Admits;

                if (!TryUnifyCore(operands, seed, out BoundType agreed, out ImmutableArray<TypedExpr> operandNodes, out int failed)
                    || !(agreed == BoundType.Null || BoundTypes.Admits(mask, agreed)))
                {
                    return Failed(signature, failed < 0 ? group.First() : group.ElementAt(failed), restStart);
                }

                variables[group.Key] = agreed;
                int position = 0;
                foreach (int index in group)
                {
                    bound[index] = operandNodes[position];
                    score += Synth(call.Arguments[index]).Type == agreed ? 2 : 1;
                    position++;
                }
            }

            for (int index = 0; index < call.Arguments.Count; index++)
            {
                FunctionParameter parameter = parameters[ParameterOf(index, parameters, restStart)];
                if (parameter.Type.Variable is not null)
                {
                    continue;
                }

                if (!BoundTypes.TrySingle(parameter.Type.Admits, out BoundType wanted)
                    || !TryCheck(call.Arguments[index], wanted, out TypedExpr argument, out CoercionCost cost))
                {
                    return Failed(signature, index, restStart);
                }

                bound[index] = argument;
                score += cost == CoercionCost.Exact ? 2 : 1;
            }

            return new Candidate(
                signature,
                Viable: true,
                [.. bound],
                score,
                FirstFailing: -1,
                ResultOf(call, signature, variables),
                restStart);
        }

        /// <summary>Builds the record for an overload that did not fit.</summary>
        /// <param name="signature">The overload.</param>
        /// <param name="failing">The argument that stopped it.</param>
        /// <param name="restStart">Where the variadic tail begins, or -1.</param>
        /// <returns>The record.</returns>
        private static Candidate Failed(FunctionSignature signature, int failing, int restStart)
        {
            return new Candidate(signature, Viable: false, [], 0, failing, BoundType.Error, restStart);
        }

        /// <summary>Groups the arguments that share a type variable.</summary>
        /// <param name="call">The call.</param>
        /// <param name="parameters">The overload's parameters.</param>
        /// <param name="restStart">Where the variadic tail begins, or -1.</param>
        /// <returns>The groups, in the order their variables were first named.</returns>
        private static IEnumerable<IGrouping<string, int>> GroupByVariable(
            CallSyntax call,
            ImmutableArray<FunctionParameter> parameters,
            int restStart)
        {
            return Enumerable.Range(0, call.Arguments.Count)
                .Where(index => parameters[ParameterOf(index, parameters, restStart)].Type.Variable is not null)
                .GroupBy(index => parameters[ParameterOf(index, parameters, restStart)].Type.Variable!,
                    StringComparer.Ordinal);
        }

        /// <summary>Which parameter an argument position belongs to.</summary>
        /// <param name="index">The argument position.</param>
        /// <param name="parameters">The overload's parameters.</param>
        /// <param name="restStart">Where the variadic tail begins, or -1.</param>
        /// <returns>The parameter position.</returns>
        private static int ParameterOf(int index, ImmutableArray<FunctionParameter> parameters, int restStart)
        {
            return restStart >= 0 && index >= restStart ? restStart : Math.Min(index, parameters.Length - 1);
        }

        /// <summary>The result type an overload would give.</summary>
        /// <param name="call">The call.</param>
        /// <param name="signature">The overload.</param>
        /// <param name="variables">The types its variables took.</param>
        /// <returns>The result type.</returns>
        private static BoundType ResultOf(
            CallSyntax call,
            FunctionSignature signature,
            Dictionary<string, BoundType> variables)
        {
            if (signature.Returns.Variable is string variable)
            {
                return variables.TryGetValue(variable, out BoundType bound) ? bound : BoundType.Error;
            }

            if (signature.Returns.FromSelectorAt is int selector)
            {
                // The result type is whatever the selector names. An argument that is not a valid
                // selector is caught when the winning overload's restrictions are checked, and the
                // error type it yields here keeps everything built on top of it quiet meanwhile.
                return selector < call.Arguments.Count
                    && SyntaxPrinter.Unwrap(call.Arguments[selector]) is StringSyntax name
                    && CastRules.TryResolveTarget(name.Value, out BoundType target)
                    ? target
                    : BoundType.Error;
            }

            return BoundTypes.TrySingle(signature.Returns.Admits, out BoundType single)
                ? single
                : BoundType.Error;
        }

        /// <summary>Checks the winning overload's restrictions and builds the call.</summary>
        /// <param name="call">The call.</param>
        /// <param name="definition">The function.</param>
        /// <param name="winner">The overload that won.</param>
        /// <returns>The bound node.</returns>
        private TypedExpr Build(CallSyntax call, FunctionDefinition definition, Candidate winner)
        {
            FunctionSignature signature = winner.Signature;
            string?[] selectors = new string?[signature.Parameters.Length];
            bool healthy = true;

            for (int index = 0; index < call.Arguments.Count; index++)
            {
                int position = ParameterOf(index, signature.Parameters, winner.RestStart);
                FunctionParameter parameter = signature.Parameters[position];
                if (!CheckConstraint(call, definition, parameter, index, winner, out string? selector))
                {
                    healthy = false;
                    continue;
                }

                if (selector is not null)
                {
                    selectors[position] = selector;
                }
            }

            // Whether the conversion a call asks for exists depends on both its source and its
            // target, so it can only be settled once the winning overload has told us what the
            // target is.
            ImmutableArray<ResolvedSynthetic> synthetic = [];
            // An argument that did not bind makes the call ill-formed however well the overload
            // scored. Without this a node the binder gave up on can sit inside a tree the rest of
            // the pipeline treats as healthy.
            bool rejected = !healthy
                || winner.ResultType == BoundType.Error
                || winner.Arguments.Any(argument => argument.Type == BoundType.Error)
                || (definition.Name.Equals("cast", StringComparison.OrdinalIgnoreCase)
                    && !CheckCastIsSupported(call, winner))
                || !TryResolveSynthetic(call, signature, selectors, out synthetic);

            if (!rejected)
            {
                // The overload has won, so what it asked of each argument is now a real demand
                // rather than one possibility among several.
                foreach (TypedExpr argument in winner.Arguments)
                {
                    NoteInference([argument], argument.Type);
                }
            }

            return rejected
                ? new TypedError(call.Span)
                : new TypedCall(
                    call.Span,
                    winner.ResultType,
                    definition,
                    signature,
                    winner.Arguments,
                    winner.RestStart,
                    [.. selectors],
                    synthetic);
        }

        /// <summary>Checks that the conversion a call asks for exists.</summary>
        /// <param name="call">The call.</param>
        /// <param name="winner">The overload that won.</param>
        /// <returns>True when it does.</returns>
        private bool CheckCastIsSupported(CallSyntax call, Candidate winner)
        {
            BoundType source = winner.Arguments[0].Type;
            if (CastRules.IsSupported(source, winner.ResultType))
            {
                return true;
            }

            Report(
                DiagnosticCodes.UnsupportedCast,
                call.Span,
                always: true,
                new KeyValuePair<string, string>(DiagnosticArgumentNames.Type, BoundTypes.Name(source)),
                new KeyValuePair<string, string>(
                    DiagnosticArgumentNames.OtherType,
                    BoundTypes.Name(winner.ResultType)));

            return false;
        }

        /// <summary>Checks one argument's restriction.</summary>
        /// <param name="call">The call.</param>
        /// <param name="definition">The function.</param>
        /// <param name="parameter">The parameter it fills.</param>
        /// <param name="index">The argument position.</param>
        /// <param name="winner">The overload that won.</param>
        /// <param name="selector">The consumed literal's value, when the restriction consumes one.</param>
        /// <returns>True when the argument satisfies it.</returns>
        private bool CheckConstraint(
            CallSyntax call,
            FunctionDefinition definition,
            FunctionParameter parameter,
            int index,
            Candidate winner,
            out string? selector)
        {
            selector = null;
            SyntaxNode argument = SyntaxPrinter.Unwrap(call.Arguments[index]);

            switch (parameter.Constraint.Kind)
            {
                case ParameterConstraintKind.None:
                    return true;

                case ParameterConstraintKind.LiteralOnly:
                    if (!IsLiteral(argument))
                    {
                        return FailConstraint(DiagnosticCodes.ArgumentMustBeLiteral, argument.Span, definition);
                    }

                    // A restriction that consumes its argument needs the value itself, not merely
                    // the knowledge that there was one: it is what the engine looks something up by.
                    if (parameter.Constraint.Consumed && argument is StringSyntax consumed)
                    {
                        selector = consumed.Value;
                    }

                    return true;

                case ParameterConstraintKind.MemberOf:
                    if (argument is not StringSyntax text)
                    {
                        return FailConstraint(DiagnosticCodes.ArgumentMustBeLiteral, argument.Span, definition);
                    }

                    if (!parameter.Constraint.Values!.Contains(text.Value))
                    {
                        Report(
                            DiagnosticCodes.ArgumentValueNotAccepted,
                            argument.Span,
                            always: true,
                            new KeyValuePair<string, string>(DiagnosticArgumentNames.Function, definition.Name),
                            new KeyValuePair<string, string>(DiagnosticArgumentNames.Name, text.Value),
                            new KeyValuePair<string, string>(
                                DiagnosticArgumentNames.Accepted,
                                string.Join(", ", parameter.Constraint.Values!.Order(StringComparer.Ordinal))));

                        return false;
                    }

                    selector = text.Value;
                    return true;

                case ParameterConstraintKind.TreeNodeKeyPath:
                    return CheckTreeNodeKey(argument, winner.Arguments[index], definition);

                case ParameterConstraintKind.PathFree:
                default:

                    // A key that read a column would differ from row to row, and the whole point of
                    // looking a node up once before the statement runs is that it does not.
                    return !winner.Arguments[index].ContainsPath
                        || FailConstraint(DiagnosticCodes.HierarchyKeyContainsPath, argument.Span, definition);
            }
        }

        /// <summary>Checks that a hierarchy predicate's key names something it can look up.</summary>
        /// <param name="argument">The argument as written.</param>
        /// <param name="bound">The bound argument.</param>
        /// <param name="definition">The function.</param>
        /// <returns>True when it does.</returns>
        private bool CheckTreeNodeKey(SyntaxNode argument, TypedExpr bound, FunctionDefinition definition)
        {
            if (bound is not TypedPath path)
            {
                // Anything but a bare path leaves nothing to look a node up in, and nothing to
                // decide which rows a key could possibly identify.
                return FailConstraint(DiagnosticCodes.HierarchyKeyMustBePath, argument.Span, definition);
            }

            EntityDescriptor entity = path.Navigations.IsEmpty
                ? _context.Root!
                : path.Navigations[^1].Target;

            if (entity.TreeNode is null)
            {
                return FailConstraint(DiagnosticCodes.EntityNotHierarchical, argument.Span, definition);
            }

            // Without uniqueness a key could name several rows, and the lookup would have to pick
            // one of them arbitrarily — which would make the same expression mean different things
            // on different days.
            return path.Property.IsUnique
                || FailConstraint(DiagnosticCodes.HierarchyKeyNotUnique, argument.Span, definition);
        }

        /// <summary>Reports a restriction failure.</summary>
        /// <param name="code">The diagnostic code.</param>
        /// <param name="span">The offending range.</param>
        /// <param name="definition">The function.</param>
        /// <returns>Always false, so callers can return it directly.</returns>
        private bool FailConstraint(string code, QueryexSpan span, FunctionDefinition definition)
        {
            Report(code, span, DiagnosticArgumentNames.Function, definition.Name, always: true);
            return false;
        }

        /// <summary>Whether an argument is a written literal.</summary>
        /// <param name="node">The argument, with any grouping already peeled.</param>
        /// <returns>True when it is.</returns>
        private static bool IsLiteral(SyntaxNode node)
        {
            return node is NumberSyntax or StringSyntax or BooleanSyntax;
        }

        /// <summary>Resolves the arguments the engine supplies itself.</summary>
        /// <param name="call">The call.</param>
        /// <param name="signature">The overload that won.</param>
        /// <param name="selectors">The consumed literal values, by parameter.</param>
        /// <param name="resolved">The resolved arguments.</param>
        /// <returns>True when they all resolved.</returns>
        private bool TryResolveSynthetic(
            CallSyntax call,
            FunctionSignature signature,
            string?[] selectors,
            out ImmutableArray<ResolvedSynthetic> resolved)
        {
            resolved = [];
            ImmutableArray<ResolvedSynthetic>.Builder builder =
                ImmutableArray.CreateBuilder<ResolvedSynthetic>(signature.Synthetic.Length);

            foreach (SyntheticArgument synthetic in signature.Synthetic)
            {
                if (synthetic.FromParameter is not int parameter)
                {
                    builder.Add(new ResolvedSynthetic(synthetic.Origin, synthetic.Type, null));
                    continue;
                }

                string zone = selectors[parameter]!;

                // Resolved here, from a table embedded in the engine, rather than from whatever
                // zone data the machine doing the compiling happens to have. Which identifiers
                // compile has to be a property of the language, not of a host.
                if (!CldrTimeZones.TryResolve(zone, out string? backendName))
                {
                    Report(
                        DiagnosticCodes.ArgumentValueNotAccepted,
                        call.Arguments[parameter].Span,
                        always: true,
                        new KeyValuePair<string, string>(DiagnosticArgumentNames.Name, zone),
                        new KeyValuePair<string, string>(
                            DiagnosticArgumentNames.Function,
                            "local"));

                    return false;
                }

                builder.Add(new ResolvedSynthetic(synthetic.Origin, synthetic.Type, backendName));
            }

            resolved = builder.ToImmutable();
            return true;
        }

        /// <summary>Folds a group of operands to one type, without reporting anything.</summary>
        /// <param name="operands">The operands.</param>
        /// <param name="seed">A type the surrounding expression already demands, when there is one.</param>
        /// <param name="agreed">The agreed type.</param>
        /// <param name="bound">The bound operands.</param>
        /// <param name="failed">The first operand that would not agree, when one would not.</param>
        /// <returns>True when they agree.</returns>
        private bool TryUnifyCore(
            List<SyntaxNode> operands,
            BoundType? seed,
            out BoundType agreed,
            out ImmutableArray<TypedExpr> bound,
            out int failed)
        {
            agreed = BoundType.Error;
            bound = [];
            failed = -1;

            BoundType? current = seed;
            if (current is null)
            {
                foreach (SyntaxNode operand in operands)
                {
                    BoundType candidate = ProbeType(operand);
                    if (candidate is not (BoundType.Null or BoundType.Error))
                    {
                        current = candidate;
                        break;
                    }
                }
            }

            if (current is null)
            {
                agreed = BoundType.Null;
                bound = [.. operands.Select(Synth)];
                return true;
            }

            if (TryBindAll(operands, current.Value, out bound, out failed))
            {
                agreed = current.Value;
                return true;
            }

            BoundType alternative = ProbeType(operands[failed]);
            if (alternative is not (BoundType.Null or BoundType.Error)
                && alternative != current.Value
                && TryBindAll(operands, alternative, out bound, out failed))
            {
                agreed = alternative;
                return true;
            }

            return false;
        }
    }
}
