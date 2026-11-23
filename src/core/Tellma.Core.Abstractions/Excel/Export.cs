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
    ///     A row of <c>core.Exports</c>: the workbook one background export produced, which the row owns
    ///     until it expires or its creator deletes it. The export's job inserts the row when it has the
    ///     file, so a row always names a finished workbook.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The stack serves reads and deletes, never a save: every write is a save the export's job
    ///         enlists with its own frame. Each member reads and deletes the rows they created; an
    ///         administrator with a stored grant reads them all. Deleting a row releases its file.
    ///     </para>
    ///     <para>
    ///         The serialized request is left out of the default projection and stays selectable.
    ///     </para>
    /// </remarks>
    [Table("Exports", Schema = "core")]
    [TableType]
    [Stack(Operations = StackOperations.Query | StackOperations.Details | StackOperations.Delete | StackOperations.Enlist, DefaultOrderBy = "CreatedAt desc")]
    [DefaultSelect("Id,Resource,Kind,FileName,FileId,RowCount,ExpiresAt,JobId,CreatedAt,CreatedById,ModifiedAt,ModifiedById")]
    public sealed class Export : TopLevelEntity, IJobEntity
    {
        /// <summary>The blob kind of every exported workbook.</summary>
        public const string FileKind = "export-file";

        /// <summary>The width of <see cref="Resource" />, <c>varchar(128)</c>: a securable resource's.</summary>
        public const int ResourceLength = 128;

        /// <summary>The width of <see cref="FileName" />, <c>nvarchar(255)</c>: a blob's file name.</summary>
        public const int FileNameLength = 255;

        /// <inheritdoc />
        /// <remarks>Set by the insert at the end of the export's run.</remarks>
        public int? JobId { get; set; }

        /// <summary>
        ///     The securable resource of the exported stack or child surface, such as <c>gl.Center</c>,
        ///     or <c>core.Report</c> for a report's table, <c>varchar(128)</c>. Write-once.
        /// </summary>
        [WriteOnce]
        [MaxLength(ResourceLength)]
        [Column(TypeName = "varchar(128)")]
        public string Resource { get; set; } = null!;

        /// <summary>The shape of the workbook, <c>varchar(9)</c>. Write-once.</summary>
        [WriteOnce]
        public ExportKind Kind { get; set; }

        /// <summary>The serialized request the export ran, <c>nvarchar(max)</c>, at most 64 KB; null for none. Write-once.</summary>
        [WriteOnce]
        public string? RequestJson { get; set; }

        /// <summary>The workbook's file name, <c>nvarchar(255)</c>.</summary>
        [MaxLength(FileNameLength)]
        public string? FileName { get; set; }

        /// <summary>The workbook's blob, navigation <c>File</c>, which only the row's readers download.</summary>
        [BlobReference(FileKind, BlobPreset.Attachment, ReadAccess = BlobReadAccess.OwnerRead)]
        public int? FileId { get; set; }

        /// <summary>How many data rows the workbook holds.</summary>
        public int? RowCount { get; set; }

        /// <summary>When the row and its file expire, <c>datetimeoffset(3)</c>: the insert's time plus the export retention. Write-once.</summary>
        [WriteOnce]
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
