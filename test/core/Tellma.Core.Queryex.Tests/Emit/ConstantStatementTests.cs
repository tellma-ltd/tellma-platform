// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Emit
{
    /// <summary>
    ///     Compiling expressions that read no row into the one statement that answers them.
    /// </summary>
    /// <remarks>
    ///     The statements and the refusals are pinned by the stored snapshots and the corpus; these
    ///     pin what those cannot show: the exact statement a host is promised for the simplest case,
    ///     what a caller is refused before anything is read, and what a compilation is remembered
    ///     under.
    /// </remarks>
    public sealed class ConstantStatementTests
    {
        /// <summary>The engine under test, shared because it is stateless apart from its caches.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>The items most tests compile.</summary>
        private static readonly ConstantItem[] Defaults =
        [
            new ConstantItem("startOfMonth(today())", QueryexType.QxDate),
            new ConstantItem("me()", QueryexType.QxNumeric),
        ];

        /// <summary>
        ///     The values a report run defaults to are one select list with no source, reading the
        ///     same slots a query reads, under the statement's own place in the batch.
        /// </summary>
        [Fact]
        public void TheStatement_IsASelectListWithNoSource()
        {
            CompiledQuery statement = Compile(Defaults, Options() with { BatchOrdinal = 1 });

            Assert.Equal(
                """
                SELECT
                    DATEADD(MONTH, DATEDIFF(MONTH, CONVERT(date, '00010101'), @qx1_p0), CONVERT(date, '00010101')) AS [c0],
                    @qx1_p1 AS [c1]
                ;

                """.ReplaceLineEndings("\n"),
                statement.Sql);

            Assert.Equal(string.Empty, statement.Prologue);
            Assert.Equal(
                [("@qx1_p0", QueryexParameterOrigin.Today), ("@qx1_p1", QueryexParameterOrigin.UserId)],
                statement.Parameters.Select(slot => (slot.Name, slot.Origin)));

            Assert.Equal(
                [
                    ("startOfMonth(today())", QueryexType.QxDate, QueryexNullity.NotNull),
                    ("me()", QueryexType.QxNumeric, QueryexNullity.NotNull),
                ],
                statement.Columns.Select(column => (column.Text, column.Type, column.Nullity)));

            Assert.All(statement.Columns, column => Assert.Null(column.Path));
            Assert.All(statement.Columns, column => Assert.False(column.IsGroupingKey));
        }

        /// <summary>
        ///     Without a signed-in user the current-user function can only be absent: its column says
        ///     so, and a comparison against it is settled while compiling.
        /// </summary>
        [Fact]
        public void WithoutAUser_TheCurrentUserIsAbsent()
        {
            ConstantItem[] items =
            [
                new ConstantItem("me()", QueryexType.QxNumeric),
                new ConstantItem("me() = 7", QueryexType.QxBool),
                new ConstantItem("coalesce(me(), 7)", QueryexType.QxNumeric),
            ];

            CompiledQuery anonymous = Compile(items, Options() with { HasUser = false });
            CompiledQuery signedIn = Compile(items, Options());

            Assert.Equal(
                [QueryexNullity.Null, QueryexNullity.NotNull, QueryexNullity.NotNull],
                anonymous.Columns.Select(column => column.Nullity));

            Assert.Equal(
                [QueryexNullity.NotNull, QueryexNullity.NotNull, QueryexNullity.NotNull],
                signedIn.Columns.Select(column => column.Nullity));

            // The comparison never reaches the server: no user can equal anything.
            Assert.Contains("CASE WHEN (1 = 0) THEN 1 ELSE 0 END AS [c1]", anonymous.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain("(1 = 0)", signedIn.Sql, StringComparison.Ordinal);
            Assert.NotEqual(anonymous.Sql, signedIn.Sql);
        }

        /// <summary>A written string is read at the type its item asks for.</summary>
        [Fact]
        public void AStringLiteral_IsReadAtTheItemsType()
        {
            CompiledQuery statement = Compile(
                [
                    new ConstantItem("'2024-03-01'", QueryexType.QxDate),
                    new ConstantItem("'2024-03-01'", QueryexType.QxString),
                ],
                Options());

            Assert.Equal([QueryexType.QxDate, QueryexType.QxString], statement.Columns.Select(column => column.Type));

            // One text, two values: the first is bound as the date it denotes, the second as the
            // characters it is.
            Assert.Equal(QueryexStoreType.QxDate, statement.Parameters[0].StoreType);
            Assert.Equal(new DateOnly(2024, 3, 1), statement.Parameters[0].Value);
            Assert.Equal(QueryexStoreFamily.QxNVarChar, statement.Parameters[1].StoreType.Family);
            Assert.Equal("2024-03-01", statement.Parameters[1].Value);
        }

        /// <summary>A string that does not denote a value of the item's type is refused, where it is.</summary>
        [Fact]
        public void AStringLiteral_ThatIsNotOfTheItemsType_IsRefused()
        {
            QueryexResult<CompiledQuery> result = Engine.EvaluateConstant(
                [
                    new ConstantItem("today()", QueryexType.QxDate),
                    new ConstantItem(" 'yesterday'", QueryexType.QxDate),
                ],
                Options());

            QueryexDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("QX3200", diagnostic.Code);
            Assert.Equal("Constant[1]", diagnostic.Location);
            Assert.Equal(new QueryexSpan(1, 11), diagnostic.Span);
        }

        /// <summary>
        ///     A written absent value takes whichever type its item asks for, the ones no expression
        ///     without a row could otherwise produce included.
        /// </summary>
        /// <param name="type">The item's type.</param>
        /// <param name="rendered">The backend type the absent value is written at.</param>
        [Theory]
        [InlineData(QueryexType.QxNumeric, "decimal(38, 6)")]
        [InlineData(QueryexType.QxString, "nvarchar(4000)")]
        [InlineData(QueryexType.QxGuid, "uniqueidentifier")]
        [InlineData(QueryexType.QxDate, "date")]
        [InlineData(QueryexType.QxDateTime, "datetime2(7)")]
        [InlineData(QueryexType.QxDateTimeOffset, "datetimeoffset(7)")]
        [InlineData(QueryexType.QxHierarchyId, "hierarchyid")]
        [InlineData(QueryexType.QxGeography, "geography")]
        public void AnAbsentValue_TakesTheItemsType(QueryexType type, string rendered)
        {
            CompiledQuery statement = Compile([new ConstantItem("null", type)], Options());

            QueryexColumn column = Assert.Single(statement.Columns);
            Assert.Equal(type, column.Type);
            Assert.Equal(QueryexNullity.Null, column.Nullity);
            Assert.Empty(statement.Parameters);
            Assert.Equal("SELECT\n    CAST(NULL AS " + rendered + ") AS [c0]\n;\n", statement.Sql);
        }

        /// <summary>The statement takes as many items as a list may hold, and no more.</summary>
        [Fact]
        public void TheItemCount_IsBoundedByTheListCeiling()
        {
            ConstantCompilationOptions options = Options() with
            {
                Limits = QueryexLimits.Default with { MaxListItems = 3 },
            };

            ConstantItem item = new("today()", QueryexType.QxDate);

            Assert.Equal(3, Compile([item, item, item], options).Columns.Count);

            ArgumentException thrown = Assert.Throws<ArgumentException>(
                () => Engine.EvaluateConstant([item, item, item, item], options));

            Assert.Equal("items", thrown.ParamName);
        }

        /// <summary>A statement with nothing to answer is the caller's mistake.</summary>
        [Fact]
        public void NoItems_AreRefused()
        {
            ArgumentException thrown = Assert.Throws<ArgumentException>(
                () => Engine.EvaluateConstant([], Options()));

            Assert.Equal("items", thrown.ParamName);
        }

        /// <summary>Arguments that are not there are refused before anything is read.</summary>
        [Fact]
        public void MissingArguments_AreRefused()
        {
            Assert.Throws<ArgumentNullException>(() => Engine.EvaluateConstant(null!, Options()));
            Assert.Throws<ArgumentNullException>(() => Engine.EvaluateConstant(Defaults, null!));

            ArgumentException noItem = Assert.Throws<ArgumentException>(
                () => Engine.EvaluateConstant([Defaults[0], null!], Options()));

            ArgumentException noText = Assert.Throws<ArgumentException>(
                () => Engine.EvaluateConstant([new ConstantItem(null!, QueryexType.QxDate)], Options()));

            Assert.Equal("items", noItem.ParamName);
            Assert.Equal("items", noText.ParamName);
        }

        /// <summary>A type the language does not have is the caller's mistake.</summary>
        [Fact]
        public void AnUndefinedType_IsRefused()
        {
            ArgumentException thrown = Assert.Throws<ArgumentException>(
                () => Engine.EvaluateConstant([new ConstantItem("1", (QueryexType)999)], Options()));

            Assert.Equal("items", thrown.ParamName);
        }

        /// <summary>A statement cannot sit before the first place in a batch.</summary>
        [Fact]
        public void ANegativeBatchOrdinal_IsRefused()
        {
            ArgumentException thrown = Assert.Throws<ArgumentException>(
                () => Engine.EvaluateConstant(Defaults, Options() with { BatchOrdinal = -1 }));

            Assert.Equal("options", thrown.ParamName);
        }

        /// <summary>The options refuse what no compilation could use, when they are built.</summary>
        [Fact]
        public void UnusableOptions_AreRefusedWhenBuilt()
        {
            Assert.Throws<ArgumentNullException>(() => Options() with { Schema = null! });
            Assert.Throws<ArgumentNullException>(() => Options() with { Limits = null! });
            Assert.Throws<ArgumentOutOfRangeException>(
                () => Options() with { QueryexVersion = QueryexLanguage.Version + 1 });
        }

        /// <summary>The same items under the same options are compiled once.</summary>
        [Fact]
        public void TheSameStatement_IsCompiledOnce()
        {
            QueryexEngine engine = new();
            ConstantItem[] again = [.. Defaults.Select(item => new ConstantItem(item.Text, item.Type))];

            CompiledQuery first = Compile(engine, Defaults, Options());
            CompiledQuery second = Compile(engine, again, Options());

            // The very same object: the second call found the first one's compilation.
            Assert.Same(first, second);
        }

        /// <summary>
        ///     A statement is remembered under everything it depends on: each item's text and type,
        ///     their order, and every option.
        /// </summary>
        [Fact]
        public void EverythingTheStatementDependsOn_IsPartOfTheKey()
        {
            ConstantCompilationOptions options = Options();

            Assert.True(ProbeCacheKeys.ShareConstantEntry(Defaults, options, [.. Defaults], Options()));

            // The items: a text, a type, the order, and how many there are.
            Assert.False(Shares([new ConstantItem("startOfMonth(today()) ", QueryexType.QxDate), Defaults[1]], options));
            Assert.False(Shares([new ConstantItem("startOfMonth(today())", QueryexType.QxDateTime), Defaults[1]], options));
            Assert.False(Shares([Defaults[1], Defaults[0]], options));
            Assert.False(Shares([Defaults[0]], options));
            Assert.False(Shares([Defaults[0], Defaults[1], Defaults[1]], options));

            // Two items are never one item whose text happens to hold both.
            Assert.False(ProbeCacheKeys.ShareConstantEntry(
                [new ConstantItem("1", QueryexType.QxNumeric), new ConstantItem("2", QueryexType.QxNumeric)],
                options,
                [new ConstantItem("1, 2", QueryexType.QxNumeric)],
                options));

            // The options.
            Assert.False(Shares(Defaults, options with { HasUser = false }));
            Assert.False(Shares(Defaults, options with { BatchOrdinal = 1 }));
            Assert.False(Shares(Defaults, options with { Limits = QueryexLimits.Default with { MaxParameters = 7 } }));
            Assert.False(Shares(Defaults, options with { Schema = new QueryexSchemaBuilder().Build() }));
        }

        /// <summary>A constant statement and a query are never mistaken for one another.</summary>
        [Fact]
        public void AStatementAndAQuery_NeverShareAnEntry()
        {
            Assert.False(ProbeCacheKeys.ShareEntryAcrossKinds(
                [new ConstantItem("Id", QueryexType.QxNumeric)],
                Options(),
                new QuerySpec { Root = LedgerFixture.Invoice, Select = "Id" },
                new QueryCompilationOptions
                {
                    QueryexVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                }));
        }

        /// <summary>
        ///     What the caller does with its list after the call changes nothing about what was
        ///     compiled or what a later call is served.
        /// </summary>
        [Fact]
        public void TheItems_AreReadOnceAtTheCall()
        {
            QueryexEngine engine = new();
            List<ConstantItem> items = [.. Defaults];

            CompiledQuery first = Compile(engine, items, Options());
            items[1] = new ConstantItem("now()", QueryexType.QxDateTimeOffset);
            CompiledQuery changed = Compile(engine, items, Options());

            Assert.Equal(2, first.Columns.Count);
            Assert.Equal("me()", first.Columns[1].Text);
            Assert.Equal("now()", changed.Columns[1].Text);
            Assert.Same(first, Compile(engine, Defaults, Options()));
        }

        /// <summary>
        ///     Whatever the text and whatever type is asked of it, an item is answered or refused
        ///     with a problem against that item — never with an exception.
        /// </summary>
        [Fact]
        public void AnyText_AtAnyType_IsCompiledOrRefused()
        {
            QueryexEngine engine = new();
            foreach (ExpressionCase entry in ExpressionCorpus.All)
            {
                foreach (QueryexType type in Enum.GetValues<QueryexType>())
                {
                    QueryexResult<CompiledQuery> result = engine.EvaluateConstant(
                        [new ConstantItem("1", QueryexType.QxNumeric), new ConstantItem(entry.Text, type)],
                        Options());

                    if (result.Succeeded)
                    {
                        Assert.Equal(type, result.Value.Columns[1].Type);
                        Assert.DoesNotContain("FROM", result.Value.Sql, StringComparison.Ordinal);
                        continue;
                    }

                    Assert.NotEmpty(result.Diagnostics);
                    Assert.All(result.Diagnostics, diagnostic => Assert.Equal("Constant[1]", diagnostic.Location));
                }
            }
        }

        /// <summary>Builds the options a test compiles under.</summary>
        /// <returns>The options.</returns>
        private static ConstantCompilationOptions Options()
        {
            return new ConstantCompilationOptions
            {
                Schema = LedgerFixture.Schema,
                QueryexVersion = QueryexLanguage.Version,
            };
        }

        /// <summary>Whether a statement shares a cache entry with the default one.</summary>
        /// <param name="items">The statement's items.</param>
        /// <param name="options">The options it compiles under.</param>
        /// <returns>True when it does.</returns>
        private static bool Shares(IReadOnlyList<ConstantItem> items, ConstantCompilationOptions options)
        {
            return ProbeCacheKeys.ShareConstantEntry(Defaults, Options(), items, options);
        }

        /// <summary>Compiles a statement and returns it, failing the test if it did not compile.</summary>
        /// <param name="items">The items.</param>
        /// <param name="options">The options.</param>
        /// <returns>The compiled statement.</returns>
        private static CompiledQuery Compile(IReadOnlyList<ConstantItem> items, ConstantCompilationOptions options)
        {
            return Compile(Engine, items, options);
        }

        /// <summary>Compiles a statement on one engine, failing the test if it did not compile.</summary>
        /// <param name="engine">The engine.</param>
        /// <param name="items">The items.</param>
        /// <param name="options">The options.</param>
        /// <returns>The compiled statement.</returns>
        private static CompiledQuery Compile(
            QueryexEngine engine,
            IReadOnlyList<ConstantItem> items,
            ConstantCompilationOptions options)
        {
            QueryexResult<CompiledQuery> result = engine.EvaluateConstant(items, options);

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            return result.Value;
        }
    }
}
