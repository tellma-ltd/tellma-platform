// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Excel;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Infrastructure;
using Tellma.Core.Tests.Localization;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The editable and display plans over the fixture entities: the parent sheet's identity,
    ///     stamp and editable columns, one column per tenant language for a multilingual group, each
    ///     foreign key as one reference column in its target's natural key, a requested key or the
    ///     surrogate id, the headers in the reader's language, a sheet per selected collection at
    ///     every depth named from its labels, and the subsets a request selects.
    /// </summary>
    /// <param name="composition">The fixture model with its labels, composed once for the class.</param>
    public sealed class ExportPlannerTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>
    {
        /// <summary>The most parent rows the plans are made for.</summary>
        private const int Cap = 100;

        /// <summary>The planner over the composition's value sets.</summary>
        private readonly ExportPlanner _planner = new(composition.Services.GetRequiredService<IValueSetRegistry>(), "1.2.3");

        [Fact]
        public void The_parent_sheet_carries_the_identity_the_stamp_and_each_editable_column_in_declaration_order()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");

            ExcelSheetPlan sheet = Plan<Widget>(scope, new ExportForImportRequest { Children = [] }).Sheets[0];

            Assert.Equal(
                ["Id", "Stamp", "Code", "Kind", "Name", "Name", "Subject", "Price", "OptionalKind", "DueOn"],
                sheet.Columns.Select(static column => column.PathText));
            Assert.Equal(
                [ExcelColumnRole.Id, ExcelColumnRole.Stamp, .. Enumerable.Repeat(ExcelColumnRole.Property, 8)],
                sheet.Columns.Select(static column => column.Role));
            Assert.Equal([null, null, null, null, "en", "ar", null, null, null, null], sheet.Columns.Select(static column => column.Language));
            Assert.Equal(["Id", "ModifiedAt", "Code", "Kind", "Name", "Name2", "Subject", "Price", "OptionalKind", "DueOn"], sheet.Query.SelectPaths);
            Assert.Equal(QueryexType.QxString, sheet.Columns[1].Type);
        }

        [Fact]
        public void A_tenant_language_beyond_the_groups_slots_adds_no_column_and_one_language_adds_one()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            ExcelSheetPlan sheet = Plan<Widget>(scope, new ExportForImportRequest { Children = [] }).Sheets[0];

            ExcelColumnSpec name = Assert.Single(sheet.Columns, static column => column.PathText == "Name");
            Assert.Equal("en", name.Language);
        }

        [Fact]
        public void The_headers_are_the_labels_in_the_readers_language_each_twin_marked_with_its_language()
        {
            string[] english;
            string[] arabic;
            string en;
            string ar;
            using (ExportScope scope = new(composition.Services, "en", "en", "ar"))
            {
                english = Headers(Plan<Widget>(scope, new ExportForImportRequest { Children = [] }).Sheets[0]);
                (en, ar) = (scope.Symbol("en"), scope.Symbol("ar"));
            }

            using (ExportScope scope = new(composition.Services, "ar", "en", "ar"))
            {
                arabic = Headers(Plan<Widget>(scope, new ExportForImportRequest { Children = [] }).Sheets[0]);
            }

            Assert.Equal(["Id", "Stamp"], english[..2]);
            Assert.Equal([$"Title ({en})", $"Title ({ar})"], english[4..6]);
            Assert.Equal([$"العنوان ({en})", $"العنوان ({ar})"], arabic[4..6]);

            // The identity and the stamp are the columns' names, whatever the reader's language.
            Assert.Equal(["Id", "Stamp"], arabic[..2]);
        }

        [Fact]
        public void A_sheet_per_collection_follows_its_owner_depth_first_named_from_its_labels_with_leading_segments_dropped()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            ExcelWorkbookPlan plan = Plan<Widget>(scope, new ExportForImportRequest());

            Assert.Equal(["Contraptions", "Contraptions · Components", "Components · Remarks"], plan.Sheets.Select(static sheet => sheet.Name));
            Assert.Equal([null, "Parts", "Parts.PartNotes"], plan.Sheets.Select(static sheet => sheet.Collection));

            // Each sheet's query rides its owner's, ordered by its owner and then itself.
            ExcelQuery parts = Assert.Single(plan.Sheets[0].Query.Dependents!);
            Assert.Same(plan.Sheets[1].Query, parts);
            Assert.Same(plan.Sheets[2].Query, Assert.Single(parts.Dependents!));
            Assert.Equal("WidgetId, Id", parts.OrderBy);
            Assert.Null(parts.Take);
            Assert.Null(parts.CountCap);
        }

        [Fact]
        public void A_child_sheet_leads_with_the_reference_to_its_owner_row_then_its_identity()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            ExcelWorkbookPlan plan = Plan<Widget>(scope, new ExportForImportRequest());

            ExcelColumnSpec owner = plan.Sheets[1].Columns[0];
            Assert.Equal((ExcelColumnRole.ParentReference, "fixture.Widget", "Widget.Id"), (owner.Role, owner.ReferenceEntity, owner.PathText));
            Assert.EndsWith(" / Id", owner.Header, StringComparison.Ordinal);
            Assert.Equal((ExcelColumnRole.Id, "Id"), (plan.Sheets[1].Columns[1].Role, plan.Sheets[1].Columns[1].Header));
            Assert.Equal(["WidgetId", "Id", "Ordinal", "Notes"], plan.Sheets[1].Query.SelectPaths);
            Assert.Equal(["WidgetPartId", "Id", "Text"], plan.Sheets[2].Query.SelectPaths);
        }

        [Fact]
        public void A_sheet_name_drops_its_leading_segments_but_never_the_last_while_it_is_too_long()
        {
            Assert.Equal("Orders · Lines", ExportPlanner.ChildSheetName(["Orders", "Lines"]));
            Assert.Equal("Lines · Allocations", ExportPlanner.ChildSheetName(["Purchase orders", "Lines", "Allocations"]));
            Assert.Equal("Allocations of a very long collection", ExportPlanner.ChildSheetName(["Orders", "Lines", "Allocations of a very long collection"]));

            // The refused characters are dropped before the length is measured.
            Assert.Equal("A/B · Lines · Allocations of[]", ExportPlanner.ChildSheetName(["A/B", "Lines", "Allocations of[]"]));
        }

        [Fact]
        public void A_columns_subset_keeps_the_listed_columns_the_natural_key_and_the_leading_columns()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");

            ExcelSheetPlan sheet = Plan<Widget>(scope, new ExportForImportRequest { Columns = ["Price", "Name2"], Children = [] }).Sheets[0];

            Assert.Equal(["Id", "Stamp", "Code", "Name", "Price"], sheet.Columns.Select(static column => column.PathText));
            Assert.Equal("ar", sheet.Columns[3].Language);
        }

        [Fact]
        public void A_group_listed_at_its_primary_keeps_every_language()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");

            ExcelSheetPlan sheet = Plan<Widget>(scope, new ExportForImportRequest { Columns = ["Name"], Children = [] }).Sheets[0];

            Assert.Equal([null, null, null, "en", "ar"], sheet.Columns.Select(static column => column.Language));
        }

        [Fact]
        public void A_children_subset_selects_collections_at_every_depth_with_their_own_columns()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            ExcelWorkbookPlan plan = Plan<Widget>(scope, new ExportForImportRequest
            {
                Children = [new ExportCollection("Parts", ["Notes"], ReferenceKeys: null, Children: [])],
            });

            Assert.Equal([null, "Parts"], plan.Sheets.Select(static sheet => sheet.Collection));
            Assert.Equal(["WidgetId", "Id", "Notes"], plan.Sheets[1].Query.SelectPaths);
            Assert.Null(plan.Sheets[1].Query.Dependents);
        }

        [Fact]
        public void An_empty_children_list_plans_the_parent_sheet_alone()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            ExcelWorkbookPlan plan = Plan<Widget>(scope, new ExportForImportRequest { Children = [] });

            Assert.Single(plan.Sheets);
            Assert.Null(plan.Sheets[0].Query.Dependents);
        }

        [Fact]
        public void A_reference_defaults_to_the_targets_first_natural_key_that_cannot_be_null()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            ExcelSheetPlan memberships = Plan<User>(scope, new ExportForImportRequest { Columns = ["Email"] }).Sheets[1];

            // The role's declared key, its code, may be null, so a role without one would export a
            // blank cell; its name, a required natural key, names every role.
            ExcelColumnSpec role = Assert.Single(memberships.Columns, static column => column.Role == ExcelColumnRole.Reference);
            Assert.Equal(("Role.Name", ExcelKeyKind.Natural, "core.Role", "en"), (role.PathText, role.KeyKind, role.ReferenceEntity, role.Language));
            Assert.Contains("Role.Name", memberships.Query.SelectPaths);
            Assert.DoesNotContain("Role.Code", memberships.Query.SelectPaths);
        }

        [Fact]
        public void A_reference_key_names_another_scalar_of_the_target_a_multilingual_one_in_its_slots_language()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");

            ExcelSheetPlan byName = Plan<User>(scope, Memberships(new Dictionary<string, string> { ["RoleId"] = "Name2" })).Sheets[1];
            ExcelSheetPlan byId = Plan<User>(scope, Memberships(new Dictionary<string, string> { ["RoleId"] = "Id" })).Sheets[1];

            ExcelColumnSpec name = Assert.Single(byName.Columns, static column => column.Role == ExcelColumnRole.Reference);
            Assert.Equal(("Role.Name", "ar", ExcelKeyKind.Natural), (name.PathText, name.Language, name.KeyKind));
            Assert.Contains("Role.Name2", byName.Query.SelectPaths);

            ExcelColumnSpec id = Assert.Single(byId.Columns, static column => column.Role == ExcelColumnRole.Reference);
            Assert.Equal(("Role.Id", ExcelKeyKind.Surrogate), (id.PathText, id.KeyKind));
            Assert.EndsWith(" / Id", id.Header, StringComparison.Ordinal);
        }

        [Fact]
        public void A_unique_code_is_the_targets_natural_key_at_every_depth()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            ExcelWorkbookPlan plan = Plan<Node>(scope, new ExportForImportRequest());

            ExcelColumnSpec parent = Assert.Single(plan.Sheets[0].Columns, static column => column.Role == ExcelColumnRole.Reference);
            Assert.Equal(("Parent.Code", ExcelKeyKind.Natural, "Parent node / Code"), (parent.PathText, parent.KeyKind, parent.Header));
            ExcelColumnSpec other = Assert.Single(plan.Sheets[1].Columns, static column => column.Role == ExcelColumnRole.Reference);
            Assert.Equal(("OtherNode.Code", ExcelKeyKind.Natural), (other.PathText, other.KeyKind));
        }

        [Fact]
        public async Task A_target_without_a_natural_key_falls_back_to_its_surrogate_id()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma.AddFeature(new TestFeature<Tag.D>("xp", contribute: static contribution => contribution
                .Entity<XpBin>()
                .Entity<XpCrate>()))).BuildServiceProvider(validateScopes: true);
            using ExportScope scope = new(provider, "en", "en");

            ExcelSheetPlan sheet = _planner.PlanEditable(scope.Metadata.Get(typeof(XpCrate)), new ExportForImportRequest(), Cap, scope.Context).Sheets[0];

            ExcelColumnSpec bin = Assert.Single(sheet.Columns, static column => column.Role == ExcelColumnRole.Reference);
            Assert.Equal(("Bin.Id", ExcelKeyKind.Surrogate, "xp.XpBin"), (bin.PathText, bin.KeyKind, bin.ReferenceEntity));
            Assert.EndsWith(" / Id", bin.Header, StringComparison.Ordinal);
            Assert.Contains("Bin.Id", sheet.Query.SelectPaths);
        }

        [Fact]
        public void A_value_set_column_offers_the_sets_stored_values()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            ExcelSheetPlan sheet = Plan<User>(scope, new ExportForImportRequest { Columns = ["PreferredLanguage"], Children = [] }).Sheets[0];

            ExcelColumnSpec language = Assert.Single(sheet.Columns, static column => column.PathText == "PreferredLanguage");
            Assert.Contains("en", language.Choices!);
            Assert.Contains("ar", language.Choices!);
        }

        [Theory]
        [InlineData("Settings")]
        [InlineData("ImageId")]
        [InlineData("CreatedAt")]
        [InlineData("Id")]
        [InlineData("Missing")]
        public void A_column_the_import_does_not_write_is_refused(string path)
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            BadRequestException fault = Assert.Throws<BadRequestException>(() => Plan<Widget>(scope, new ExportForImportRequest { Columns = [path] }));

            Assert.Equal("columns[0]", fault.Arguments!["path"]);
            Assert.Equal(path, fault.Arguments["name"]);
        }

        [Fact]
        public void A_reference_key_of_no_foreign_key_or_of_no_scalar_of_the_target_is_refused()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            BadRequestException notForeign = Assert.Throws<BadRequestException>(() => Plan<User>(scope, Memberships(new Dictionary<string, string> { ["Notes"] = "Code" })));
            BadRequestException notScalar = Assert.Throws<BadRequestException>(() => Plan<User>(scope, Memberships(new Dictionary<string, string> { ["RoleId"] = "Permissions" })));
            BadRequestException noSlot = Assert.Throws<BadRequestException>(() => Plan<User>(scope, Memberships(new Dictionary<string, string> { ["RoleId"] = "Name2" })));

            Assert.Equal("children[0].referenceKeys.Notes", notForeign.Arguments!["path"]);
            Assert.Equal("children[0].referenceKeys.RoleId", notScalar.Arguments!["path"]);
            Assert.Equal("children[0].referenceKeys.RoleId", noSlot.Arguments!["path"]);
        }

        [Fact]
        public void An_unknown_or_repeated_collection_is_refused_at_its_place_in_the_request()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            BadRequestException unknown = Assert.Throws<BadRequestException>(() => Plan<Widget>(scope, new ExportForImportRequest
            {
                Children = [new ExportCollection("Parts", null, null, [new ExportCollection("Notes", null, null, null)])],
            }));
            BadRequestException repeated = Assert.Throws<BadRequestException>(() => Plan<Widget>(scope, new ExportForImportRequest
            {
                Children = [new ExportCollection("Parts", null, null, null), new ExportCollection("Parts", null, null, null)],
            }));
            BadRequestException missing = Assert.Throws<BadRequestException>(() => Plan<Widget>(scope, new ExportForImportRequest { Children = [null!] }));

            Assert.Equal(("children[0].children[0]", "Notes"), (unknown.Arguments!["path"], unknown.Arguments["name"]));
            Assert.Equal(("children[1]", "Parts"), (repeated.Arguments!["path"], repeated.Arguments["name"]));
            Assert.Equal("children[0]", missing.Arguments!["path"]);
        }

        [Fact]
        public void A_collection_of_a_child_without_its_own_collection_is_refused_as_unknown()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            BadRequestException fault = Assert.Throws<BadRequestException>(() => Plan<Widget>(scope, new ExportForImportRequest
            {
                Children = [new ExportCollection("Parts", null, null, [new ExportCollection("PartNotes", null, null, [new ExportCollection("Parts", null, null, null)])])],
            }));

            Assert.Equal("children[0].children[0].children[0]", fault.Arguments!["path"]);
        }

        [Fact]
        public void The_parent_query_orders_by_the_request_else_the_key_and_counts_only_a_query_export()
        {
            using ExportScope scope = new(composition.Services, "en", "en");

            ExcelQuery byQuery = Plan<Widget>(scope, new ExportForImportRequest { Children = [] }).Sheets[0].Query;
            ExcelQuery byIds = Plan<Widget>(scope, new ExportForImportRequest { Ids = [1], OrderBy = "Code desc", Children = [] }).Sheets[0].Query;

            Assert.Equal(("Id", Cap + 1, Cap), (byQuery.OrderBy, byQuery.Take, byQuery.CountCap));
            Assert.Equal(("Code desc", Cap + 1, null), (byIds.OrderBy, byIds.Take, byIds.CountCap));
        }

        [Fact]
        public void The_plan_carries_the_manifest_and_the_file_name_of_its_shape()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");

            ExcelWorkbookPlan plan = Plan<Widget>(scope, new ExportForImportRequest { Children = [] });

            Assert.Equal(ExportKind.ForImport, plan.Shape);
            Assert.Equal("ForImport", plan.Manifest[ExcelManifest.ShapeKey]);
            Assert.Equal("fixture.Widget", plan.Manifest[ExcelManifest.EntityKey]);
            Assert.Equal("deployment-a", plan.Manifest[ExcelManifest.DeploymentKey]);
            Assert.Equal("7", plan.Manifest[ExcelManifest.SourceTenantIdKey]);
            Assert.Equal("1.2.3", plan.Manifest[ExcelManifest.PlatformKey]);
            Assert.Equal("en, ar", plan.Manifest[ExcelManifest.LanguagesKey]);
            Assert.StartsWith("Contraptions ", plan.FileName, StringComparison.Ordinal);
            Assert.EndsWith(".xlsx", plan.FileName, StringComparison.Ordinal);
        }

        [Fact]
        public void A_display_plan_wraps_its_columns_and_query_in_one_sheet()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            ExcelColumnSpec column = new("Code", QueryexType.QxString, Scale: null, ["Code"]);
            ExcelQuery query = new("fixture.Widget", ["Code"], OrderBy: null, Restriction: null, RestrictionValues: null, Take: 11, CountCap: 10, Dependents: null);

            ExcelWorkbookPlan plan = _planner.PlanDisplay(scope.Metadata.Get(typeof(Widget)), [column], query, scope.Context);

            ExcelSheetPlan sheet = Assert.Single(plan.Sheets);
            Assert.Equal(("Contraptions", ExportKind.Display, "Display"), (sheet.Name, plan.Shape, plan.Manifest[ExcelManifest.ShapeKey]));
            Assert.Same(query, sheet.Query);
            Assert.Equal([column], sheet.Columns);
        }

        /// <summary>Plans an editable export of an entity in a scope.</summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <param name="scope">The reader's scope.</param>
        /// <param name="request">The request.</param>
        /// <returns>The plan.</returns>
        private ExcelWorkbookPlan Plan<TEntity>(ExportScope scope, ExportForImportRequest request)
        {
            return _planner.PlanEditable(scope.Metadata.Get(typeof(TEntity)), request, Cap, scope.Context);
        }

        /// <summary>A user export whose role memberships are expressed in the given keys.</summary>
        /// <param name="keys">The memberships' reference keys.</param>
        /// <returns>The request.</returns>
        private static ExportForImportRequest Memberships(Dictionary<string, string> keys)
        {
            return new ExportForImportRequest { Children = [new ExportCollection(nameof(User.RoleMemberships), Columns: null, keys, Children: null)] };
        }

        /// <summary>A sheet's headers.</summary>
        /// <param name="sheet">The sheet.</param>
        /// <returns>The headers.</returns>
        private static string[] Headers(ExcelSheetPlan sheet)
        {
            return [.. sheet.Columns.Select(static column => column.Header)];
        }
    }

    /// <summary>A bin of the planner's own model, with no natural key.</summary>
    [Table("Bins", Schema = "xp")]
    [TableType]
    public class XpBin : TopLevelEntity
    {
        /// <summary>The bin's name, which need not be unique.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>A crate of the planner's own model, stored in a bin.</summary>
    [Table("Crates", Schema = "xp")]
    [TableType]
    public class XpCrate : TopLevelEntity
    {
        /// <summary>The crate's label.</summary>
        [MaxLength(255)]
        public string Label { get; set; } = null!;

        /// <summary>The bin the crate is stored in.</summary>
        [References(typeof(XpBin))]
        public int? BinId { get; set; }
    }
}
