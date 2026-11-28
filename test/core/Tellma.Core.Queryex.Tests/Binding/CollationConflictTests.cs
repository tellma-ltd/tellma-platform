// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Tests.Binding
{
    /// <summary>
    ///     Text from columns of different collations meeting in one operation: refused when the query
    ///     compiles, against the operation, rather than by the backend when it runs.
    /// </summary>
    public sealed class CollationConflictTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>A binary-collated column meets one in the database's collation, at any depth.</summary>
        /// <param name="filter">The filter.</param>
        /// <param name="start">Where the refused operation starts.</param>
        /// <param name="length">How long it is.</param>
        [Theory]
        [InlineData("Subject = Email", 0, 15)]
        [InlineData("Email != Subject", 0, 16)]
        [InlineData("Subject in ('a', Email)", 0, 23)]
        [InlineData("upper(Subject) = Email", 0, 22)]
        [InlineData("Subject || Email = 'ab'", 0, 16)]
        [InlineData("contains(Email, Subject)", 0, 24)]
        [InlineData("Code = 'x' and coalesce(Subject, Email) = 'a'", 15, 24)]
        public void AnOperationOverColumnsOfDifferentCollations_IsRefusedAtTheOperation(string filter, int start, int length)
        {
            QueryexResult<CompiledQuery> result = Compile("fx.User", "Id", filter);

            QueryexDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal(("QX3203", start, length), (diagnostic.Code, diagnostic.Span.Start, diagnostic.Span.Length));
        }

        /// <summary>The same columns in a select item and through a navigation.</summary>
        [Fact]
        public void ASelectItemAndAPathThroughANavigation_AreCheckedAlike()
        {
            Assert.Equal("QX3203", Assert.Single(Compile("fx.User", "if(Code = 'x', Subject, Email)", null).Diagnostics).Code);
            Assert.Equal("QX3203", Assert.Single(Compile("fx.Session", "Id", "User.Subject = User.Email").Diagnostics).Code);
        }

        /// <summary>Text of one collation, or text that carries none, combines freely.</summary>
        /// <param name="filter">The filter.</param>
        [Theory]
        [InlineData("Subject = 'a'")]
        [InlineData("Subject = upper(Subject)")]
        [InlineData("Email = Code")]
        [InlineData("coalesce(Email, Code, 'x') = 'x'")]
        [InlineData("User.Subject = User.Subject")]
        public void TextOfOneCollationOrOfNone_Combines(string filter)
        {
            QueryexResult<CompiledQuery> result = Compile(filter.StartsWith("User.", StringComparison.Ordinal) ? "fx.Session" : "fx.User", "Id", filter);

            Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Select(static diagnostic => diagnostic.Code)));
        }

        /// <summary>
        ///     Compiles a query over a user with a binary-collated subject, an email and a code in the
        ///     database's collation, and a session that points at it.
        /// </summary>
        /// <param name="root">The root entity.</param>
        /// <param name="select">The select list.</param>
        /// <param name="filter">The filter, or null.</param>
        /// <returns>The result.</returns>
        private static QueryexResult<CompiledQuery> Compile(string root, string select, string? filter)
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity("fx.User", "[fx].[Users]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Subject", QueryexType.QxString, "Subject", collation: "Latin1_General_100_BIN2")
                .Property("Email", QueryexType.QxString, "Email")
                .Property("Code", QueryexType.QxString, "Code");
            _ = builder.Entity("fx.Session", "[fx].[Sessions]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("UserId", QueryexType.QxNumeric, "UserId", isNotNull: true)
                .Navigation("User", "fx.User", "UserId");
            QueryexSchema schema = builder.Build();

            return Engine.CompileQuery(
                new QuerySpec { Root = schema.FindEntity(root)!, Select = select, Filter = filter is null ? null : FilterTree.Leaf(filter) },
                new QueryCompilationOptions { Schema = schema, QueryexVersion = QueryexLanguage.Version });
        }
    }
}
