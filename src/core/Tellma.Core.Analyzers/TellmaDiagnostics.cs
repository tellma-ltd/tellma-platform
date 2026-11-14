// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.CodeAnalysis;

namespace Tellma.Core.Analyzers
{
    /// <summary>
    ///     The platform's six diagnostics. Each is an error by default and carries one message
    ///     argument, the condition-specific sentence that says what was refused and what to write
    ///     instead.
    /// </summary>
    internal static class TellmaDiagnostics
    {
        /// <summary>The category of the data-access rules.</summary>
        private const string DataAccess = "Tellma.DataAccess";

        /// <summary>The category of the service-pipeline rules.</summary>
        private const string Pipeline = "Tellma.Pipeline";

        /// <summary>TELLMA0001: a table-valued parameter record built or bound by ordinal outside a binder type.</summary>
        public static readonly DiagnosticDescriptor OrdinalBinding = new(
            id: "TELLMA0001",
            title: "Table-valued parameter rows are bound from the model, never by ordinal",
            messageFormat: "{0}",
            category: DataAccess,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "A table type's column order is part of its contract, so rows bind through the model's table-type definitions by column name. Only a type marked [TableTypeBinder] constructs a SqlDataRecord or calls its Set methods or GetOrdinal.");

        /// <summary>TELLMA0002: an EF query or save over the tenant context outside the runtime and the migrators.</summary>
        public static readonly DiagnosticDescriptor TenantContext = new(
            id: "TELLMA0002",
            title: "The tenant context is platform-internal",
            messageFormat: "{0}",
            category: DataAccess,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "TellmaDbContext builds the tenant model and serves the migrator. Tables are read and written only through IDataBatch, so an EF query, raw EF SQL or SaveChanges over the context is refused outside Tellma.Core and the projects whose assembly name ends in .Migrator.");

        /// <summary>TELLMA0003: raw statement text the batch cannot run safely.</summary>
        public static readonly DiagnosticDescriptor RawSql = new(
            id: "TELLMA0003",
            title: "Raw SQL declares its writes and stays inside the batch's rules",
            messageFormat: "{0}",
            category: DataAccess,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "IDataBatch.Sql text is parsed with ScriptDom. It must be an interpolated string whose holes are values, identifiers or table-valued parameters; it declares every table it writes; it runs no MERGE, no dynamic SQL and no procedure other than sp_sequence_get_range and sp_getapplock; it leaves the transaction and the isolation level to the executor; it declares and assigns no reserved name; and it raises errors only with THROW inside the platform and distribution bands.");

        /// <summary>TELLMA0004: a direct call to an action method.</summary>
        public static readonly DiagnosticDescriptor ActionCall = new(
            id: "TELLMA0004",
            title: "Action methods are reached only through the invoker and the pipeline",
            messageFormat: "{0}",
            category: Pipeline,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "A method marked [ApiAction] runs through IApiActionInvoker and one marked [EntityAction] through ExecuteActionAsync, which evaluate its securable first. A call from outside the declaring type and Tellma.Core would skip that check.");

        /// <summary>TELLMA0005: a nested pipeline run from a pipeline participant.</summary>
        public static readonly DiagnosticDescriptor NestedPipeline = new(
            id: "TELLMA0005",
            title: "A pipeline participant never runs a nested pipeline",
            messageFormat: "{0}",
            category: Pipeline,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "A hook override, an [EntityAction] method and every member of an IEntityValidator<> or IPersistEffect<> run inside a frame the pipeline opened. A front-door write of any entity service from there would open a second pipeline and a second transaction; a write into another stack is enlisted through IEnlists<T> instead.");

        /// <summary>TELLMA0006: a write into a stack-owned table by a type that is not its owner.</summary>
        public static readonly DiagnosticDescriptor OwnedTableWrite = new(
            id: "TELLMA0006",
            title: "A stack-owned table is written by its owner or through an enlisted write",
            messageFormat: "{0}",
            category: DataAccess,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "The table of a stack's entity, and of its child collections, is written only by the stack's own service, validators, effects and companions, closed over that entity. Any other type outside Tellma.Core enlists the write with its host, so the owning stack's rules run.");
    }
}
