// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using System.Data;
using System.Globalization;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.Tests.Data.Model;

namespace Tellma.Core.Tests.Data
{
    /// <summary>
    ///     The table-type binder: metadata from the model's definitions in their column order, values
    ///     matched by column name, enums through their string conversion, and lazily streamed records.
    /// </summary>
    public class TableTypeBinderTests(DataModel model) : IClassFixture<DataModel>
    {
        private TableTypeBinder Binder { get; } = new(model.RuntimeModel);

        [Fact]
        public void An_entitys_metadata_follows_its_row_image_column_by_column()
        {
            TableTypeBinding binding = Binder.Binding(typeof(Widget));
            TableTypeDefinition definition = model.RuntimeModel.FindEntityType(typeof(Widget))!.GetTableType()!;

            Assert.Equal(definition.Columns.Select(static column => column.Name), binding.Metadata.Select(static metadata => metadata.Name));
            Assert.Equal($"[fixture].[{definition.PhysicalName}]", binding.TypeName);
        }

        [Theory]
        [InlineData(nameof(Widget.Id), SqlDbType.Int, 0L)]
        [InlineData(nameof(Widget.Kind), SqlDbType.VarChar, 8L)]
        [InlineData(nameof(Widget.Name), SqlDbType.NVarChar, 255L)]
        [InlineData(nameof(Widget.Settings), SqlDbType.NVarChar, -1L)]
        [InlineData(nameof(Widget.IsActive), SqlDbType.Bit, 0L)]
        [InlineData(nameof(Widget.ImageId), SqlDbType.Int, 0L)]
        public void Each_column_binds_its_store_type(string column, SqlDbType type, long maxLength)
        {
            SqlMetaData metadata = Binder.Binding(typeof(Widget)).Metadata.Single(metadata => metadata.Name == column);

            Assert.Equal(type, metadata.SqlDbType);
            if (maxLength != 0)
            {
                Assert.Equal(maxLength, metadata.MaxLength);
            }
        }

        [Fact]
        public void The_audit_stamps_bind_as_datetimeoffset_with_seven_digits()
        {
            SqlMetaData modifiedAt = Binder.Binding(typeof(Widget)).Metadata.Single(static metadata => metadata.Name == nameof(Widget.ModifiedAt));

            Assert.Equal((SqlDbType.DateTimeOffset, (byte)7), (modifiedAt.SqlDbType, modifiedAt.Scale));
        }

        [Fact]
        public void A_decimal_binds_its_precision_and_scale_and_a_long_key_binds_as_bigint()
        {
            SqlMetaData price = Binder.Binding(typeof(Widget)).Metadata.Single(static metadata => metadata.Name == nameof(Widget.Price));
            SqlMetaData id = Binder.Binding(typeof(Shipment)).Metadata.Single(static metadata => metadata.Name == nameof(Shipment.Id));

            Assert.Equal((SqlDbType.Decimal, (byte)19, (byte)4), (price.SqlDbType, price.Precision, price.Scale));
            Assert.Equal(SqlDbType.BigInt, id.SqlDbType);
        }

        [Fact]
        public void Text_columns_declare_the_platform_locale_whatever_the_threads_culture()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
                TableTypeBinder binder = new(model.RuntimeModel);

