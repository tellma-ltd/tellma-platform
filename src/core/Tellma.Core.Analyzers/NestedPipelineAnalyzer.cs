// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;

namespace Tellma.Core.Analyzers
{
    /// <summary>
    ///     TELLMA0005: a front-door write of any <c>EntityService&lt;,&gt;</c> (<c>SaveAsync</c>,
    ///     <c>DeleteByIdsAsync</c>, <c>DeleteByQueryAsync</c>, <c>ExecuteActionAsync</c>,
    ///     <c>ActivateAsync</c>, <c>DeactivateAsync</c>, <c>DeleteWithDescendantsAsync</c>) from a
    ///     pipeline participant: a hook override, an <c>[EntityAction]</c> method, or any member of an
    ///     <c>IEntityValidator&lt;&gt;</c> or <c>IPersistEffect&lt;&gt;</c>.
    /// </summary>
    /// <remarks>
    ///     The rule follows the call site's own member, lambdas and local functions included, but not
    ///     the helpers it calls; the pipeline refuses those at run time.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NestedPipelineAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The service operations that run a pipeline of their own and write.</summary>
        private static readonly ImmutableHashSet<string> FrontDoorWrites = ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "SaveAsync",
            "DeleteByIdsAsync",
            "DeleteByQueryAsync",
            "ExecuteActionAsync",
            "ActivateAsync",
            "DeactivateAsync",
            "DeleteWithDescendantsAsync");

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [TellmaDiagnostics.NestedPipeline];

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        }

        /// <summary>Reports a front-door write called from a pipeline participant.</summary>
        /// <param name="context">The operation context.</param>
        private static void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            if (!IsFrontDoorWrite(invocation.TargetMethod))
            {
                return;
            }

            string? participant = Participant(context.ContainingSymbol.EnclosingMember());
            if (participant is null)
            {
                return;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                TellmaDiagnostics.NestedPipeline,
                invocation.Syntax.GetLocation(),
                $"{invocation.TargetMethod.Name} runs a nested pipeline from {participant}, which already runs inside the pipeline's frame. Enlist the write with the context's Host through IEnlists<T> (EnlistSaveAsync or EnlistDelete) instead."));
        }

        /// <summary>Whether a method is one of the entity service's front-door writes.</summary>
        /// <param name="method">The called method, possibly in its reduced extension form.</param>
        /// <returns>True when it is.</returns>
        private static bool IsFrontDoorWrite(IMethodSymbol method)
        {
            IMethodSymbol definition = method.ReducedFrom ?? method;
            return FrontDoorWrites.Contains(definition.Name)
                && (definition.ContainingType.IsOrDerivesFrom(KnownNames.CrudNamespace, KnownNames.EntityService)
                    || definition.ContainingType.Is(KnownNames.CrudNamespace, KnownNames.EntityServiceExtensions));
        }

        /// <summary>Describes the participant a member is, or null when it is none.</summary>
        /// <param name="member">The member whose body holds the call.</param>
        /// <returns>The description, for the message.</returns>
        private static string? Participant(ISymbol member)
        {
            // --- An [EntityAction] method, or an override of one of the service's hooks ---
            if (member is IMethodSymbol method)
            {
                if (method.HasInheritedAttribute(KnownNames.CrudNamespace, KnownNames.EntityActionAttribute))
                {
                    return $"the [EntityAction] method {method.Name}";
                }

                if (OverridesServiceHook(method))
                {
                    return $"the hook override {method.Name}";
                }
            }

            // --- Any member of a validator or an effect, or of a type nested in one ---
            foreach (INamedTypeSymbol type in member.EnclosingTypes())
            {
                if (type.IsOrImplements(KnownNames.CrudNamespace, KnownNames.EntityValidator))
                {
                    return $"the validator {type.Name}";
                }

                if (type.IsOrImplements(KnownNames.CrudNamespace, KnownNames.PersistEffect))
                {
                    return $"the persist effect {type.Name}";
                }
            }

            return null;
        }

        /// <summary>Whether a method overrides a virtual member the entity service base declares.</summary>
        /// <param name="method">The method.</param>
        /// <returns>True when it does.</returns>
        private static bool OverridesServiceHook(IMethodSymbol method)
        {
            for (IMethodSymbol? current = method.OverriddenMethod; current is not null; current = current.OverriddenMethod)
            {
                if (current.ContainingType.Is(KnownNames.CrudNamespace, KnownNames.EntityService))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
