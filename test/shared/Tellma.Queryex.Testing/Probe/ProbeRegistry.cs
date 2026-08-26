// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Core.Queryex.Emit;
using Tellma.Core.Queryex.Functions;

namespace Tellma.Queryex.Testing.Probe
{
    /// <summary>One declared parameter of one overload, rendered.</summary>
    /// <param name="Name">The parameter's name.</param>
    /// <param name="Types">The types it accepts.</param>
    /// <param name="Variable">The type variable it shares, when it shares one.</param>
    /// <param name="Rest">Whether it is a variadic tail.</param>
    /// <param name="Propagates">Whether a demand on the call reaches it.</param>
    /// <param name="Constraint">What else the argument has to satisfy.</param>
    /// <param name="AcceptedValues">The values a closed set accepts.</param>
    public sealed record ProbeParameter(
        string Name,
        string Types,
        string? Variable,
        bool Rest,
        bool Propagates,
        string Constraint,
        IReadOnlyList<string> AcceptedValues);

    /// <summary>One overload, rendered.</summary>
    /// <param name="Index">Its position among the function's overloads.</param>
    /// <param name="Parameters">The declared parameters.</param>
    /// <param name="Returns">The result type or variable.</param>
    /// <param name="Nullity">How the result's nullity is decided.</param>
    /// <param name="Strategy">How the call becomes SQL.</param>
    /// <param name="Pattern">The emission pattern, where the strategy has one fixed pattern.</param>
    /// <param name="UseCounts">How many times each argument appears in the produced SQL.</param>
    /// <param name="ResultShape">Whether the call produces a truth value or a scalar.</param>
    /// <param name="StoredAs">
    ///     The type the backend holds the result in, where the pattern fixes it. Null where the
    ///     result follows the operands instead.
    /// </param>
    /// <param name="Follows">The argument whose stored type the result takes, where it takes one.</param>
    /// <param name="SyntheticCount">How many arguments the engine supplies itself.</param>
    public sealed record ProbeSignature(
        int Index,
        IReadOnlyList<ProbeParameter> Parameters,
        string Returns,
        string Nullity,
        string Strategy,
        string? Pattern,
        IReadOnlyList<int> UseCounts,
        string ResultShape,
        QueryexStoreType? StoredAs,
        int? Follows,
        int SyntheticCount);

    /// <summary>One function, rendered.</summary>
    /// <param name="Name">The function's name.</param>
    /// <param name="Category">Whether it reads one row or a whole group.</param>
    /// <param name="Signatures">Its overloads.</param>
    public sealed record ProbeFunction(
        string Name,
        string Category,
        IReadOnlyList<ProbeSignature> Signatures);

    /// <summary>The function library, rendered.</summary>
    public static class ProbeRegistry
    {
        /// <summary>Every function the language has.</summary>
        public static IReadOnlyList<ProbeFunction> Functions { get; } = Render();

        /// <summary>Every name a conversion may target.</summary>
        public static IReadOnlyList<string> CastTargets { get; } = CastRules.TargetNames;

        /// <summary>Whether a conversion exists between two named types.</summary>
        /// <param name="from">The source type's name.</param>
        /// <param name="to">The target type's name.</param>
        /// <returns>True when it does.</returns>
        public static bool SupportsCast(string from, string to)
        {
            return CastRules.TryResolveTarget(from, out Core.Queryex.Binding.BoundType source)
                && CastRules.TryResolveTarget(to, out Core.Queryex.Binding.BoundType target)
                && CastRules.IsSupported(source, target);
        }

        /// <summary>Renders the library.</summary>
        /// <returns>The rendered functions.</returns>
        private static List<ProbeFunction> Render()
        {
            List<ProbeFunction> functions = [];
            foreach (FunctionDefinition definition in FunctionRegistry.All)
            {
                List<ProbeSignature> signatures = [];
                for (int index = 0; index < definition.Signatures.Length; index++)
                {
                    signatures.Add(RenderSignature(index, definition.Signatures[index]));
                }

                functions.Add(new ProbeFunction(
                    definition.Name,
                    definition.Category.ToString(),
                    signatures));
            }

            return functions;
        }

        /// <summary>Renders one overload.</summary>
        /// <param name="index">Its position among the function's overloads.</param>
        /// <param name="signature">The overload.</param>
        /// <returns>The rendered overload.</returns>
        private static ProbeSignature RenderSignature(int index, FunctionSignature signature)
        {
            List<ProbeParameter> parameters = [];
            foreach (FunctionParameter parameter in signature.Parameters)
            {
                parameters.Add(new ProbeParameter(
                    parameter.Name,
                    Core.Queryex.Binding.BoundTypes.Names(parameter.Type.Admits),
                    parameter.Type.Variable,
                    parameter.Rest,
                    parameter.Propagates,
                    parameter.Constraint.Kind.ToString(),
                    [.. parameter.Constraint.Values ?? []]));
            }

            EmitStrategy strategy = signature.Emit;
            EmitTemplate? template = strategy switch
            {
                TemplateStrategy fixedTemplate => fixedTemplate.Template,
                SelectedStrategy selected => selected.Cases.Values[0],
                _ => null,
            };

            return new ProbeSignature(
                index,
                parameters,
                signature.Returns.Variable ?? Core.Queryex.Binding.BoundTypes.Names(signature.Returns.Admits),
                signature.Nullity.Kind.ToString(),
                strategy.GetType().Name,
                template?.Pattern,
                [.. strategy.UseCounts],
                strategy.ResultShape.ToString(),
                template?.Result,
                template?.Follows,
                signature.Synthetic.Length);
        }
    }
}
