// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Tests.Localization;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Excel.Corpus
{
    /// <summary>
    ///     The import plan of the same hand-made workbook as each producer saves it: every producer's
    ///     package maps to the same sheets, columns and row counts, whether or not it declares a
    ///     dimension, and whatever elements of other namespaces it carries.
    /// </summary>
    /// <param name="composition">The fixture model with its labels, composed once for the class.</param>
    public sealed class CorpusTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>, IDisposable
    {
        /// <summary>The kit over the composition.</summary>
        private readonly ImportKit _kit = new(composition.Services);

        [Theory]
        [InlineData(nameof(Producer.Excel))]
        [InlineData(nameof(Producer.LibreOffice))]
        [InlineData(nameof(Producer.GoogleSheets))]
        [InlineData(nameof(Producer.Strict))]
        public async Task Each_producers_save_of_a_hand_made_workbook_maps_alike(string producer)
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            SyntheticWorkbook workbook = new(
                Enum.Parse<Producer>(producer),
                new RawSheet("Contraptions",
                [
                    [RawCell.Text("Code"), RawCell.InlineText("Title"), RawCell.Text("Price"), RawCell.Text("Due on")],
                    [RawCell.Text("W-1"), RawCell.Text("One"), RawCell.Number(1.5), RawCell.Number(46_000)],
                    [RawCell.Text("W-2"), RawCell.InlineText("Two"), RawCell.Number(2), null],
                    [],
                    [RawCell.Text("W-3"), RawCell.Text("Three"), RawCell.Uncalculated("B2*2"), RawCell.Error("#N/A")],
                ]),
                RawSheet.OfText("Parts", ["Widget.Id", "Ordinal"], ["1", "1"]));

            ImportPlan plan = await _kit.InspectAsync<Widget>(workbook.Build(), scope.Context);

            Assert.Equal([ImportSheetStatus.Parent, ImportSheetStatus.Child], plan.Sheets.Select(static sheet => sheet.Status));
            Assert.Equal(["Code", "Name", "Price", "DueOn"], plan.Sheets[0].Columns.Select(static column => column.Path));
            Assert.All(plan.Sheets.SelectMany(static sheet => sheet.Columns), static column => Assert.Equal(ImportColumnStatus.Mapped, column.Status));

            // A dimension counts every row to its end; a producer without one has its non-blank rows counted.
            long expected = workbook.Producer == Producer.GoogleSheets ? 3 : 4;
            Assert.Equal(expected, plan.Sheets[0].Rows);
            Assert.Equal(1, plan.Sheets[1].Rows);
            Assert.Equal(["Code"], plan.RowKeyCandidates);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }
    }
}
