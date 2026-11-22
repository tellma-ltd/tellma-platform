// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Excel;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     How an export request names its rows and columns: by ids alone or by the grid's clauses,
    ///     an empty id list only for the template, no named filters on a child surface, and one to a
    ///     hundred columns each with an expression and a header of at most 255 characters.
    /// </summary>
    public sealed class ExportRequestTests
    {
        [Fact]
        public void The_clauses_map_to_one_query_source_carrying_every_filter_and_named_filter()
        {
            Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal) { ["min"] = JsonSerializer.SerializeToElement(1) };
            ExportRequest request = new()
            {
                Filters = ["Code != 'A'", "Price > @min"],
                NamedFilters = ["active", "on-gizmo"],
                Search = "board",
                OrderBy = "Code desc",
                Arguments = arguments,
                IncludeInactive = true,
            };

            ExportSource.ByQuery source = Assert.IsType<ExportSource.ByQuery>(ExportSources.Of(request, template: false, child: false));

            Assert.Equal(["Code != 'A'", "Price > @min"], source.Filters!);
            Assert.Equal(["active", "on-gizmo"], source.NamedFilters!);
            Assert.Equal(("board", "Code desc", true), (source.Search, source.OrderBy, source.IncludeInactive));
            Assert.Same(arguments, source.Arguments);
        }

        [Fact]
        public void A_request_without_ids_or_clauses_is_a_query_of_every_row()
        {
            ExportSource.ByQuery source = Assert.IsType<ExportSource.ByQuery>(ExportSources.Of(new ExportRequest(), template: false, child: false));

            Assert.Equal(new ExportSource.ByQuery(null, null, null, null, null, false), source);
        }

        [Fact]
        public void Ids_map_to_an_ids_source_with_the_ordering_beside_them()
        {
            ExportSource.ByIds source = Assert.IsType<ExportSource.ByIds>(ExportSources.Of(new ExportRequest { Ids = [3, 1], OrderBy = "Code" }, template: false, child: false));

            Assert.Equal([3L, 1L], source.Ids);
            Assert.Equal("Code", source.OrderBy);
        }

        [Theory]
        [InlineData("filters")]
        [InlineData("namedFilters")]
        [InlineData("search")]
        [InlineData("arguments")]
        [InlineData("includeInactive")]
        public void Ids_beside_a_clause_are_refused_naming_the_clause(string clause)
        {
            ExportRequest request = new() { Ids = [1] };
            switch (clause)
            {
                case "filters":
                    request.Filters = ["Code != ''"];
                    break;
                case "namedFilters":
                    request.NamedFilters = ["active"];
                    break;
                case "search":
                    request.Search = "board";
                    break;
                case "arguments":
                    request.Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["a"] = JsonSerializer.SerializeToElement(1) };
                    break;
                default:
                    request.IncludeInactive = true;
                    break;
            }

            BadRequestException fault = Assert.Throws<BadRequestException>(() => ExportSources.Of(request, template: false, child: false));

            Assert.Equal(clause, fault.Arguments!["path"]);
        }

        [Fact]
        public void Empty_clauses_beside_ids_are_no_clause()
        {
            ExportRequest request = new() { Ids = [1], Filters = [], NamedFilters = [], Search = " ", Arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal) };

            Assert.IsType<ExportSource.ByIds>(ExportSources.Of(request, template: false, child: false));
        }

        [Fact]
        public void An_empty_id_list_is_the_template_of_an_editable_export_and_refused_elsewhere()
        {
            ExportSource.ByIds template = Assert.IsType<ExportSource.ByIds>(ExportSources.Of(new ExportForImportRequest { Ids = [] }, template: true, child: false));
            BadRequestException fault = Assert.Throws<BadRequestException>(() => ExportSources.Of(new ExportRequest { Ids = [] }, template: false, child: false));

            Assert.Empty(template.Ids);
            Assert.Equal("ids", fault.Arguments!["path"]);
        }

        [Fact]
        public void A_child_surface_refuses_named_filters_by_query_and_by_ids()
        {
            BadRequestException byQuery = Assert.Throws<BadRequestException>(() => ExportSources.Of(new ExportRequest { NamedFilters = ["active"] }, template: false, child: true));
            BadRequestException byIds = Assert.Throws<BadRequestException>(() => ExportSources.Of(new ExportRequest { Ids = [1], NamedFilters = ["active"] }, template: false, child: true));

            Assert.Equal("namedFilters", byQuery.Arguments!["path"]);
            Assert.Equal("namedFilters", byIds.Arguments!["path"]);
            Assert.IsType<ExportSource.ByQuery>(ExportSources.Of(new ExportRequest { Filters = ["Code != ''"] }, template: false, child: true));
        }

        [Fact]
        public void A_display_request_lists_at_least_one_column()
        {
            BadRequestException fault = Assert.Throws<BadRequestException>(() => ExportSources.CheckColumns(new ExportRequest { Columns = [] }));

            Assert.Equal("columns", fault.Arguments!["path"]);
        }

        [Fact]
        public void A_display_request_lists_at_most_a_hundred_columns()
        {
            ExportSources.CheckColumns(new ExportRequest { Columns = Columns(ExportRequest.MaxColumns) });

            BadRequestException fault = Assert.Throws<BadRequestException>(() => ExportSources.CheckColumns(new ExportRequest { Columns = Columns(ExportRequest.MaxColumns + 1) }));

            Assert.Equal(("columns", 101, 100), (fault.Arguments!["path"], fault.Arguments["actual"], fault.Arguments["maximum"]));
        }

        [Fact]
        public void A_header_has_at_most_255_characters()
        {
            ExportSources.CheckColumns(new ExportRequest { Columns = [new ExportColumn("Code", new string('h', ExportColumn.MaxHeaderLength))] });

            BadRequestException fault = Assert.Throws<BadRequestException>(() => ExportSources.CheckColumns(new ExportRequest
            {
                Columns = [new ExportColumn("Code", null), new ExportColumn("Price", new string('h', ExportColumn.MaxHeaderLength + 1))],
            }));

            Assert.Equal(("columns[1].header", 256, 255), (fault.Arguments!["path"], fault.Arguments["actual"], fault.Arguments["maximum"]));
        }

        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        public void A_column_without_an_expression_is_refused(string expression)
        {
            BadRequestException fault = Assert.Throws<BadRequestException>(() => ExportSources.CheckColumns(new ExportRequest
            {
                Columns = [new ExportColumn("Code", null), new ExportColumn(expression, "Blank")],
            }));

            Assert.Equal("columns[1]", fault.Arguments!["path"]);
        }

        /// <summary>A number of plain columns.</summary>
        /// <param name="count">How many.</param>
        /// <returns>The columns.</returns>
        private static ExportColumn[] Columns(int count)
        {
            return [.. Enumerable.Range(0, count).Select(static _ => new ExportColumn("Code", Header: null))];
        }
    }
}
