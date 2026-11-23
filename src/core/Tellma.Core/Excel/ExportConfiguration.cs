// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The model configuration of <c>core.Exports</c>: the two indexes the "My exports" list and the
    ///     file retention seek.
    /// </summary>
    /// <remarks>
    ///     The class's attributes carry the column widths. The conventions give the table its sequence
    ///     <c>core.sq_Exports</c>, its positive-key check, its <c>datetimeoffset(3)</c> timestamps, the
    ///     kind as non-Unicode text, the file's foreign key to <c>core.Blobs</c> with its filtered unique
    ///     index <c>UX_Exports_FileId</c>, the job's foreign key (set to null when the job is deleted)
    ///     with its filtered unique index <c>UX_Exports_JobId</c>, the audit foreign keys, and its table
    ///     type over every column.
    /// </remarks>
    internal sealed class ExportConfiguration : IEntityTypeConfiguration<Export>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<Export> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- A member's exports, newest first: the self-scoped list ---
            _ = builder.HasIndex(static export => new { export.CreatedById, export.CreatedAt })
                .HasDatabaseName("IX_Exports_CreatedBy")
                .IsDescending(false, true);

            // --- The expired rows the daily file retention deletes ---
            _ = builder.HasIndex(static export => export.ExpiresAt)
                .HasDatabaseName("IX_Exports_ExpiresAt");
        }
    }
}
