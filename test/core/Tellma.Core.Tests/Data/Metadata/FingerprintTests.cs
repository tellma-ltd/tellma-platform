// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.Data;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Tests.Data.Metadata.Fingerprinted;

namespace Tellma.Core.Tests.Data.Metadata
{
    /// <summary>
    ///     The schema fingerprint of an entity and the storage fingerprint of a model: stable for equal
    ///     inputs, pinned to fixed values, and changed by exactly the changes they guard.
    /// </summary>
    public class FingerprintTests
    {
        private const string LowerHex64 = "^[0-9a-f]{64}$";

        // --- Schema fingerprints ---

        [Fact]
        public void A_schema_fingerprint_is_lower_hex_and_pinned()
        {
            string fingerprint = Shapes().Get(typeof(Shape)).SchemaFingerprint;

            Assert.Matches(LowerHex64, fingerprint);
            Assert.Equal("ad5452afbca2cea42cf9490f951ef9868860968e3c3b2037a976831ac9e3c902", fingerprint);
        }

        [Fact]
        public void A_schema_fingerprint_is_the_same_across_model_builds_and_classes_of_one_shape()
        {
            EntityMetadataProvider first = Shapes();
            EntityMetadataProvider second = Shapes();

            Assert.Equal(first.Get(typeof(Shape)).SchemaFingerprint, second.Get(typeof(Shape)).SchemaFingerprint);
            Assert.Equal(first.Get(typeof(Shape)).SchemaFingerprint, first.Get(typeof(SameShape)).SchemaFingerprint);
        }

        [Fact]
        public void A_schema_fingerprint_changes_with_an_editable_column_and_with_a_childs_shape()
        {
            EntityMetadataProvider shapes = Shapes();
            string reference = shapes.Get(typeof(Shape)).SchemaFingerprint;

            Assert.NotEqual(reference, shapes.Get(typeof(NullableShape)).SchemaFingerprint);
            Assert.NotEqual(reference, shapes.Get(typeof(ChildShape)).SchemaFingerprint);
            Assert.NotEqual(shapes.Get(typeof(ShapeLine)).SchemaFingerprint, shapes.Get(typeof(ChildShapeLine)).SchemaFingerprint);
        }

        [Fact]
        public void A_schema_fingerprint_ignores_what_an_import_never_writes()
        {
            EntityMetadataProvider shapes = Shapes();

            Assert.Equal(shapes.Get(typeof(Shape)).SchemaFingerprint, shapes.Get(typeof(OwnedShape)).SchemaFingerprint);
        }

        // --- Storage fingerprints ---

        [Fact]
        public void A_storage_fingerprint_is_lower_hex_and_pinned()
        {
            string fingerprint = Storage(typeof(Store));

            Assert.Matches(LowerHex64, fingerprint);
            Assert.Equal("41c91f47af4bbebfa57f7c344014ce951ae50a959a2665224adc3d9b6dfb169d", fingerprint);
        }

        [Fact]
        public void A_storage_fingerprint_depends_on_the_storage_alone()
        {
            Assert.Equal(Storage(typeof(Store)), Storage(typeof(Store)));
            Assert.Equal(Storage(typeof(Store)), Storage(typeof(StoreTwin)));
        }

        [Theory]
        [InlineData(typeof(StoreWithPhone))]
        [InlineData(typeof(StoreNullable))]
        [InlineData(typeof(StoreIndexed))]
        public void A_storage_fingerprint_changes_with_a_column_its_nullability_or_an_index(Type variant)
        {
            Assert.NotEqual(Storage(typeof(Store)), Storage(variant));
        }

        [Fact]
        public void A_storage_fingerprint_changes_with_a_columns_default()
        {
            string defaulted = MetadataModels.Build([typeof(StoreDefaulted)], [typeof(StoreDefaultedConfiguration)]).Provider!.StorageFingerprint;

            Assert.NotEqual(Storage(typeof(Store)), defaulted);
        }

        [Fact]
        public void A_storage_fingerprint_changes_with_a_table_type_alone()
        {
            Assert.NotEqual(Storage(typeof(StoreWithScratch)), Storage(typeof(StoreScratchExcluded)));
        }

        [Fact]
        public void The_migrators_per_database_context_fingerprints_the_hosts_model()
        {
            var definition = TellmaModelDefinition.Create([.. MetadataModels.PlatformStandIns, typeof(Store)], [], [], new Dictionary<Type, Type>());
            TellmaModelFactory factory = new(definition, MetadataModels.Deployment);

            string host = MetadataModels.Build(definition).Provider!.StorageFingerprint;
            using TellmaDbContext migrator = factory.CreateContext("Server=(local);Database=TellmaFingerprint;Integrated Security=true;TrustServerCertificate=true");

            Assert.Equal(host, Fingerprints.Storage(migrator.Model));
            Assert.Equal(host, Fingerprints.Storage(migrator.GetService<IDesignTimeModel>().Model));
        }

        /// <summary>The metadata of the schema-fingerprint shapes, all in one model.</summary>
        /// <returns>The provider.</returns>
        private static EntityMetadataProvider Shapes()
        {
            EntityMetadataBuild build = MetadataModels.Build(typeof(Shape), typeof(SameShape), typeof(NullableShape), typeof(OwnedShape), typeof(ChildShape));
            Assert.Empty(build.Problems);
            return build.Provider!;
        }

        /// <summary>The storage fingerprint of the stand-ins and the given entities.</summary>
        /// <param name="entities">The entities.</param>
        /// <returns>The fingerprint.</returns>
        private static string Storage(params Type[] entities)
        {
            EntityMetadataBuild build = MetadataModels.Build(entities);
            Assert.Empty(build.Problems);
            return build.Provider!.StorageFingerprint;
        }
    }
}
