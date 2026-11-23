// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Excel;
using Tellma.Core.Tests.Infrastructure;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The matching of an updated owner's child rows by the child's natural key, when the child
    ///     sheet carries no identities to match by: a child whose key the file names keeps its id and
    ///     takes the file's values, a stored child the file does not name is dropped, a key the owner
    ///     has no child of is a new child, and the sheet need not carry every column.
    /// </summary>
    /// <param name="composition">The fixture model with a kit whose pieces are keyed by label, composed once for the class.</param>
    public sealed class ImportChildKeyTests(ChildKeyComposition composition) : IClassFixture<ChildKeyComposition>, IDisposable
    {
        /// <summary>No lookup rows.</summary>
        private static readonly Dictionary<ExcelQuery, IReadOnlyList<IReadOnlyList<object?>>> NoLookups = [];

        /// <summary>The kit over the composition.</summary>
        private readonly ImportKit _kit = new(composition.Services);

        [Fact]
        public async Task An_updated_owners_children_are_matched_by_their_natural_key_when_the_sheet_carries_no_identities()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            byte[] template = await _kit.ExportAsync<CkKit>(scope.Context, rows: 0);
            ImportPlan plan = await _kit.InspectAsync<CkKit>(template, scope.Context);
            ImportPlanSheet kits = plan.Sheets[0];
            ImportPlanSheet pieces = plan.Sheets.Single(static sheet => sheet.Columns.Any(static column => column.Path == "Label"));

            // --- The file names piece "a" again with a new note and adds "c"; its piece identities are left unmapped ---
            byte[] workbook = WorkbookEdits.WithRows(
                template,
                plan,
                (kits.Sheet, [Row(("Id", 1L), ("Name", "Kit one"))]),
                (pieces.Sheet, [Row(("Kit.Id", 1L), ("Label", "A"), ("Note", "changed")), Row(("Kit.Id", 1L), ("Label", "c"))]));
            string pieceId = pieces.Columns.Single(static column => column.Path == "Id").Column;
            ImportRequest update = new() { FileId = 1, Mode = ImportMode.Update, ColumnMappings = [new ImportColumnMapping(pieces.Sheet, pieceId, null)] };
            CkKit stored = new()
            {
                Id = 1,
                Name = "Kit one",
                ModifiedAt = ExcelTestKit.Now.AddDays(-1),
                Pieces = [new() { Id = 10, KitId = 1, Label = "a", Note = "old" }, new() { Id = 11, KitId = 1, Label = "b", Note = "gone" }],
            };

            ExcelImportSession<CkKit> session = await _kit.ParseAsync<CkKit>(workbook, scope.Context, update);
            ExcelChunk<CkKit> chunk = session.Resolve(0, 0, NoLookups, [stored], new Dictionary<long, long>());

            // --- "a" matched ignoring case keeps its id and takes the note; "c" is new; "b" is dropped; nothing is incomplete ---
            Assert.Empty(chunk.Errors);
            List<CkPiece> kept = Assert.Single(chunk.Entities).Pieces!;
            Assert.Equal([(10, "A", "changed"), (0, "c", null)], kept.Select(static piece => (piece.Id, piece.Label, piece.Note)));
            Assert.Equal((1, 1, 1), (chunk.Facts.ChildrenInserted, chunk.Facts.ChildrenUpdated, chunk.Facts.ChildrenDeleted));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        /// <summary>A row by path.</summary>
        /// <param name="values">Each path with its value.</param>
        /// <returns>The row.</returns>
        private static Dictionary<string, object?> Row(params (string Path, object? Value)[] values)
        {
            return values.ToDictionary(static value => value.Path, static value => value.Value, StringComparer.Ordinal);
        }
    }

    /// <summary>The fixture model with a kit whose pieces are keyed by label, and two offered languages.</summary>
    public sealed class ChildKeyComposition : IDisposable
    {
        /// <summary>Composes the model.</summary>
        public ChildKeyComposition()
        {
            Services = FixtureComposition.Compose(
                new ServiceCollection(),
                compose: static tellma => tellma
                    .Languages(["en", "ar"])
                    .AddFeature(new TestFeature<Tag.A>("test.labels"))
                    .AddFeature(new TestFeature<Tag.B>("ck", contribute: static contribution => contribution.Entity<CkKit>())))
                .BuildServiceProvider();
        }

        /// <summary>The realised composition's services.</summary>
        public ServiceProvider Services { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            Services.Dispose();
        }
    }

    /// <summary>A kit: an aggregate root whose pieces are keyed by label within it.</summary>
    [Table("Kits", Schema = "ck")]
    [TableType]
    public class CkKit : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>The pieces.</summary>
        [NotMapped]
        public List<CkPiece>? Pieces { get; set; }
    }

    /// <summary>A piece of a kit, keyed by its label within the kit.</summary>
    [Table("Pieces", Schema = "ck")]
    [TableType]
    public class CkPiece : ChildEntity
    {
        /// <summary>The kit.</summary>
        [ParentKey]
        public int KitId { get; set; }

        /// <summary>The label, unique within the kit.</summary>
        [MaxLength(50)]
        [NaturalKey]
        public string Label { get; set; } = null!;

        /// <summary>A note.</summary>
        [MaxLength(255)]
        public string? Note { get; set; }
    }
}
