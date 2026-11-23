// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Reports;

namespace Tellma.Core.Reports
{
    // Spec 0033 §2.1's table: the conventions give core.Reports its sequence, its positive-key check,
    // the natural key's filtered unique index, the root's index, the owner's foreign key and the
    // kind's text; this adds what the owner's index covers, so the Mine tab and the orphan search seek
    // it without a lookup.
    /// <summary>The model configuration of <c>core.Reports</c>: the owner's index covers the active flag.</summary>
    internal sealed class ReportConfiguration : IEntityTypeConfiguration<Report>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<Report> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            _ = builder.HasIndex(static report => report.OwnerId).IncludeProperties(static report => report.IsActive);
        }
    }
}
