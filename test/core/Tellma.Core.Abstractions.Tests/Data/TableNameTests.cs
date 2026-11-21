// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Tests.Data
{
    /// <summary>A table by schema and name: equality, its T-SQL form and the parts it refuses.</summary>
    public class TableNameTests
    {
        [Fact]
        public void A_table_name_compares_by_value_and_renders_delimited_and_plain()
        {
            TableName users = new("core", "Users");

            Assert.Equal(new TableName("core", "Users"), users);
            Assert.NotEqual(new TableName("core", "users"), users);
            Assert.Equal("[core].[Users]", users.Delimited);
            Assert.Equal("core.Users", users.ToString());
        }

        [Fact]
        public void A_closing_bracket_in_a_part_is_doubled()
        {
            Assert.Equal("[odd]]schema].[a]]b]", new TableName("odd]schema", "a]b").Delimited);
        }

        [Theory]
        [InlineData(null, "Users")]
        [InlineData("", "Users")]
        [InlineData("core", " ")]
        public void An_empty_part_is_refused(string? schema, string name)
        {
            _ = Assert.ThrowsAny<ArgumentException>(() => new TableName(schema!, name));
        }
    }
}
