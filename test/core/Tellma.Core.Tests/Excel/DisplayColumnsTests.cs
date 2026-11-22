// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Excel;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Localization;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     A display export's columns: a navigation read through its target's display-name columns
    ///     and written as the display name in the reader's content slot, a multilingual group written
    ///     as the content slot's twin with the first slot behind it, any other path or expression
    ///     typed by the engine and written as itself, every select term read once, and the headers
    ///     derived from the labels when the request gives none.
    /// </summary>
    /// <param name="composition">The fixture model with its labels, composed once for the class.</param>
    public sealed class DisplayColumnsTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>
    {
        /// <summary>The engine every column that is neither a navigation nor a group is typed by.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>The display-name renderer.</summary>
        private IDisplayNames DisplayNames => composition.Services.GetRequiredService<IDisplayNames>();

        [Fact]
        public void A_navigation_reads_its_targets_display_name_columns_and_writes_the_name_in_the_readers_slot()
        {
            using ExportScope scope = new(composition.Services, "ar", "en", "ar");

            DisplayColumns columns = Expand<Widget>(scope, "CreatedBy");

            Assert.Equal(DisplayNames.Columns("core.User", MultilingualShape.PrimaryAndSecondary, "CreatedBy"), columns.SelectPaths);
            ExcelColumnSpec spec = Assert.Single(columns.Specs);
            Assert.Equal((QueryexType.QxString, ExcelColumnRole.Reference, "core.User"), (spec.Type, spec.Role, spec.ReferenceEntity));
            Assert.Equal("أحمد", Fold(columns, new() { ["CreatedBy.Id"] = 5, ["CreatedBy.Name"] = "Ahmad", ["CreatedBy.Name2"] = "أحمد" })[0]);
        }

        [Fact]
        public void A_navigation_falls_back_to_the_first_slot_and_writes_nothing_for_no_row()
        {
            using ExportScope scope = new(composition.Services, "ar", "en", "ar");

            DisplayColumns columns = Expand<Widget>(scope, "CreatedBy");

            Assert.Equal("Ahmad", Fold(columns, new() { ["CreatedBy.Id"] = 5, ["CreatedBy.Name"] = "Ahmad" })[0]);
            Assert.Null(Fold(columns, new() { ["CreatedBy.Name"] = "Ahmad" })[0]);
        }

        [Fact]
        public void A_reader_whose_language_the_tenant_lacks_reads_the_first_slot()
        {
            using ExportScope scope = new(composition.Services, "am", "en", "ar");

            Assert.Equal(1, DisplayColumns.ContentSlot(scope.Context));
            Assert.Equal("Ahmad", Fold(Expand<Widget>(scope, "CreatedBy"), new() { ["CreatedBy.Id"] = 5, ["CreatedBy.Name"] = "Ahmad", ["CreatedBy.Name2"] = "أحمد" })[0]);
        }

        [Fact]
        public void A_group_anchor_writes_the_content_slots_twin_else_the_first_slot()
        {
            using ExportScope scope = new(composition.Services, "ar", "en", "ar");

            DisplayColumns columns = Expand<Widget>(scope, "Name");

            Assert.Equal(["Name2", "Name"], columns.SelectPaths);
            Assert.Equal("العنوان", Assert.Single(columns.Specs).Header);
            Assert.Equal("لوحة", Fold(columns, new() { ["Name"] = "Board", ["Name2"] = "لوحة" })[0]);
            Assert.Equal("Board", Fold(columns, new() { ["Name"] = "Board", ["Name2"] = " " })[0]);
        }

        [Fact]
        public void A_group_reached_through_a_navigation_reads_the_targets_twins()
        {
            using ExportScope scope = new(composition.Services, "ar", "en", "ar");

            DisplayColumns columns = Expand<Widget>(scope, "CreatedBy.Name");

            Assert.Equal(["CreatedBy.Name2", "CreatedBy.Name"], columns.SelectPaths);
            Assert.StartsWith(Label(scope, typeof(Widget), "CreatedBy") + " / ", Assert.Single(columns.Specs).Header, StringComparison.Ordinal);
        }

        [Fact]
        public void The_identity_is_read_only_when_listed()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            Assert.Equal(["Code"], Expand<Widget>(scope, "Code").SelectPaths);
            Assert.Equal(["Code", "Id"], Expand<Widget>(scope, "Code", "Id").SelectPaths);
        }

        [Fact]
        public void Every_select_term_is_read_once_however_many_columns_read_it()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            DisplayColumns columns = Expand<Widget>(scope, "CreatedBy", "CreatedBy.Name", "Code", "Code", "CreatedBy.Id");

            Assert.Equal(columns.SelectPaths.Distinct(StringComparer.Ordinal), columns.SelectPaths);
            Assert.Equal(5, columns.Specs.Count);
            Assert.Equal(["Ahmad", "Ahmad", "W-1", "W-1", 5], Fold(columns, new() { ["CreatedBy.Id"] = 5, ["CreatedBy.Name"] = "Ahmad", ["Code"] = "W-1" }));
        }

        [Fact]
        public void A_header_is_the_requests_else_the_labels_of_the_path_else_the_expression_as_sent()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            ExportColumn[] request =
            [
                new("Code", "My code"),
                new("CreatedBy.Email", null),
                new("Price", " "),
                new(" Price * 2 ", null),
            ];

            var columns = DisplayColumns.Expand(scope.Metadata.Get(typeof(Widget)), request, scope.Context, Engine, Schema(MultilingualShape.Primary), DisplayNames);

            Assert.Equal(
                [
                    "My code",
                    $"{Label(scope, typeof(Widget), "CreatedBy")} / {scope.Context.Labels.PropertyLabel(typeof(User), "Email")}",
                    scope.Context.Labels.PropertyLabel(typeof(Widget), "Price"),
                    " Price * 2 ",
                ],
                columns.Specs.Select(static spec => spec.Header));
        }

        [Fact]
        public void A_derived_header_that_coincides_with_another_gets_its_expression_and_a_supplied_one_stays_as_sent()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            string code = scope.Context.Labels.PropertyLabel(typeof(Widget), "Code");
            string price = scope.Context.Labels.PropertyLabel(typeof(Widget), "Price");
            ExportColumn[] request =
            [
                new("Code", null),
                new("Price", code.ToUpperInvariant()),
                new("Price", null),
                new(" Price ", null),
                new("CreatedBy", null),
            ];

            var columns = DisplayColumns.Expand(scope.Metadata.Get(typeof(Widget)), request, scope.Context, Engine, Schema(MultilingualShape.Primary), DisplayNames);

            // Coinciding ignores case; the supplied header and the unique one are untouched.
            Assert.Equal(
                [
                    $"{code} (Code)",
                    code.ToUpperInvariant(),
                    $"{price} (Price)",
                    $"{price} (Price)",
                    Label(scope, typeof(Widget), "CreatedBy"),
                ],
                columns.Specs.Select(static spec => spec.Header));
        }

        [Fact]
        public void A_path_or_expression_takes_the_engines_type_and_a_bare_decimal_its_scale()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            DisplayColumns columns = Expand<Widget>(scope, "Price", "Price * 2", "DueOn", "IsActive");

            Assert.Equal([QueryexType.QxNumeric, QueryexType.QxNumeric, QueryexType.QxDate, QueryexType.QxBool], columns.Specs.Select(static spec => spec.Type));
            Assert.Equal([4, null, null, null], columns.Specs.Select(static spec => spec.Scale));
            Assert.Null(columns.Specs[1].Path);
        }

        [Fact]
        public void A_two_segment_path_through_a_navigation_is_a_reference_in_the_targets_key()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            DisplayColumns columns = Expand<Widget>(scope, "CreatedBy.Email", "CreatedBy.Id");

            Assert.Equal(
                [(ExcelColumnRole.Reference, "core.User", ExcelKeyKind.Natural), (ExcelColumnRole.Reference, "core.User", ExcelKeyKind.Surrogate)],
                columns.Specs.Select(static spec => (spec.Role, spec.ReferenceEntity, spec.KeyKind)));
        }

        [Fact]
        public void A_value_set_column_writes_each_value_as_its_label()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            ExcelColumnSpec spec = Assert.Single(Expand<User>(scope, "PreferredLanguage").Specs);
            var encoding = ColumnEncoding.For(spec, ExportKind.Display, scope.Context, numberFormat: null, spec.Header.Length);

            string label = scope.Context.Labels.ValueLabel(CoreValueSets.Language, "ar");
            Assert.NotEqual("ar", label);
            Assert.Equal(new EncodedCell(EncodedCellKind.Text, label), CellCodec.Encode("ar", encoding));
        }

        [Fact]
        public void An_expression_that_does_not_compile_is_refused_at_its_column()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            InvalidQueryException fault = Assert.Throws<InvalidQueryException>(() => Expand<Widget>(scope, "Code", "Missing + 1"));

            Assert.All(fault.Diagnostics, static diagnostic => Assert.Equal("columns[1]", diagnostic.Location));
        }

        [Fact]
        public void An_aggregate_is_refused_at_its_column()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            InvalidQueryException fault = Assert.Throws<InvalidQueryException>(() => Expand<Widget>(scope, "Count(Id)"));

            Assert.All(fault.Diagnostics, static diagnostic => Assert.Equal("columns[0]", diagnostic.Location));
        }

        [Fact]
        public void A_list_of_expressions_is_refused_at_its_column()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            BadRequestException fault = Assert.Throws<BadRequestException>(() => Expand<Widget>(scope, "Code", "Code, Price"));

            Assert.Equal("columns[1]", fault.Arguments!["path"]);
        }

        /// <summary>Expands columns of an entity, each with no header of its own.</summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <param name="scope">The reader's scope.</param>
        /// <param name="expressions">The columns' expressions.</param>
        /// <returns>The expansion.</returns>
        private DisplayColumns Expand<TEntity>(ExportScope scope, params string[] expressions)
        {
            var shape = (MultilingualShape)scope.Context.TenantLanguages.Count;
            return DisplayColumns.Expand(
                scope.Metadata.Get(typeof(TEntity)),
                [.. expressions.Select(static expression => new ExportColumn(expression, Header: null))],
                scope.Context,
                Engine,
                Schema(shape),
                DisplayNames);
        }

        /// <summary>The query schema of a content-language shape.</summary>
        /// <param name="shape">The shape.</param>
        /// <returns>The schema.</returns>
        private QueryexSchema Schema(MultilingualShape shape)
        {
            return composition.Services.GetRequiredService<IQueryexSchemaProvider>().GetSchema(shape);
        }

        /// <summary>Folds a row given by select term; a term it omits reads null.</summary>
        /// <param name="columns">The expansion.</param>
        /// <param name="values">The row's values by select term.</param>
        /// <returns>The folded row.</returns>
        private static IReadOnlyList<object?> Fold(DisplayColumns columns, Dictionary<string, object?> values)
        {
            return columns.Fold([.. columns.SelectPaths.Select(term => values.GetValueOrDefault(term))]);
        }

        /// <summary>A navigation's label in the scope's language.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="entity">The entity declaring the navigation.</param>
        /// <param name="navigation">The navigation.</param>
        /// <returns>The label.</returns>
        private static string Label(ExportScope scope, Type entity, string navigation)
        {
            return scope.Context.Labels.NavigationLabel(entity, navigation);
        }
    }
}
