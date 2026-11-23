// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Excel;
using Tellma.Core.Tests.Localization;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The import's path grammar resolved against the fixture model and a tenant's languages,
    ///     what of it is importable, the collection paths of an entity, and the form checks of an
    ///     inspect request's overrides.
    /// </summary>
    /// <param name="composition">The fixture model with its labels, composed once for the class.</param>
    public sealed class ImportPathsTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>, IDisposable
    {
        /// <summary>The kit over the composition.</summary>
        private readonly ImportKit _kit = new(composition.Services);

        [Theory]
        [InlineData("Id", null, "Id", nameof(ImportPathKind.Id), null)]
        [InlineData("Stamp", null, "Stamp", nameof(ImportPathKind.Stamp), null)]
        [InlineData("Code", null, "Code", nameof(ImportPathKind.Property), null)]
        [InlineData("Name", null, "Name", nameof(ImportPathKind.Property), "ar")]
        [InlineData("Name", "ar", "Name", nameof(ImportPathKind.Property), "ar")]
        [InlineData("Name", "EN", "Name2", nameof(ImportPathKind.Property), "en")]
        [InlineData("Name2", "ar", "Name2", nameof(ImportPathKind.Property), "en")]
        [InlineData("Name3", null, "Name3", nameof(ImportPathKind.Property), null)]
        public void A_parent_sheets_path_resolves_through_the_tenants_slots(string path, string? language, string text, string kind, string? slotLanguage)
        {
            ImportPath resolved = Resolve<Widget>(path, language, "ar", "en");

            Assert.Equal((text, kind, slotLanguage), (resolved.Text, resolved.Kind.ToString(), resolved.Language));
        }

        [Fact]
        public void A_column_in_a_language_the_tenant_lacks_resolves_to_that_language_alone()
        {
            ImportPathResolution resolution = ImportPaths.Resolve(_kit.Entity<Widget>(), collection: null, "Name", "am", ["en", "ar"]);

            Assert.Equal((null, "am", false), (resolution.Path, resolution.UnconfiguredLanguage, resolution.IsUnknown));
        }

        [Theory]
        [InlineData("Nope")]
        [InlineData("Code.Id")]
        [InlineData("Name.")]
        [InlineData(".Name")]
        [InlineData("Parts")]
        [InlineData("Parts.Id")]
        [InlineData("a.b.c")]
        [InlineData("code")]
        public void A_path_the_entity_does_not_carry_is_unknown(string path)
        {
            Assert.True(ImportPaths.Resolve(_kit.Entity<Widget>(), collection: null, path, language: null, ["en"]).IsUnknown);
        }

        [Fact]
        public void A_child_sheets_parent_link_resolves_from_its_navigation_or_its_foreign_key_and_no_stamp_resolves()
        {
            ImportCollection parts = ImportPaths.Collections(_kit.Entity<Widget>())[0];
            EntityMetadata part = parts.Collection.Child;

            ImportPath link = Resolve(part, parts.Collection, "Widget.Id");
            ImportPath key = Resolve(part, parts.Collection, "WidgetId");

            Assert.Equal((ImportPathKind.ParentReference, "Widget.Id"), (link.Kind, link.Text));
            Assert.Equal(link, key);
            Assert.True(ImportPaths.Resolve(part, parts.Collection, "Widget.Code", language: null, ["en"]).IsUnknown);
            Assert.True(ImportPaths.Resolve(part, parts.Collection, "Stamp", language: null, ["en"]).IsUnknown);
            Assert.True(ImportPaths.IsImportable(part, parts.Collection, link));
            Assert.Equal("Widget", ImportPaths.ParentNavigation(parts.Collection));
        }

        [Fact]
        public void A_reference_resolves_in_its_surrogate_its_bare_foreign_key_or_a_keyable_target_property_and_a_bare_navigation_is_never_importable()
        {
            EntityMetadata node = _kit.Entity<Node>();

            ImportPath surrogate = Resolve(node, null, "Parent.Id");
            ImportPath bare = Resolve(node, null, "ParentId");
            ImportPath code = Resolve(node, null, "Parent.Code");
            ImportPath navigation = Resolve(node, null, "Parent");

            Assert.Equal((ImportPathKind.Reference, "Parent.Id", "ParentId"), (surrogate.Kind, surrogate.Text, surrogate.Property.Name));
            Assert.Equal(surrogate, bare);
            Assert.Equal((ImportPathKind.Reference, "Parent.Code", "ParentId", "Code"), (code.Kind, code.Text, code.Property.Name, code.Key!.Name));
            Assert.Equal(ImportPathKind.Navigation, navigation.Kind);
            Assert.False(ImportPaths.IsImportable(node, null, navigation));
        }

        [Fact]
        public void Server_owned_and_excluded_properties_resolve_and_are_not_importable()
        {
            EntityMetadata widget = _kit.Entity<Widget>();

            Assert.False(ImportPaths.IsImportable(widget, null, Resolve(widget, null, "CreatedAt")));
            Assert.False(ImportPaths.IsImportable(widget, null, Resolve(widget, null, "Settings")));
            Assert.True(ImportPaths.IsImportable(widget, null, Resolve(widget, null, "Subject")));
            Assert.True(ImportPaths.IsImportable(widget, null, Resolve(widget, null, "Id")));
        }

        [Fact]
        public void An_entitys_collections_are_listed_depth_first_with_their_owners_and_label_chains()
        {
            IReadOnlyList<ImportCollection> collections = ImportPaths.Collections(_kit.Entity<Widget>());

            Assert.Equal(["Parts", "Parts.PartNotes"], collections.Select(static collection => collection.Path));
            Assert.Equal(["", "Parts"], collections.Select(static collection => collection.OwnerPath));
            Assert.Equal(["Parts", "PartNotes"], collections[1].Labels.Select(static label => label.Collection));
        }

        [Fact]
        public void A_slot_names_its_group_and_one_based_position()
        {
            EntityMetadata widget = _kit.Entity<Widget>();

            Assert.Equal(3, ImportPaths.Slot(widget, "Name3")!.Value.Slot);
            Assert.Equal("Name", ImportPaths.Slot(widget, "Name2")!.Value.Group.Primary.Name);
            Assert.Null(ImportPaths.Slot(widget, "Code"));
        }

        [Theory]
        [InlineData(0, "fileId")]
        [InlineData(-3, "fileId")]
        public void A_file_id_that_names_no_upload_is_refused(int fileId, string path)
        {
            BadRequestException fault = Assert.Throws<BadRequestException>(() => Check(fileId, [], []));

            Assert.Equal(path, fault.Arguments!["path"]);
        }

        [Fact]
        public void Missing_override_lists_are_refused()
        {
            Assert.Equal("sheetMappings", Assert.Throws<BadRequestException>(() => Check(1, null, [])).Arguments!["path"]);
            Assert.Equal("columnMappings", Assert.Throws<BadRequestException>(() => Check(1, [], null)).Arguments!["path"]);
        }

        [Fact]
        public void A_sheet_override_without_a_sheet_twice_for_one_sheet_to_an_unknown_collection_or_second_to_one_target_is_refused()
        {
            Assert.Equal("sheetMappings[0]", Fault([null!], []).Arguments!["path"]);
            Assert.Equal("sheetMappings[0].sheet", Fault([new(" ", "")], []).Arguments!["path"]);
            Assert.Equal(("sheetMappings[1].sheet", "data"), Pair(Fault([new("Data", ""), new("data", null)], []), "name"));
            Assert.Equal(("sheetMappings[0].collection", "Nope"), Pair(Fault([new("Data", "Nope")], []), "name"));
            Assert.Equal(("sheetMappings[1].collection", "Parts"), Pair(Fault([new("A", "Parts"), new("B", "Parts")], []), "name"));
            Assert.Equal(("sheetMappings[1].collection", ""), Pair(Fault([new("A", ""), new("B", "")], []), "name"));

            // Ignoring many sheets is no conflict.
            Check(1, [new("A", null), new("B", null), new("C", "Parts.PartNotes")], []);
        }

        [Fact]
        public void A_column_override_without_a_sheet_of_no_column_letter_or_twice_for_one_column_is_refused()
        {
            Assert.Equal("columnMappings[0]", Fault([], [null!]).Arguments!["path"]);
            Assert.Equal("columnMappings[0].sheet", Fault([], [new("", "A", "Code")]).Arguments!["path"]);
            Assert.Equal(("columnMappings[0].column", "A1"), Pair(Fault([], [new("Data", "A1", "Code")]), "name"));
            Assert.Equal(("columnMappings[0].column", "XFE"), Pair(Fault([], [new("Data", "XFE", "Code")]), "name"));
            Assert.Equal(("columnMappings[1].column", "b"), Pair(Fault([], [new("Data", "B", "Code"), new("DATA", "b", null)]), "name"));

            // A path is checked against the workbook's sheet, not here.
            Check(1, [], [new("Data", "XFD", "Nope.Nope"), new("Other", "B", "Code")]);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        /// <summary>Resolves a path that must resolve on an entity's parent sheet.</summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <param name="path">The path.</param>
        /// <param name="language">The column's language.</param>
        /// <param name="languages">The tenant's languages.</param>
        /// <returns>The path.</returns>
        private ImportPath Resolve<TEntity>(string path, string? language, params string[] languages)
        {
            return ImportPaths.Resolve(_kit.Entity<TEntity>(), collection: null, path, language, languages).Path
                ?? throw new Xunit.Sdk.XunitException($"The path '{path}' did not resolve.");
        }

        /// <summary>Resolves a path that must resolve, for a tenant of English alone.</summary>
        /// <param name="entity">The sheet's entity.</param>
        /// <param name="collection">The sheet's collection.</param>
        /// <param name="path">The path.</param>
        /// <returns>The path.</returns>
        private static ImportPath Resolve(EntityMetadata entity, ChildCollectionMetadata? collection, string path)
        {
            return ImportPaths.Resolve(entity, collection, path, language: null, ["en"]).Path
                ?? throw new Xunit.Sdk.XunitException($"The path '{path}' did not resolve.");
        }

        /// <summary>Checks a request against the widget.</summary>
        /// <param name="fileId">The file id.</param>
        /// <param name="sheets">The sheet overrides.</param>
        /// <param name="columns">The column overrides.</param>
        private void Check(int fileId, ImportSheetMapping[]? sheets, ImportColumnMapping[]? columns)
        {
            ImportRequests.Check(_kit.Entity<Widget>(), fileId, sheets, columns);
        }

        /// <summary>The fault of a request of the widget's that is refused.</summary>
        /// <param name="sheets">The sheet overrides.</param>
        /// <param name="columns">The column overrides.</param>
        /// <returns>The fault.</returns>
        private BadRequestException Fault(ImportSheetMapping[] sheets, ImportColumnMapping[] columns)
        {
            return Assert.Throws<BadRequestException>(() => Check(1, sheets, columns));
        }

        /// <summary>A fault's path and one more argument.</summary>
        /// <param name="fault">The fault.</param>
        /// <param name="argument">The argument.</param>
        /// <returns>The pair.</returns>
        private static (object? Path, object? Argument) Pair(BadRequestException fault, string argument)
        {
            return (fault.Arguments["path"], fault.Arguments[argument]);
        }
    }
}
