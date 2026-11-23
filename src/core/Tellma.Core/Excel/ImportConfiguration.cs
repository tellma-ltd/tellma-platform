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
    ///     The model configuration of <c>core.Imports</c>: the two indexes the "My imports" list and the
    ///     file retention seek.
    /// </summary>
    /// <remarks>
    ///     The class's attributes carry the column widths. The conventions give the table its sequence
    ///     <c>core.sq_Imports</c>, its positive-key check, its <c>datetimeoffset(3)</c> timestamps, the
    ///     mode as non-Unicode text, the file's foreign key to <c>core.Blobs</c> with its filtered unique
    ///     index <c>UX_Imports_FileId</c>, the job's foreign key (set to null when the job is deleted)
    ///     with its filtered unique index <c>UX_Imports_JobId</c>, the audit foreign keys, and its table
    ///     type over every column.
    /// </remarks>
    internal sealed class ImportConfiguration : IEntityTypeConfiguration<Import>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<Import> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- A member's imports, newest first: the self-scoped list ---
            _ = builder.HasIndex(static import => new { import.CreatedById, import.CreatedAt })
                .HasDatabaseName("IX_Imports_CreatedBy")
                .IsDescending(false, true);

            // --- The expired rows the daily file retention deletes ---
            _ = builder.HasIndex(static import => import.ExpiresAt)
                .HasDatabaseName("IX_Imports_ExpiresAt");
        }
    }
}
