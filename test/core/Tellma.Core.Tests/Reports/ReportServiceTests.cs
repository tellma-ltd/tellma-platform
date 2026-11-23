// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Reports;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;
using Tellma.Core.Reports;
using Tellma.Core.Tests.Access;
using Tellma.Core.Tests.Sharing;

namespace Tellma.Core.Tests.Reports
{
    /// <summary>
    ///     The reports' service hooks: default titles per slot in each slot's tenant language, derived
    ///     again under a changed root or kind; the document's strict binding; the query language
    ///     version; the seeds' reserved codes; and the dashboards that block a delete.
    /// </summary>
    public sealed class ReportServiceTests : IDisposable
    {
        /// <summary>The service.</summary>
        private readonly SharingKit _kit = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        [Fact]
        public async Task An_empty_title_fills_every_empty_configured_slot_in_its_tenant_language()
        {
            Report row = SharingKit.Row(title: "");

            await _kit.Hooks.PreprocessAsync(_kit.Save(system: false, (row, null)));

            Assert.Equal("Users – list", row.Title);
            Assert.Equal("المستخدمون – قائمة", row.Title2);
            Assert.Null(row.Title3);
        }

        [Theory]
        [InlineData(ReportKind.List, "Roles – list", "الأدوار – قائمة")]
        [InlineData(ReportKind.Summary, "Roles – summary", "الأدوار – ملخص")]
        [InlineData(ReportKind.Pivot, "Roles – pivot", "الأدوار – جدول محوري")]
        [InlineData(ReportKind.Chart, "Roles – chart", "الأدوار – مخطط")]
        public async Task The_default_title_names_the_roots_plural_label_and_the_kind(ReportKind kind, string english, string arabic)
        {
            Report row = SharingKit.Row(title: " ");
            row.Root = "core.Role";
            row.Kind = kind;

            await _kit.Hooks.PreprocessAsync(_kit.Save(system: false, (row, null)));

            Assert.Equal((english, arabic), (row.Title, row.Title2));
        }

        [Fact]
        public async Task A_typed_slot_keeps_its_text_and_a_typed_title_fills_nothing()
        {
            Report untitled = SharingKit.Row(title: "");
            untitled.Title2 = "مبيعات";
            Report titled = SharingKit.Row(title: "Sales");

            await _kit.Hooks.PreprocessAsync(_kit.Save(system: false, (untitled, null), (titled, null)));

            Assert.Equal(("Users – list", "مبيعات"), (untitled.Title, untitled.Title2));
            Assert.Equal("Sales", titled.Title);
            Assert.Null(titled.Title2);
        }

        [Fact]
        public async Task A_root_the_model_does_not_name_renders_as_its_name()
        {
            Report row = SharingKit.Row(title: "");
            row.Root = "fx.Unknown";

            await _kit.Hooks.PreprocessAsync(_kit.Save(system: false, (row, null)));

            Assert.Equal("fx.Unknown – list", row.Title);
        }

        [Fact]
        public void The_default_title_is_cut_to_255_characters_never_inside_a_surrogate_pair()
        {
            string plain = new('a', 300);
            string paired = new string('a', 254) + "\U0001F600" + "b";

            Assert.Equal(new string('a', 255), ReportTitles.Cut(plain));
            Assert.Equal(new string('a', 254), ReportTitles.Cut(paired));
            Assert.Equal("short", ReportTitles.Cut("short"));
        }

        [Fact]
        public async Task A_changed_root_derives_again_each_slot_that_still_holds_the_old_default()
        {
            Report before = SharingKit.Row(id: 5, title: "Users – list");
            before.Title2 = "المستخدمون – قائمة";
            Report row = SharingKit.Row(id: 5, title: "Users – list");
            row.Title2 = "مستخدمون";
            row.Root = "core.Role";
            row.Kind = ReportKind.Chart;
            SaveContext<Report> context = _kit.Save(system: false, (row, before));

            await _kit.Hooks.PreprocessAsync(context);
            await _kit.Hooks.ValidateAsync(context);

            Assert.Equal(("Roles – chart", "مستخدمون"), (row.Title, row.Title2));
        }

