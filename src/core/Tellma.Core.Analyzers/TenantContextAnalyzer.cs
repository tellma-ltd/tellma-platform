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
    ///     TELLMA0002: an EF query (<c>Set&lt;T&gt;()</c>, <c>Find</c>, a <c>DbSet&lt;T&gt;</c>, the
    ///     database facade's raw SQL) or <c>SaveChanges</c>/<c>SaveChangesAsync</c> over
    ///     <c>TellmaDbContext</c>, outside <c>Tellma.Core</c> and the projects whose assembly name ends
    ///     in <c>.Migrator</c>.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TenantContextAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>The context members that query or write: everything else only builds the model.</summary>
        private static readonly ImmutableHashSet<string> ContextMembers =
            ImmutableHashSet.Create(StringComparer.Ordinal, "Set", "Find", "FindAsync", "SaveChanges", "SaveChangesAsync");

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [TellmaDiagnostics.TenantContext];

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(static start =>
            {
                // --- The runtime and the migrators own the context ---
                string name = start.Compilation.AssemblyName ?? string.Empty;
                if (string.Equals(name, KnownNames.RuntimeAssembly, StringComparison.Ordinal)
                    || name.EndsWith(KnownNames.MigratorSuffix, StringComparison.Ordinal))
                {
                    return;
                }

                start.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
                start.RegisterOperationAction(AnalyzeMethodReference, OperationKind.MethodReference);
                start.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
            });
        }

        /// <summary>Whether a receiver is the tenant context.</summary>
        /// <param name="instance">The receiver, or null for a static call.</param>
        /// <returns>True when it is.</returns>
        private static bool IsTenantContext(IOperation? instance)
        {
            return instance?.Type.IsOrDerivesFrom(KnownNames.RuntimeDataNamespace, KnownNames.TellmaDbContext) == true;
        }

        /// <summary>Whether a value is the database facade of the tenant context (<c>context.Database</c>).</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it is.</returns>
        private static bool IsTenantFacade(IOperation? value)
        {
            return value?.WithoutConversions() is IPropertyReferenceOperation { Property.Name: "Database" } facade
                && IsTenantContext(facade.Instance);
        }

        /// <summary>Reports a query or save called on the context, or raw SQL called on its facade.</summary>
        /// <param name="context">The operation context.</param>
        private static void AnalyzeInvocation(OperationAnalysisContext context)
        {
            var invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol method = invocation.TargetMethod;

            // --- A query or a save on the context ---
            if (IsContextMember(method) && IsTenantContext(invocation.Instance))
            {
                Report(context, invocation.Syntax, $"TellmaDbContext.{method.Name}");
                return;
            }

            // --- Raw SQL through the context's database facade ---
            if (IsFacadeSql(method) && IsTenantFacade(invocation.Arguments.FirstOrDefault()?.Value ?? invocation.Instance))
            {
                Report(context, invocation.Syntax, $"TellmaDbContext.Database.{method.Name}");
            }
        }

        /// <summary>Reports a query or save taken as a delegate.</summary>
        /// <param name="context">The operation context.</param>
        private static void AnalyzeMethodReference(OperationAnalysisContext context)
        {
            var reference = (IMethodReferenceOperation)context.Operation;
            if (!reference.IsInsideNameOf() && IsContextMember(reference.Method) && IsTenantContext(reference.Instance))
            {
                Report(context, reference.Syntax, $"TellmaDbContext.{reference.Method.Name}");
            }
        }

        /// <summary>Reports an entity set read off the context.</summary>
        /// <param name="context">The operation context.</param>
        private static void AnalyzePropertyReference(OperationAnalysisContext context)
        {
            var reference = (IPropertyReferenceOperation)context.Operation;
            if (!reference.IsInsideNameOf()
                && reference.Type.IsOrDerivesFrom(KnownNames.EfNamespace, KnownNames.DbSet)
                && IsTenantContext(reference.Instance))
            {
                Report(context, reference.Syntax, $"The entity set TellmaDbContext.{reference.Property.Name}");
            }
        }

        /// <summary>Whether a method is one of the context's query or save members.</summary>
        /// <param name="method">The method.</param>
        /// <returns>True when it is.</returns>
        private static bool IsContextMember(IMethodSymbol method)
        {
            return ContextMembers.Contains(method.Name) && method.ContainingType.IsOrDerivesFrom(KnownNames.EfNamespace, KnownNames.DbContext);
        }

        /// <summary>Whether a method is one of the database facade's raw-SQL extensions.</summary>
        /// <param name="method">The method, possibly in its reduced extension form.</param>
        /// <returns>True when it is.</returns>
        private static bool IsFacadeSql(IMethodSymbol method)
        {
            IMethodSymbol definition = method.ReducedFrom ?? method;
            return definition.ContainingType.Is(KnownNames.EfNamespace, KnownNames.RelationalDatabaseFacadeExtensions)
                && (definition.Name.StartsWith("SqlQuery", StringComparison.Ordinal) || definition.Name.StartsWith("ExecuteSql", StringComparison.Ordinal));
        }

        /// <summary>Reports one use of the context.</summary>
        /// <param name="context">The operation context.</param>
        /// <param name="syntax">Where.</param>
        /// <param name="what">The member used, for the message.</param>
        private static void Report(OperationAnalysisContext context, SyntaxNode syntax, string what)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                TellmaDiagnostics.TenantContext,
                syntax.GetLocation(),
                $"{what} reads or writes tenant tables through EF, outside Tellma.Core and the migrators. TellmaDbContext only builds the model: read and write through IDataBatch."));
        }
    }
}