                Assert.All(
                    binder.Binding(typeof(Widget)).Metadata.Where(static metadata => metadata.SqlDbType is SqlDbType.VarChar or SqlDbType.NVarChar),
                    static metadata => Assert.Equal(1033L, metadata.LocaleId));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void A_row_binds_its_values_by_column_name_with_enums_as_names_and_nulls_as_null()
        {
            DateTimeOffset stamp = new(2026, 11, 8, 9, 30, 0, 123, TimeSpan.Zero);
            Widget widget = new()
            {
                Id = 1001,
                Code = "W-1",
                Kind = WidgetKind.Fancy,
                OptionalKind = null,
                Status = LongStatus.PendingConfirmationByTheOwner,
                Access = AccessRights.Read | AccessRights.Write,
                Name = "Gadget",
                Subject = "subject-1",
                Price = 12.5m,
                CreatedAt = stamp,
                ModifiedAt = stamp,
                CreatedById = 7,
                ModifiedById = 8,
            };

            SqlDataRecord record = Assert.Single(Binder.Records([widget]));

            Assert.Equal(1001, record[nameof(Widget.Id)]);
            Assert.Equal("W-1", record[nameof(Widget.Code)]);
            Assert.Equal("Fancy", record[nameof(Widget.Kind)]);
            Assert.Equal(DBNull.Value, record[nameof(Widget.OptionalKind)]);
            Assert.Equal("PendingConfirmationByTheOwner", record[nameof(Widget.Status)]);
            Assert.Equal("Read, Write", record[nameof(Widget.Access)]);
            Assert.Equal(12.5m, record[nameof(Widget.Price)]);
            Assert.Equal(stamp, record[nameof(Widget.ModifiedAt)]);
            Assert.Equal(true, record[nameof(Widget.IsActive)]);
            Assert.Equal(DBNull.Value, record[nameof(Widget.ImageId)]);
        }

        [Fact]
        public void Records_are_created_lazily_and_again_on_every_enumeration()
        {
            Widget widget = new() { Id = 1, Name = "One", Subject = "s" };
            IEnumerable<SqlDataRecord> records = Binder.Records([widget]);

            // Nothing is read until the driver enumerates, so a change before then is what binds,
            // and a second enumeration (a retry) reads the rows again.
            widget.Name = "Changed";
            Assert.Equal("Changed", Assert.Single(records)[nameof(Widget.Name)]);
            widget.Name = "Again";
            Assert.Equal("Again", Assert.Single(records)[nameof(Widget.Name)]);
        }

        [Fact]
        public void A_default_resolves_to_the_leaf_that_replaces_it()
        {
            TableTypeBinding binding = Binder.Binding(typeof(Center));

            Assert.Equal(typeof(MyCenter), binding.RowType);
            Assert.Contains(binding.Metadata, static metadata => metadata.Name == nameof(MyCenter.Region));
            Assert.Throws<InvalidOperationException>(() => binding.Records([new Center()]).ToList());
        }

        [Fact]
        public void A_standalone_class_binds_through_the_type_registered_for_it()
        {
            TableTypeBinding binding = Binder.Binding(typeof(IdList));

            Assert.Equal("Id", Assert.Single(binding.Metadata).Name);
            Assert.StartsWith("[dbo].[IdList_", binding.TypeName, StringComparison.Ordinal);
            Assert.Equal([1, 2, 3], Binder.Records<IdList>([new() { Id = 1 }, new() { Id = 2 }, new() { Id = 3 }]).Select(static record => (int)record[0]).ToList());
        }

        [Fact]
        public void A_standalone_enum_binds_by_name_and_a_date_as_a_date()
        {
            CollatedKeyRow row = new() { Key = "k", Status = LongStatus.Active, OptionalKind = WidgetKind.Plain, On = new DateOnly(2026, 11, 8), Scratch = "ignored" };

            SqlDataRecord record = Assert.Single(Binder.Records([row]));

            Assert.Equal(5, record.FieldCount);
            Assert.Equal(("k", "Active", "Plain"), (record["Key"], record["Status"], record["OptionalKind"]));
            Assert.Equal(new DateTime(2026, 11, 8), record["On"]);
            Assert.Equal(SqlDbType.Date, Binder.Binding(typeof(CollatedKeyRow)).Metadata.Single(static metadata => metadata.Name == "On").SqlDbType);
        }

        [Fact]
        public void A_timestamp_binds_with_three_digits_unless_declared_otherwise()
        {
            TableTypeBinding widgets = Binder.Binding(typeof(Widget));

            Assert.Equal((byte)3, widgets.Metadata.Single(static metadata => metadata.Name == nameof(Widget.DueAt)).Scale);
            Assert.Equal((byte)7, widgets.Metadata.Single(static metadata => metadata.Name == nameof(Widget.PreciseAt)).Scale);
            Assert.Equal((byte)3, Binder.Binding(typeof(CollatedKeyRow)).Metadata.Single(static metadata => metadata.Name == nameof(CollatedKeyRow.At)).Scale);
        }

