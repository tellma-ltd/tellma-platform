// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Tests.Emit
{
    /// <summary>
    ///     A key-set restriction on a text column that declares its own collation: the host's table
    ///     holds keys in the database's collation, so its keys are converted to the column's.
    /// </summary>
    /// <remarks>
    ///     Without the conversion the backend refuses the comparison as a collation conflict. The
    ///     conversion sits on the table's side, so the column itself is compared bare.
    /// </remarks>
    public sealed class CollatedKeySetTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>The table's keys are converted to the restricted column's collation.</summary>
        [Fact]
        public void ARestrictionOnACollatedColumn_ConvertsTheTablesKeysToItsCollation()
        {
            (QueryexSchema schema, EntityDescriptor user) = Schema();

            CompiledQuery compiled = Compile(schema, user, new KeySetRestriction("Subject", "@subjects"));

            Assert.Contains("([T].[Subject] IN (SELECT [Id] COLLATE Latin1_General_100_BIN2 FROM @subjects))", compiled.Sql, StringComparison.Ordinal);
        }

        /// <summary>A column in the database's collation is compared as it was.</summary>
        [Fact]
        public void ARestrictionOnAColumnOfTheDatabasesCollation_IsWrittenAsBefore()
        {
            (QueryexSchema schema, EntityDescriptor user) = Schema();

            CompiledQuery compiled = Compile(schema, user, new KeySetRestriction("Name", "@names"));

            Assert.Contains("([T].[Name] IN (SELECT [Id] FROM @names))", compiled.Sql, StringComparison.Ordinal);
        }

        /// <summary>The restriction reaches a collated column through a navigation as well.</summary>
        [Fact]
        public void ARestrictionThroughANavigation_ReadsTheTargetsCollation()
        {
            (QueryexSchema schema, _) = Schema();

            CompiledQuery compiled = Compile(schema, schema.FindEntity("fx.Session")!, new KeySetRestriction("User.Subject", "@subjects"));

            Assert.Contains("([P1].[Subject] IN (SELECT [Id] COLLATE Latin1_General_100_BIN2 FROM @subjects))", compiled.Sql, StringComparison.Ordinal);
        }

        /// <summary>Only a text property declares a collation, and only a bare collation name.</summary>
        /// <param name="type">The property's type.</param>
        /// <param name="collation">The collation.</param>
        [Theory]
        [InlineData(QueryexType.QxNumeric, "Latin1_General_100_BIN2")]
        [InlineData(QueryexType.QxString, "")]
        [InlineData(QueryexType.QxString, "Latin1_General_100_BIN2 FROM x; --")]
        [InlineData(QueryexType.QxString, "[Latin1_General_100_BIN2]")]
        public void ACollationOnAPropertyThatIsNotTextOrThatIsNotAName_IsRefused(QueryexType type, string collation)
        {
            QueryexSchemaBuilder.EntityBuilder entity = new QueryexSchemaBuilder().Entity("fx.User", "[fx].[Users]").Key("Id", QueryexType.QxNumeric, "Id");

            ArgumentException refused = Assert.Throws<ArgumentException>(() => entity.Property("Subject", type, "Subject", collation: collation));

            Assert.Equal("collation", refused.ParamName);
        }

        /// <summary>A property declares no collation unless the host gives one.</summary>
        [Fact]
        public void AProperty_TakesTheDatabasesCollationByDefault()
        {
            (_, EntityDescriptor user) = Schema();

            Assert.Null(user.Properties.Single(static property => property.Name == "Name").Collation);
            Assert.Equal("Latin1_General_100_BIN2", user.Properties.Single(static property => property.Name == "Subject").Collation);
        }

        /// <summary>A user with a binary-collated subject and a name in the database's collation, and a session that points at it.</summary>
        /// <returns>The schema and the user entity.</returns>
        private static (QueryexSchema Schema, EntityDescriptor User) Schema()
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity("fx.User", "[fx].[Users]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Subject", QueryexType.QxString, "Subject", isNotNull: true, isUnique: true, collation: "Latin1_General_100_BIN2")
                .Property("Name", QueryexType.QxString, "Name", isNotNull: true);
            _ = builder.Entity("fx.Session", "[fx].[Sessions]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("UserId", QueryexType.QxNumeric, "UserId", isNotNull: true)
                .Navigation("User", "fx.User", "UserId");

            QueryexSchema schema = builder.Build();
            return (schema, schema.FindEntity("fx.User")!);
        }

        /// <summary>Compiles a key-only select restricted on one path, failing the test on any diagnostic.</summary>
        /// <param name="schema">The schema.</param>
        /// <param name="root">The root entity.</param>
        /// <param name="restriction">The restriction.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compile(QueryexSchema schema, EntityDescriptor root, KeySetRestriction restriction)
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec { Root = root, Select = "Id", Restrictions = [restriction] },
                new QueryCompilationOptions { Schema = schema, QueryexVersion = QueryexLanguage.Version });

            Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Select(static diagnostic => diagnostic.Code)));
            return result.Value;
        }
    }
}
