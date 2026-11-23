// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Sharing;

namespace Tellma.Core.Abstractions.Reports
{
    /// <summary>
    ///     A stored report: a definition document over one root entity, of one kind, owned by the user
    ///     who created it and shared through its grants beside it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A report with no owner is a standard report, seeded by a feature: read-only outside a
    ///         system scope, though it can be shared and published. An inactive report still runs; the
    ///         reports page hides it unless a query includes inactive rows.
    ///     </para>
    ///     <para>
    ///         The stack serves no delete by query: reports are deleted by their ids, so every deletion
    ///         meets the rules that keep standard reports and the reports a dashboard shows.
    ///     </para>
    /// </remarks>
    [Table("Reports", Schema = "core")]
    [TableType]
    [Temporal]
    [TableIndex(nameof(Root))]
    [ApiResource(Segment = "reports", Description = "Stored reports")]
    [Stack(Operations = StackOperations.Query | StackOperations.Details | StackOperations.Save | StackOperations.Delete | StackOperations.Enlist)]
    [DisplayNameSource(nameof(Title))]
    public class Report : TopLevelEntity, IActivatable, IShareable
    {
        /// <summary>The natural key seeds find the report by, <c>nvarchar(50)</c>, unique when present; a feature's prefix is reserved to its seeds.</summary>
        [MaxLength(50)]
        [NaturalKey]
        public string? Code { get; set; }

        /// <summary>The title in the tenant's primary language, <c>nvarchar(255)</c>; a save that leaves it empty takes the default title.</summary>
        [MaxLength(255)]
        [Multilingual]
        [Searchable]
        [Defaulted]
        public string Title { get; set; } = null!;

        /// <summary>The title in the tenant's secondary language.</summary>
        public string? Title2 { get; set; }

        /// <summary>The title in the tenant's tertiary language.</summary>
        public string? Title3 { get; set; }

        /// <summary>The description in the tenant's primary language, <c>nvarchar(1024)</c>.</summary>
        [MaxLength(1024)]
        [Multilingual]
        public string? Description { get; set; }

        /// <summary>The description in the tenant's secondary language.</summary>
        public string? Description2 { get; set; }

        /// <summary>The description in the tenant's tertiary language.</summary>
        public string? Description3 { get; set; }

        /// <summary>The entity the report reads, by its name, such as <c>acme.SalesInvoiceLine</c>; <c>varchar(128)</c>.</summary>
        [MaxLength(128)]
        [Column(TypeName = "varchar(128)")]
        public string Root { get; set; } = null!;

        /// <summary>The report's kind, which names the one block its document holds.</summary>
        public ReportKind Kind { get; set; }

        /// <summary>The definition document, camelCase JSON read into <see cref="ReportDocument" />.</summary>
        [JsonColumn]
        public string DefinitionJson { get; set; } = null!;

        /// <summary>The query language version the document's expressions are read under; derived by the service.</summary>
        [Derived]
        public int QueryexVersion { get; set; }

        /// <summary>The user who owns the report; stamped by the service on insert, kept by every update, changed only by a transfer; null for a standard report.</summary>
        [ServerOwned]
        [References(typeof(User))]
        public int? OwnerId { get; set; }

        /// <summary>The report a "save as" copied, set on insert only; lineage, with no foreign key, since the source may be deleted later.</summary>
        [WriteOnce]
        public int? BasedOnId { get; set; }

        /// <summary>Whether the report shows on the reports page by default; server-owned, changed by the activate and deactivate actions.</summary>
        [ServerOwned]
        public bool IsActive { get; set; } = true;
    }
}
