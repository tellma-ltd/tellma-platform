// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The model of <c>core.Blobs</c>: the column facets, the storage name and content hash the class
    ///     does not carry, the uploader's reference, and the indexes of the sweep, the storage name and
    ///     the staging quota. The conventions give it its sequence and its key check.
    /// </summary>
    internal sealed class BlobConfiguration : IEntityTypeConfiguration<Blob>
    {
        /// <summary>The column holding the full object name of the stored file; never in a query or a response.</summary>
        public const string StorageKey = "StorageKey";

        /// <summary>The column holding the SHA-256 hash of the stored file; never in a query or a response.</summary>
        public const string Sha256 = "Sha256";

        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<Blob> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- Column facets ---
            _ = builder.Property(static blob => blob.Kind).HasMaxLength(40);
            _ = builder.Property(static blob => blob.ContentType).HasMaxLength(100);
            _ = builder.Property(static blob => blob.FileName).HasMaxLength(255);
            _ = builder.Property(static blob => blob.Variants).HasMaxLength(100);

            // --- The columns the class does not carry ---
            _ = builder.Property<string>(StorageKey).HasMaxLength(120).IsRequired();
            _ = builder.Property<byte[]>(Sha256).HasColumnType("binary(32)").HasMaxLength(32).IsFixedLength().IsRequired();

            // --- The uploader, whichever leaf the distribution registers for the user ---
            IReadOnlyEntityType user = ConventionModel.Leaf(builder.Metadata.Model, typeof(User), "The uploader of core.Blobs");
            _ = builder.HasOne(user.ClrType)
                .WithMany()
                .HasForeignKey(nameof(Blob.CreatedById))
                .OnDelete(DeleteBehavior.NoAction);

            // --- Indexes: the sweep's claim, the storage name, and the staging quota ---
            _ = builder.HasIndex(static blob => blob.ExpiresAt)
                .HasDatabaseName("IX_Blobs_Expiry")
                .HasFilter("[ExpiresAt] IS NOT NULL")
                .IncludeProperties(nameof(Blob.Kind), StorageKey, nameof(Blob.Variants), nameof(Blob.State));
            _ = builder.HasIndex(StorageKey)
                .HasDatabaseName("UX_Blobs_StorageKey")
                .IsUnique();
            _ = builder.HasIndex(static blob => blob.CreatedById)
                .HasDatabaseName("IX_Blobs_StagedBy")
                .HasFilter($"[State] = '{nameof(BlobState.Staged)}'")
                .IncludeProperties(static blob => new { blob.Size, blob.ExpiresAt });
        }
    }
}
