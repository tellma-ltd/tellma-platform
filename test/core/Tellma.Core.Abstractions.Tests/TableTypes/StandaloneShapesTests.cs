// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.TableTypes;

namespace Tellma.Core.Abstractions.Tests.TableTypes
{
    /// <summary>The standalone table-type shapes: plain classes the platform maps in schema dbo under their class names.</summary>
    public class StandaloneShapesTests
    {
        [Theory]
        [InlineData(typeof(IdList), typeof(int))]
        [InlineData(typeof(BigIdList), typeof(long))]
        [InlineData(typeof(GuidList), typeof(Guid))]
        [InlineData(typeof(StringList), typeof(string))]
        [InlineData(typeof(DateList), typeof(DateOnly))]
        public void Each_key_list_is_a_table_type_class_with_one_key(Type list, Type key)
        {
            TableTypeAttribute attribute = list.GetCustomAttribute<TableTypeAttribute>()!;
            PropertyInfo id = Assert.Single(list.GetProperties());

            Assert.Equal((null, null), (attribute.Name, attribute.Schema));
            Assert.Equal(("Id", key), (id.Name, id.PropertyType));
            Assert.NotNull(id.GetCustomAttribute<KeyAttribute>());
        }

        [Fact]
        public void The_stamp_list_carries_an_id_and_a_seven_digit_stamp()
        {
            PropertyInfo[] properties = typeof(IdStampList).GetProperties();

            Assert.Equal(["Id", "ModifiedAt"], properties.Select(static property => property.Name));
            Assert.Equal("datetimeoffset(7)", properties[1].GetCustomAttribute<ColumnAttribute>()!.TypeName);
            Assert.NotNull(typeof(IdStampList).GetCustomAttribute<TableTypeAttribute>());
        }

        [Fact]
        public void The_version_tag_list_is_a_table_type_class()
        {
            Assert.NotNull(typeof(VersionTagList).GetCustomAttribute<TableTypeAttribute>());
        }

        [Fact]
        public void The_application_role_is_a_platform_constant()
        {
            Assert.Equal("tellma_app", DatabaseRoles.Application);
        }
    }
}