        [Fact]
        public void A_type_name_doubles_a_closing_bracket()
        {
            TableTypeBinding binding = new(typeof(IdList), new TableTypeDefinition { Name = "Odd]List", Schema = "a]b" }, [], []);

            Assert.StartsWith("[a]]b].[Odd]]List_", binding.TypeName, StringComparison.Ordinal);
        }

        [Fact]
        public void A_stamp_list_binds_its_seven_digit_stamp()
        {
            SqlMetaData modifiedAt = Binder.Binding(typeof(IdStampList)).Metadata.Single(static metadata => metadata.Name == nameof(IdStampList.ModifiedAt));

            Assert.Equal((SqlDbType.DateTimeOffset, (byte)7), (modifiedAt.SqlDbType, modifiedAt.Scale));
        }

        [Fact]
        public void A_parameter_names_the_physical_type_and_binds_no_value_for_no_rows()
        {
            SqlParameter rows = Binder.Parameter<IdList>("@ids", [new() { Id = 5 }]);
            SqlParameter none = Binder.Parameter<IdList>("@none", []);

            Assert.Equal((SqlDbType.Structured, Binder.Binding(typeof(IdList)).TypeName), (rows.SqlDbType, rows.TypeName));
            Assert.IsAssignableFrom<IEnumerable<SqlDataRecord>>(rows.Value);
            Assert.Null(none.Value);
        }

        [Fact]
        public void A_class_the_model_has_no_type_for_is_refused_with_the_remedy()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Binder.Binding(typeof(UnregisteredRow)));

            Assert.Contains("contribution.TableType<UnregisteredRow>()", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_entity_without_a_table_type_is_refused()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Binder.Binding(typeof(WidgetExtra)));

            Assert.Contains("[TableType]", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("nvarchar(max)", "nvarchar", null, null, true)]
        [InlineData("decimal(19,4)", "decimal", 19, 4, false)]
        [InlineData("datetimeoffset(3)", "datetimeoffset", 3, null, false)]
        [InlineData("INT", "int", null, null, false)]
        [InlineData("varchar( 50 )", "varchar", 50, null, false)]
        public void Store_types_parse_into_their_base_and_facets(string storeType, string baseType, int? first, int? second, bool max)
        {
            Assert.Equal((baseType, first, second, max), StoreTypeMetadata.Parse(storeType));
        }

        [Theory]
        [InlineData("hierarchyid", null)]
        [InlineData("varchar(10)", "Arabic_CI_AS")]
        [InlineData("varchar(max)", "Latin1_General_100_BIN2_UTF8")]
        public void Columns_a_parameter_cannot_carry_faithfully_are_refused(string storeType, string? collation)
        {
            TableTypeDefinition definition = new() { Name = "Probe", Schema = "dbo" };
            TableTypeColumnDefinition column = new() { Name = "Value", StoreType = storeType, Collation = collation };

            Assert.Throws<NotSupportedException>(() => StoreTypeMetadata.For(definition, column));
        }

        [Fact]
        public void A_latin1_collated_varchar_and_a_collated_nvarchar_bind()
        {
            TableTypeDefinition definition = new() { Name = "Probe", Schema = "dbo" };

            Assert.Equal(SqlDbType.VarChar, StoreTypeMetadata.For(definition, new() { Name = "A", StoreType = "varchar(10)", Collation = "Latin1_General_100_BIN2" }).SqlDbType);
            Assert.Equal(SqlDbType.NVarChar, StoreTypeMetadata.For(definition, new() { Name = "B", StoreType = "nvarchar(10)", Collation = "Arabic_CI_AS" }).SqlDbType);
        }

        /// <summary>A class no feature contributed.</summary>
        public sealed class UnregisteredRow
        {
            public int Id { get; set; }
        }
    }
}
