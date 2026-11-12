// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Data;
using Tellma.Core.Tests.Data.Model;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Data
{
    /// <summary>
    ///     The parent-key index a child takes from the conventions, beside an index a model
    ///     configuration declares: an unfiltered one leading with the parent key stands in for it, a
    ///     filtered one does not.
    /// </summary>
    public sealed class ChildIndexTests : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IModel _model;

        /// <summary>Composes a feature with two parents, each child shaped by its own configuration.</summary>
        public ChildIndexTests()
        {
            _provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Entity<Ledger>()
                    .Entity<Journal>()
                    .Model<LedgerLineConfiguration>()
                    .Model<JournalLineConfiguration>())))
                .BuildServiceProvider(validateScopes: true);

            using TellmaDbContext context = _provider.GetRequiredService<TellmaModelFactory>().CreateContext(DataModel.UnusedConnectionString);
            _model = context.GetService<IDesignTimeModel>().Model;
        }

        [Fact]
        public void An_unfiltered_configured_index_leading_with_the_parent_key_stands_in_for_the_conventions()
        {
            Assert.Equal(["IX_LedgerLines_LedgerId_Ordinal"], Indexes<LedgerLine>());
        }

        [Fact]
        public void A_filtered_configured_index_leading_with_the_parent_key_leaves_the_conventions_in_place()
        {
            Assert.Equal(["IX_JournalLines_JournalId", "IX_JournalLines_JournalId_State"], Indexes<JournalLine>());

            // The convention's index is the full one, carrying the child's key.
            IIndex index = _model.FindEntityType(typeof(JournalLine))!.GetIndexes().Single(static index => index.GetDatabaseName() == "IX_JournalLines_JournalId");
            Assert.Null(index.GetFilter());
            Assert.Equal([nameof(JournalLine.Id)], index.GetIncludeProperties());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _provider.Dispose();
        }

        /// <summary>The names of a type's indexes, in ordinal order.</summary>
        /// <typeparam name="T">The type.</typeparam>
        /// <returns>The names.</returns>
        private List<string> Indexes<T>()
        {
            return [.. _model.FindEntityType(typeof(T))!.GetIndexes().Select(static index => index.GetDatabaseName()!).Order(StringComparer.Ordinal)];
        }

        /// <summary>A parent whose child's configuration declares an unfiltered index.</summary>
        [Table("Ledgers", Schema = "fixture")]
        public class Ledger : TopLevelEntity
        {
            [NotMapped]
            public List<LedgerLine>? Lines { get; set; }
        }

        /// <summary>The child of <see cref="Ledger" />.</summary>
        [Table("LedgerLines", Schema = "fixture")]
        public class LedgerLine : ChildEntity
        {
            [ParentKey]
            public int LedgerId { get; set; }

            public int Ordinal { get; set; }
        }

        /// <summary>A parent whose child's configuration declares a filtered index.</summary>
        [Table("Journals", Schema = "fixture")]
        public class Journal : TopLevelEntity
        {
            [NotMapped]
            public List<JournalLine>? Lines { get; set; }
        }

        /// <summary>The child of <see cref="Journal" />.</summary>
        [Table("JournalLines", Schema = "fixture")]
        public class JournalLine : ChildEntity
        {
            [ParentKey]
            public int JournalId { get; set; }

            [MaxLength(16)]
            public string State { get; set; } = null!;
        }

        /// <summary>Declares an unfiltered index leading with the parent key.</summary>
        public sealed class LedgerLineConfiguration : IEntityTypeConfiguration<LedgerLine>
        {
            /// <inheritdoc />
            public void Configure(EntityTypeBuilder<LedgerLine> builder)
            {
                _ = builder.HasIndex(static line => new { line.LedgerId, line.Ordinal }).HasDatabaseName("IX_LedgerLines_LedgerId_Ordinal");
            }
        }

        /// <summary>Declares a filtered index leading with the parent key.</summary>
        public sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
        {
            /// <inheritdoc />
            public void Configure(EntityTypeBuilder<JournalLine> builder)
            {
                _ = builder.HasIndex(static line => new { line.JournalId, line.State })
                    .HasDatabaseName("IX_JournalLines_JournalId_State")
                    .HasFilter("[State] = 'Draft'");
            }
        }
    }
}