        [Fact]
        public async Task An_unchanged_root_and_kind_keep_a_title_that_equals_the_default()
        {
            Report before = SharingKit.Row(id: 5, title: "Users – list");
            Report row = SharingKit.Row(id: 5, title: "Users – list");
            row.Description = "changed";
            SaveContext<Report> context = _kit.Save(system: false, (row, before));

            await _kit.Hooks.ValidateAsync(context);

            Assert.Equal("Users – list", row.Title);
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{ "formatVersion": 1, "list": { "columns": [ { "path": "Name", "running": true } ] } }""", "/list/columns/0/running")]
        [InlineData(/*lang=json,strict*/ """{ "formatVersion": 1, "lst": { } }""", "/lst")]
        [InlineData(/*lang=json,strict*/ """{ "formatVersion": 1, "chart": { "type": "radar", "dimensions": [], "measure": { "path": "Id" } } }""", "/chart/type")]
        [InlineData(/*lang=json,strict*/ """{ "formatVersion": "one" }""", "/formatVersion")]
        [InlineData("""null""", "")]
        public async Task A_document_that_fails_to_bind_is_refused_with_the_members_pointer(string json, string pointer)
        {
            Report row = SharingKit.Row();
            row.DefinitionJson = json;
            SaveContext<Report> context = _kit.Save(system: false, (row, null));

            await _kit.Hooks.ValidateAsync(context);

            Assert.Equal([("[0].DefinitionJson", ReportValidationCodes.UnknownMember)], AccessRulesHarness.Errors(context.Errors));
            Assert.Equal(pointer, Assert.Single(context.Errors.Items).Arguments["pointer"]);
        }

        [Fact]
        public async Task A_bound_document_saves_and_a_new_row_takes_the_current_query_language_version()
        {
            Report row = SharingKit.Row();
            row.QueryexVersion = 99;
            SaveContext<Report> context = _kit.Save(system: false, (row, null));

            await _kit.Hooks.ValidateAsync(context);

            Assert.Empty(AccessRulesHarness.Errors(context.Errors));
            Assert.Equal(QueryexLanguage.Version, row.QueryexVersion);
        }

        [Theory]
        [InlineData(/*lang=json,strict*/ """{ "formatVersion": 1, "filter": "Id > 1", "list": { "columns": [ { "path": "Name", "label": "Renamed" } ] } }""", false)]
        [InlineData(/*lang=json,strict*/ """{ "formatVersion": 1, "filter": "Id > 2", "list": { "columns": [ { "path": "Name" } ] } }""", true)]
        [InlineData(/*lang=json,strict*/ """{ "formatVersion": 1, "filter": "Id > 1", "list": { "columns": [ { "expr": "Name" } ] } }""", true)]
        public async Task An_update_keeps_its_stored_version_unless_an_expression_changed(string json, bool changed)
        {
            Report before = SharingKit.Row(id: 5);
            before.DefinitionJson = /*lang=json,strict*/ """{ "formatVersion": 1, "filter": "Id > 1", "list": { "columns": [ { "path": "Name" } ] } }""";
            before.QueryexVersion = 0;
            Report row = SharingKit.Row(id: 5);
            row.DefinitionJson = json;
            SaveContext<Report> context = _kit.Save(system: false, (row, before));

            await _kit.Hooks.ValidateAsync(context);

            Assert.Equal(changed ? QueryexLanguage.Version : 0, row.QueryexVersion);
        }

        [Fact]
        public void The_expressions_are_the_filter_havings_term_exprs_order_exprs_and_parameter_defaults()
        {
            Assert.True(ReportDocuments.TryParse(
                /*lang=json,strict*/ """
                {
                  "formatVersion": 1,
                  "parameters": [ { "key": "From", "default": "startOfYear(today())" }, { "key": "To" } ],
                  "filter": "Date >= @From",
                  "pivot": {
                    "rows": [ { "path": "Center", "attributes": [ { "expr": "Center.Code" } ] } ],
                    "columns": [ { "expr": "year(Date)" } ],
                    "measures": [ { "expr": "sum(Amount)" }, { "path": "Id" } ],
                    "having": "sum(Amount) > 0"
                  },
                  "drillColumns": [ "Name" ]
                }
                """,
                out ReportDocument? document,
                out _));

            Assert.Equal(
                ["startOfYear(today())", "Date >= @From", "Center.Code", "year(Date)", "sum(Amount)", "sum(Amount) > 0"],
                ReportDocuments.Expressions(document!));
        }

        [Theory]
        [InlineData(null, "")]
        [InlineData("$", "")]
        [InlineData("$.list.columns[0].running", "/list/columns/0/running")]
        [InlineData("$['a/b'].c", "/a~1b/c")]
        [InlineData("$['it\\'s'][2]", "/it's/2")]
        [InlineData("$.x~y", "/x~0y")]
        public void A_json_path_converts_to_its_pointer(string? path, string pointer)
        {
            Assert.Equal(pointer, ReportDocuments.Pointer(path));
        }

        [Theory]
        [InlineData("core.sales", "core.")]
        [InlineData("CORE.Sales", "core.")]
        [InlineData("acme.sales-by-center", null)]
        [InlineData("my-report", null)]
        [InlineData("corex.sales", null)]
        public async Task A_code_starting_with_a_features_name_and_a_dot_is_reserved_outside_a_system_scope(string code, string? prefix)
        {
            Report row = SharingKit.Row();
            row.Code = code;
            SaveContext<Report> user = _kit.Save(system: false, (row, null));
            SaveContext<Report> system = _kit.Save(system: true, (SharingKit.Row(owner: null), null));
            system.Entities[0].Code = code;

            await _kit.Hooks.ValidateAsync(user);
            await _kit.Hooks.ValidateAsync(system);

            Assert.Equal(prefix is null ? [] : [("[0].Code", ReportValidationCodes.CodeReserved)], AccessRulesHarness.Errors(user.Errors));
            Assert.Equal(prefix, user.Errors.Items.SingleOrDefault()?.Arguments["prefix"]);
            Assert.Empty(AccessRulesHarness.Errors(system.Errors));
        }

        [Fact]
        public async Task An_unchanged_reserved_code_is_not_refused()
        {
            Report before = SharingKit.Row(id: 5);
            before.Code = "core.sales";
            Report row = SharingKit.Row(id: 5);
            row.Code = "core.sales";
            SaveContext<Report> context = _kit.Save(system: false, (row, before));

            await _kit.Hooks.ValidateAsync(context);

            Assert.Empty(AccessRulesHarness.Errors(context.Errors));
        }

        [Fact]
        public async Task A_report_a_dashboard_shows_refuses_its_delete_naming_up_to_five_readable_dashboards()
        {
            _kit.Dashboards.Entries.AddRange(
            [
                (3, 1, "One"), (3, 2, "Two"), (3, 3, null), (3, 4, "Four"), (3, 5, "Five"), (3, 6, "Six"), (3, 7, "Seven"),
                (5, 8, null),
                (9, 9, "Elsewhere"),
            ]);
            DeleteContext<Report, int> context = _kit.Delete(system: false, [3, 4, 5], SharingKit.Row(id: 3), SharingKit.Row(id: 4), SharingKit.Row(id: 5));

            await _kit.Hooks.ValidateDeleteAsync(context);

            Assert.Equal([("Ids[0]", ReportValidationCodes.InUse), ("Ids[2]", ReportValidationCodes.InUse)], AccessRulesHarness.Errors(context.Errors));
            Assert.Equal([3, 4, 5], Assert.Single(_kit.Dashboards.Asked));
            IReadOnlyDictionary<string, object?> first = context.Errors.Items[0].Arguments;
            Assert.Equal(("One, Two, Four, Five, Six", 2, 5), (first["dashboards"], first["others"], first["named"]));
            IReadOnlyDictionary<string, object?> third = context.Errors.Items[1].Arguments;
            Assert.Equal((string.Empty, 1, 0), (third["dashboards"], third["others"], third["named"]));
        }
    }
}
