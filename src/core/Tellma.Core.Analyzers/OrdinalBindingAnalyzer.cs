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
    ///     TELLMA0001: a <c>SqlDataRecord</c> constructed, or its <c>Set*</c> methods or
    ///     <c>GetOrdinal</c> called, outside a type marked <c>[TableTypeBinder]</c>, which is how a
    ///     hard-coded column ordinal reaches a table-valued parameter.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class OrdinalBindingAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [TellmaDiagnostics.OrdinalBinding];

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeCreation, OperationKind.ObjectCreation);
            context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
            context.RegisterOperationAction(AnalyzeMethodReference, OperationKind.MethodReference);
        }

        /// <summary>Whether a type is a table-valued parameter record, from either SqlClient.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True when it is or derives from one.</returns>
        private static bool IsRecord(ITypeSymbol? type)
        {
            return type.IsOrDerivesFrom(KnownNames.SqlClientServerNamespace, KnownNames.SqlDataRecord)
                || type.IsOrDerivesFrom(KnownNames.LegacySqlServerNamespace, KnownNames.SqlDataRecord);
        }

        /// <summary>Whether a record member binds by ordinal: a <c>Set*</c> method or <c>GetOrdinal</c>.</summary>
        /// <param name="method">The member.</param>
        /// <returns>True when it does.</returns>
        private static bool IsOrdinalMember(IMethodSymbol method)
        {
            return IsRecord(method.ContainingType)
                && (method.Name.StartsWith("Set", StringComparison.Ordinal) || string.Equals(method.Name, "GetOrdinal", StringComparison.Ordinal));
        }

        /// <summary>Whether the code sits in a type marked as a binder, or in a type nested in one.</summary>
        /// <param name="symbol">The analysis context's containing symbol.</param>
        /// <returns>True when it does.</returns>
        private static bool InBinder(ISymbol symbol)
        {
            return symbol.EnclosingTypes().Any(static type => type.HasAttribute(KnownNames.EntitiesNamespace, KnownNames.TableTypeBinderAttribute));
        }

        /// <summary>Reports a record constructed outside a binder.</summary>
        /// <param name="context">The operation context.</param>
        private static void AnalyzeCreation(OperationAnalysisContext context)
        {
            var creation = (IObjectCreationOperation)context.Operation;
            if (IsRecord(creation.Type) && !InBinder(context.ContainingSymbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    TellmaDiagnostics.OrdinalBinding,
                    creation.Syntax.GetLocation(),
                    "A SqlDataRecord is constructed outside a type marked [TableTypeBinder]. Bind the rows through IDataBatch.Tvp, or a list of table-type rows in an IDataBatch.Sql hole, so columns are matched by name from the model."));
            }
        }

        /// <summary>Reports an ordinal member called outside a binder.</summary>
        /// <param name="context">The operation context.</param>
        private static void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            Report(context, invocation.TargetMethod, invocation.Syntax);
        }

        /// <summary>Reports an ordinal member taken as a delegate outside a binder.</summary>
        /// <param name="context">The operation context.</param>
        private static void AnalyzeMethodReference(OperationAnalysisContext context)
        {
            var reference = (IMethodReferenceOperation)context.Operation;
            if (!reference.IsInsideNameOf())
            {
                Report(context, reference.Method, reference.Syntax);
            }
        }

        /// <summary>Reports a use of an ordinal member outside a binder.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="method">The member used.</param>
        /// <param name="syntax">Where.</param>
        private static void Report(OperationAnalysisContext context, IMethodSymbol method, SyntaxNode syntax)
        {
            if (IsOrdinalMember(method) && !InBinder(context.ContainingSymbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    TellmaDiagnostics.OrdinalBinding,
                    syntax.GetLocation(),
                    $"SqlDataRecord.{method.Name} binds a column by ordinal outside a type marked [TableTypeBinder]. Bind the rows through IDataBatch.Tvp, or a list of table-type rows in an IDataBatch.Sql hole, so columns are matched by name from the model."));
            }
        }
    }
}
