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
    ///     TELLMA0004: a call to a method marked <c>[ApiAction]</c> or <c>[EntityAction]</c> from
    ///     outside its declaring type and <c>Tellma.Core</c>. The invoker and the pipeline evaluate the
    ///     action's securable; a direct call would not.
    /// </summary>
    /// <remarks>
    ///     A call from the declaring type, or from a type nested in it, passes, and so does a call on
    ///     the instance itself (<c>this</c> or <c>base</c>) from a derived type: an override that
    ///     composes with its base runs inside the invocation the invoker already authorised.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ActionCallAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [TellmaDiagnostics.ActionCall];

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(static start =>
            {
                // --- The runtime is the invoker and the pipeline ---
                if (string.Equals(start.Compilation.AssemblyName, KnownNames.RuntimeAssembly, StringComparison.Ordinal))
                {
                    return;
                }

                start.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
                start.RegisterOperationAction(AnalyzeMethodReference, OperationKind.MethodReference);
            });
        }

        /// <summary>Reports a direct call to an action method.</summary>
        /// <param name="context">The operation context.</param>
        private static void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            Analyze(context, invocation.TargetMethod, invocation.Instance, invocation.Syntax);
        }

        /// <summary>Reports an action method taken as a delegate, which a caller could then invoke directly.</summary>
        /// <param name="context">The operation context.</param>
        private static void AnalyzeMethodReference(OperationAnalysisContext context)
        {
            var reference = (IMethodReferenceOperation)context.Operation;
            if (!reference.IsInsideNameOf())
            {
                Analyze(context, reference.Method, reference.Instance, reference.Syntax);
            }
        }

        /// <summary>Reports a use of an action method from outside its declaring type.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="method">The method used.</param>
        /// <param name="instance">The receiver, or null for a static method.</param>
        /// <param name="syntax">Where.</param>
        private static void Analyze(OperationAnalysisContext context, IMethodSymbol method, IOperation? instance, SyntaxNode syntax)
        {
            // --- Is it an action, and which kind ---
            string? kind = method.HasInheritedAttribute(KnownNames.CrudNamespace, KnownNames.ApiActionAttribute) ? "[ApiAction]"
                : method.HasInheritedAttribute(KnownNames.CrudNamespace, KnownNames.EntityActionAttribute) ? "[EntityAction]"
                : null;
            if (kind is null)
            {
                return;
            }

            // --- The declaring type, and the types nested in it, may call it ---
            INamedTypeSymbol declaring = method.OriginalDefinition.ContainingType;
            IReadOnlyList<INamedTypeSymbol> callers = [.. context.ContainingSymbol.EnclosingTypes()];
            if (callers.Any(caller => SymbolEqualityComparer.Default.Equals(caller.OriginalDefinition, declaring)))
            {
                return;
            }

            // --- A derived type may call it on itself ---
            bool onSelf = instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance };
            if (onSelf && callers.Count > 0 && IsDerivedFrom(callers[0], declaring))
            {
                return;
            }

            string entry = kind == "[ApiAction]" ? "IApiActionInvoker" : "the service's ExecuteActionAsync";
            context.ReportDiagnostic(Diagnostic.Create(
                TellmaDiagnostics.ActionCall,
                syntax.GetLocation(),
                $"{declaring.Name}.{method.Name} is an {kind} method, called here from outside its declaring type. Reach it through {entry}, which evaluates its securable first."));
        }

        /// <summary>Whether a type derives from another type's definition.</summary>
        /// <param name="type">The candidate.</param>
        /// <param name="ancestor">The definition.</param>
        /// <returns>True when it does.</returns>
        private static bool IsDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol ancestor)
        {
            for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, ancestor))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
