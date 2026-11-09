// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Tests.Data
{
    /// <summary>The parameter and table-variable names the platform reserves in a batch's SQL text.</summary>
    public class ReservedSqlNamesTests
    {
        [Fact]
        public void The_three_prefixes_are_reserved()
        {
            Assert.Equal(["@qx", "@tb", "@tm"], ReservedSqlNames.Prefixes);
            Assert.Equal(("@qx", "@tb", "@tm_"), (ReservedSqlNames.EnginePrefix, ReservedSqlNames.StatementPrefix, ReservedSqlNames.BatchPrefix));
        }

        [Theory]
        [InlineData("@qx0_p1", true)]
        [InlineData("@QX3_skip", true)]
        [InlineData("@tb2_t0", true)]
        [InlineData("@TB2_now", true)]
        [InlineData("@tm_schema", true)]
        [InlineData("@tm_userIds_UserId", true)]
        [InlineData("@tmx", true)]
        [InlineData("@total", false)]
        [InlineData("@t", false)]
        [InlineData("@ids", false)]
        [InlineData("@q", false)]
        public void A_name_in_a_reserved_namespace_is_reported_ignoring_case(string name, bool reserved)
        {
            Assert.Equal(reserved, ReservedSqlNames.IsReserved(name));
        }

        [Fact]
        public void The_once_per_batch_names_are_spelled_as_the_statements_use_them()
        {
            Assert.Equal(
                ["@tm_schema", "@tm_UserId", "@tm_Guard", "@tm_expectedTags", "@tm_tag", "@tm_tagNames", "@tm_callerIds", "@tm_userIds_", "@tm_visible"],
                [
                    ReservedSqlNames.SchemaFingerprint, ReservedSqlNames.UserId, ReservedSqlNames.Guard, ReservedSqlNames.ExpectedTags,
                    ReservedSqlNames.Tag, ReservedSqlNames.TagNames, ReservedSqlNames.CallerIds, ReservedSqlNames.UserIdsPrefix, ReservedSqlNames.Visible,
                ]);
            Assert.All(
                [ReservedSqlNames.SchemaFingerprint, ReservedSqlNames.Guard, ReservedSqlNames.Visible],
                static name => Assert.True(ReservedSqlNames.IsReserved(name)));
        }

        [Fact]
        public void A_statement_name_joins_the_prefix_the_ordinal_and_the_name()
        {
            Assert.Equal("@tb0_now", ReservedSqlNames.Statement(0, ReservedSqlNames.StatementNames.Now));
            Assert.Equal("@tb12_t3", ReservedSqlNames.Statement(12, ReservedSqlNames.StatementNames.TableValued(3)));
            Assert.Equal("@tb1_p0", ReservedSqlNames.Statement(1, ReservedSqlNames.StatementNames.Scalar(0)));
            Assert.Equal("@tb4_cap2", ReservedSqlNames.Statement(4, ReservedSqlNames.StatementNames.Capture(2)));
            Assert.Equal("@tb5_blob_gl_Centers_ImageId", ReservedSqlNames.Statement(5, ReservedSqlNames.StatementNames.BlobCapture("gl", "Centers", "ImageId")));
        }

        [Fact]
        public void The_statement_names_are_spelled_as_the_statements_use_them()
        {
            Assert.Equal(
                ["now", "keys", "ids", "new", "saved", "touched", "conflicts", "msg", "aff", "old", "paths", "anc", "first", "last", "claimed", "due", "fired", "jobIds"],
                [
                    ReservedSqlNames.StatementNames.Now, ReservedSqlNames.StatementNames.Keys, ReservedSqlNames.StatementNames.Ids,
                    ReservedSqlNames.StatementNames.New, ReservedSqlNames.StatementNames.Saved, ReservedSqlNames.StatementNames.Touched,
                    ReservedSqlNames.StatementNames.Conflicts, ReservedSqlNames.StatementNames.Message, ReservedSqlNames.StatementNames.Affected,
                    ReservedSqlNames.StatementNames.Old, ReservedSqlNames.StatementNames.Paths, ReservedSqlNames.StatementNames.Ancestors,
                    ReservedSqlNames.StatementNames.First, ReservedSqlNames.StatementNames.Last, ReservedSqlNames.StatementNames.Claimed,
                    ReservedSqlNames.StatementNames.Due, ReservedSqlNames.StatementNames.Fired, ReservedSqlNames.StatementNames.JobIds,
                ]);
        }

        [Fact]
        public void Malformed_arguments_are_refused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ReservedSqlNames.Statement(-1, "now"));
            Assert.Throws<ArgumentException>(() => ReservedSqlNames.Statement(0, ""));
            Assert.Throws<ArgumentOutOfRangeException>(() => ReservedSqlNames.StatementNames.TableValued(-1));
            Assert.Throws<ArgumentNullException>(() => ReservedSqlNames.IsReserved(null!));
        }
    }
}
