// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Excel;
using Tellma.Core.Tests.Excel.Corpus;
using Tellma.Core.Tests.Localization;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The manifest as an import reads it back: an editable export's header block and column
    ///     table, and hand-edited manifests whose rows the reader drops or whose format it does not
    ///     read, without ever trusting a row it cannot use.
    /// </summary>
    /// <param name="composition">The fixture model with its labels, composed once for the class.</param>
    public sealed class ImportManifestTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>, IDisposable
    {
        /// <summary>The kit over the composition.</summary>
        private readonly ImportKit _kit = new(composition.Services);

        [Fact]
        public async Task An_editable_exports_manifest_reads_back_its_source_and_every_column()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            byte[] workbook = await _kit.ExportAsync<Widget>(scope.Context);

            ImportManifest manifest = Assert.IsType<ImportManifest>(Read(workbook));

            Assert.Equal(ExcelManifest.Version, manifest.Version);
            Assert.Equal(("fixture.Widget", "deployment-a", 7), (manifest.Entity, manifest.Deployment, manifest.SourceTenantId));
            Assert.Equal(_kit.Entity<Widget>().SchemaFingerprint, manifest.SchemaFingerprint);
            Assert.Equal(
                ["Contraptions", "Contraptions · Components", "Components · Remarks"],
                manifest.Columns.Select(static column => column.Sheet).Distinct());

            ImportManifestColumn[] names = [.. manifest.Columns.Where(static column => column.Path == "Name")];
            Assert.Equal([("", 4, "en"), ("", 5, "ar")], names.Select(static column => (column.Collection, column.Column, column.Language)));
            Assert.Equal("Parts", manifest.Columns.First(static column => column.Sheet == "Contraptions · Components").Collection);
        }

        [Theory]
        [InlineData("Tellma.Manifest", "2")]
        [InlineData("Tellma.Manifest", "one")]
        [InlineData("Manifest", "1")]
        public void A_manifest_of_another_format_or_version_is_not_read(string key, string version)
        {
            Assert.Null(Read(Manifest([key, version])));
        }

        [Fact]
        public void A_manifest_not_starting_at_row_one_is_not_read()
        {
            SyntheticWorkbook workbook = new(Producer.Excel, RawSheet.OfText(SheetNames.Manifest, [null], [ExcelManifest.VersionKey, "1"]));

            Assert.Null(Read(workbook.Build()));
        }

        [Fact]
        public void Header_rows_beyond_the_cap_and_repeated_keys_are_dropped()
        {
            string?[][] rows = [[ExcelManifest.VersionKey, "1"], [ExcelManifest.EntityKey, "fixture.Widget"], [ExcelManifest.EntityKey, "fixture.Node"], .. Enumerable.Range(0, 100).Select(static i => new string?[] { $"Key{i}", "v" })];

            ImportManifest manifest = Read(Manifest(rows))!;

            Assert.Equal("fixture.Widget", manifest.Entity);
            Assert.Equal(ImportManifest.MaxHeaderRows, manifest.Header.Count);
            Assert.Empty(manifest.Columns);
        }

        [Fact]
        public void Column_rows_that_lack_a_field_misname_a_column_or_language_or_repeat_a_header_are_dropped()
        {
            string?[][] rows =
            [
                [ExcelManifest.VersionKey, "1"],
                [ExcelManifest.EntityKey, "fixture.Widget"],
                [],
                ["Sheet", "Collection", "Column", "Header", "Path", "Language"],
                ["Data", null, "A", "Code", "Code", null],
                ["Data", null, "B", "Title ", "Name", "en"],
                ["Data", null, "C", "Bad language", "Name", "e_n"],
                ["Data", null, "D1", "Bad letter", "Code", null],
                ["Data", null, "E", "No path", null, null],
                ["Data", null, "F", "   ", "Code", null],
                ["DATA", null, "G", "Code", "Price", null],
                ["Data", "Parts", "H", "Ordinal", "Ordinal", null],
            ];

            ImportManifest manifest = Read(Manifest(rows))!;

            Assert.Equal(
                [("Code", 0, "Code", null, ""), ("Title ", 1, "Name", "en", ""), ("Ordinal", 7, "Ordinal", null, "Parts")],
                manifest.Columns.Select(static column => (column.Header, column.Column, column.Path, column.Language, column.Collection)));
        }

        [Fact]
        public void Header_and_column_rows_holding_a_field_longer_than_the_cap_are_dropped()
        {
            string overlong = new('x', ImportManifest.MaxFieldLength + 1);
            string?[][] rows =
            [
                [ExcelManifest.VersionKey, "1"],
                [ExcelManifest.EntityKey, "fixture.Widget"],
                [ExcelManifest.DeploymentKey, overlong],
                [],
                ["Sheet", "Collection", "Column", "Header", "Path", "Language"],
                ["Data", null, "A", "Code", "Code", null],
                ["Data", null, "B", overlong, "Name", "en"],
                ["Data", null, "C", "Price", "Price", null],
            ];

            ImportManifest manifest = Read(Manifest(rows))!;

            Assert.Equal(("fixture.Widget", null), (manifest.Entity, manifest.Deployment));
            Assert.Equal(["Code", "Price"], manifest.Columns.Select(static column => column.Header));
        }

        [Fact]
        public void A_column_table_without_its_required_fields_reads_as_no_columns()
        {
            ImportManifest manifest = Read(Manifest([ExcelManifest.VersionKey, "1"], [], ["Sheet", "Header", "Path"], ["Data", "Code", "Code"]))!;

            Assert.Empty(manifest.Columns);
        }

        [Theory]
        [InlineData("en", true)]
        [InlineData("zh-Hant-TW", true)]
        [InlineData("e", false)]
        [InlineData("en_US", false)]
        [InlineData("en-", false)]
        [InlineData("", false)]
        public void A_language_is_a_language_tag(string code, bool expected)
        {
            Assert.Equal(expected, ImportManifest.IsLanguageCode(code));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        /// <summary>A workbook of a data sheet and a manifest sheet of text rows.</summary>
        /// <param name="rows">The manifest's rows.</param>
        /// <returns>The workbook's bytes.</returns>
        private static byte[] Manifest(params string?[][] rows)
        {
            return new SyntheticWorkbook(Producer.Excel, RawSheet.OfText("Data", ["Code"]), RawSheet.OfText(SheetNames.Manifest, rows) with { State = "veryHidden" }).Build();
        }

        /// <summary>Reads a workbook's manifest sheet.</summary>
        /// <param name="workbook">The workbook's bytes.</param>
        /// <returns>The manifest; null when the sheet is not one this codec reads.</returns>
        private ImportManifest? Read(byte[] workbook)
        {
            using var reader = WorkbookReader.Open(new MemoryStream(workbook, writable: false), new WorkbookLimits(1 << 24, 1 << 26, 1 << 24, 100_000), _kit.Scratch);
            using SheetReader sheet = reader.OpenSheet(Assert.Single(reader.Sheets, static sheet => sheet.Name == SheetNames.Manifest));
            return ImportManifest.Read(sheet);
        }
    }
}
