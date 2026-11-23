// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     A row of <c>core.Imports</c>: one background import, which owns the uploaded workbook and, once
    ///     the import ends, records its outcome. The row is inserted with the import's job, in the
    ///     transaction that enqueues it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The stack serves reads and deletes, never a save: every write is a save the start of the
    ///         import or its job enlists. Each member reads and deletes the rows they created; an
    ///         administrator with a stored grant reads them all. A row whose job has not ended refuses
    ///         a delete, so a waiting or running import keeps its file; deleting any other row releases
    ///         the file.
    ///     </para>
    ///     <para>
    ///         The serialized request and the outcome are left out of the default projection and stay
    ///         selectable.
    ///     </para>
    /// </remarks>
    [Table("Imports", Schema = "core")]
    [TableType]
    [Stack(Operations = StackOperations.Query | StackOperations.Details | StackOperations.Delete | StackOperations.Enlist, DefaultOrderBy = "CreatedAt desc")]
    [DefaultSelect("Id,Resource,Mode,FileId,RowCount,ErrorCount,ExpiresAt,JobId,CreatedAt,CreatedById,ModifiedAt,ModifiedById")]
    public sealed class Import : TopLevelEntity, IJobEntity
    {
        /// <summary>The blob kind of every uploaded workbook.</summary>
        public const string FileKind = "import-file";

        /// <summary>The width of <see cref="Resource" />, <c>varchar(128)</c>: a securable resource's.</summary>
        public const int ResourceLength = 128;

        /// <inheritdoc />
        /// <remarks>Set when the import's job is enqueued, in the same transaction as the row.</remarks>
        public int? JobId { get; set; }

        /// <summary>The securable resource of the stack the workbook imports into, such as <c>gl.Center</c>, <c>varchar(128)</c>. Write-once.</summary>
        [WriteOnce]
        [MaxLength(ResourceLength)]
        [Column(TypeName = "varchar(128)")]
        public string Resource { get; set; } = null!;

        /// <summary>What the import does with each parent row, <c>varchar(8)</c>. Write-once.</summary>
        [WriteOnce]
        public ImportMode Mode { get; set; }

        /// <summary>The serialized request the import runs, <c>nvarchar(max)</c>, at most 64 KB; null for none. Write-once.</summary>
        [WriteOnce]
        public string? RequestJson { get; set; }

        /// <summary>The uploaded workbook's blob, navigation <c>File</c>, attached when the row is inserted.</summary>
        [BlobReference(FileKind, BlobPreset.Attachment, ReadAccess = BlobReadAccess.OwnerRead)]
        public int? FileId { get; set; }

        /// <summary>
        ///     The outcome of the ended import, <c>nvarchar(max)</c>: the counts when
        ///     <see cref="ErrorCount" /> is 0, else the reported errors, how many exist and the committed
        ///     row ranges; null until the import ends.
        /// </summary>
        public string? ResultJson { get; set; }

        /// <summary>How many parent rows the workbook holds.</summary>
        public int? RowCount { get; set; }

        /// <summary>How many errors a failed import found; 0 for a success or a stop.</summary>
        public int? ErrorCount { get; set; }

        /// <summary>When the row and its file expire, <c>datetimeoffset(3)</c>: the insert's time plus the import retention. Write-once.</summary>
        [WriteOnce]
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
