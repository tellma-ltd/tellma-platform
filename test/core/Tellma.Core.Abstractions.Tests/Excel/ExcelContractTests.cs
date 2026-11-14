// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Abstractions.Tests.Excel
{
    /// <summary>
    ///     The contracts between a stack and the workbook features: what an export reads, the
    ///     queries and pages it reads them through, and the failure an import reports.
    /// </summary>
    public class ExcelContractTests
    {
        /// <summary>No arguments.</summary>
        private static readonly Dictionary<string, object?> None = [];

        [Fact]
        public void An_export_reads_listed_rows_a_query_or_everything()
        {
            ExportSource[] sources =
            [
                new ExportSource.ByIds([3, 4], "Code"),
                new ExportSource.ByQuery(["IsActive = true"], null, "east", null, null, IncludeInactive: false),
                new ExportSource.All(),
            ];

            // A switch over the three kinds is exhaustive: no other assembly can add a fourth.
            Assert.Equal(["ids", "query", "all"], sources.Select(static source => source switch
            {
                ExportSource.ByIds => "ids",
                ExportSource.ByQuery => "query",
                ExportSource.All => "all",
                _ => "other",
            }));
            _ = Assert.Throws<ArgumentNullException>(() => new ExportSource.ByIds(null!, null));
        }

        [Fact]
        public void An_export_query_names_its_root_and_what_it_selects()
        {
            ExcelQuery query = new("Widget", ["Code", "Name"], "Code", null, null, Take: 100, CountCap: null, Dependents: null);

            Assert.Equal("Widget", query.RootEntity);
            Assert.Equal(["Code", "Name"], query.SelectPaths);
            _ = Assert.Throws<ArgumentException>(() => new ExcelQuery(" ", [], null, null, null, null, null, null));
            _ = Assert.Throws<ArgumentNullException>(() => new ExcelQuery("Widget", null!, null, null, null, null, null, null));
            _ = Assert.Throws<ArgumentException>(() => query with { RootEntity = "" });
        }

        [Fact]
        public void An_export_page_holds_its_rows_and_its_dependents_pages()
        {
            ExcelPage page = new([["W-1", 4m]], [], Count: 1);

            Assert.Equal("W-1", page.Rows[0][0]);
            Assert.Empty(page.Dependents);
            _ = Assert.Throws<ArgumentNullException>(() => new ExcelPage(null!, [], null));
            _ = Assert.Throws<ArgumentNullException>(() => new ExcelPage([], null!, null));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(5, 4)]
        public void A_row_range_starts_at_row_one_or_later_and_never_ends_before_it_starts(int from, int to)
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new RowRange(from, to));
        }

        [Fact]
        public void A_single_row_is_a_range()
        {
            Assert.Equal(new RowRange(2, 2), new RowRange(2, 2));
        }

        [Fact]
        public void An_import_failure_is_a_validation_problem_that_lists_its_cells()
        {
            ImportError cell = new("Widgets", 3, "B", "Name", "Name", "Required", None);
            ImportError header = new(null, null, null, null, null, "Import.LanguageNotConfigured", None);

            ImportException failure = new([cell, header], totalErrors: 7, committedRowRanges: [new RowRange(2, 2)]);

            Assert.Equal("validation", failure.Code);
            Assert.Equal(7, failure.TotalErrors);
            Assert.Equal([cell, header], failure.Errors);
            Assert.Equal([new RowRange(2, 2)], failure.CommittedRowRanges);
            Assert.Equal("The import failed with 7 error(s): Required at Widgets!B3, Import.LanguageNotConfigured.", failure.Message);
            Assert.IsAssignableFrom<CodedError>(cell);
            Assert.IsAssignableFrom<TellmaException>(failure);
        }

        [Fact]
        public void An_import_failure_reports_at_least_one_error_and_counts_at_least_those_it_reports()
        {
            ImportError error = new(null, null, null, null, null, "Required", None);

            _ = Assert.Throws<ArgumentException>(() => new ImportException([], 0, []));
            _ = Assert.Throws<ArgumentNullException>(() => new ImportException(null!, 0, []));
            _ = Assert.Throws<ArgumentException>(() => new ImportException([null!], 1, []));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new ImportException([error, error], 1, []));
            _ = Assert.Throws<ArgumentException>(() => new ImportException([error], 1, [null!]));
        }
    }
}
