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
    ///     Compiling with declared parameters left out: what folding does to the text, and what a
    ///     compilation then does with what is left.
    /// </summary>
    /// <remarks>
    ///     The statements a folded query compiles to are pinned by the stored snapshots, and what
    ///     they answer by the differential check. These pin the rule itself, as text before anything
    ///     binds it, and what neither of the others can show: what is never bound, what a caller is
    ///     refused, and what a compilation is remembered under.
    /// </remarks>
    public sealed class OmittedParameterTests
    {
        /// <summary>The engine under test, shared because it is stateless apart from its caches.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>The parameters every test here declares.</summary>
        private static readonly QueryexParameterDeclaration[] Declared =
        [
            new QueryexParameterDeclaration("AsOf", QueryexType.QxDate, IsNotNull: false),
            new QueryexParameterDeclaration("Customer", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Main", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Other", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Root", QueryexType.QxString, IsNotNull: false),
            new QueryexParameterDeclaration("Posted", QueryexType.QxBool, IsNotNull: false),
            new QueryexParameterDeclaration("Since", QueryexType.QxDateTime, IsNotNull: false),
            new QueryexParameterDeclaration("At", QueryexType.QxDateTimeOffset, IsNotNull: false),
            new QueryexParameterDeclaration("Key", QueryexType.QxGuid, IsNotNull: false),
        ];

        /// <summary>The names most tests leave out.</summary>
        private static readonly string[] Left = ["Customer", "Other"];

        /// <summary>
        ///     A predicate folds to the text an author would have written without the parameter.
        /// </summary>
        /// <param name="text">The predicate as written.</param>
        /// <param name="expected">What is left of it.</param>
        [Theory]

        // The cases the rule is stated with.
        [InlineData("PostingDate <= @AsOf and CustomerId = @Customer", "PostingDate <= @AsOf")]
        [InlineData("CustomerId in (@Main, @Other)", "CustomerId in (@Main)")]

        // Absorption, through every connective and to a fixed point.
        [InlineData("CustomerId = @Customer and IsPosted", "IsPosted")]
        [InlineData("IsPosted and CustomerId = @Customer", "IsPosted")]
        [InlineData("IsPosted and (Amount > 1000 or not (CustomerId = @Customer and Amount < 100))", "IsPosted")]
        [InlineData("IsPosted and (CustomerId = @Customer and (CustomerId = @Other and Amount > 0))", "IsPosted and Amount > 0")]
        [InlineData("not (CustomerId = @Customer or Amount > 0)", "not Amount > 0")]
        [InlineData("not (not (CustomerId = @Customer) or IsPosted)", "not IsPosted")]
        [InlineData("not (not (not (CustomerId = @Customer or Amount > 0)))", "not not not Amount > 0")]
        [InlineData("Amount > 0 and not (IsPosted and not (CustomerId = @Customer and Amount < 5))", "Amount > 0 and not (IsPosted and not Amount < 5)")]
        [InlineData("IsPosted or (Amount > 0 and not (CustomerId = @Customer)) or Amount < 0", "IsPosted or Amount > 0 or Amount < 0")]

        // Lists: a bare element leaves, through parentheses of its own, and the rest still restrict.
        [InlineData("CustomerId in ((@Other), @Main, (@Customer))", "CustomerId in (@Main)")]
        [InlineData("CustomerId in (@Customer, 1, @Other, 2)", "CustomerId in (1, 2)")]
        [InlineData("descendantOf(Account.Concept, @Other, 'Equity')", "descendantOf(Account.Concept, 'Equity')")]
        [InlineData("ancestorOf(Account.Concept, 'Cash', @Other)", "ancestorOf(Account.Concept, 'Cash')")]

        // An aggregation's condition folds on its own, whatever the comparison around it mentions.
        [InlineData("count(Id, CustomerId = @Customer) > 2", "count(Id, true) > 2")]
        [InlineData("sum(Amount, CustomerId = @Customer and IsPosted) > @Main", "sum(Amount, IsPosted) > @Main")]
        [InlineData("not (sum(Amount, not (CustomerId = @Customer)) > 2)", "not sum(Amount, true) > 2")]

        // Elsewhere inside an atom that is left there is nothing to fold: the atom was kept because
        // it mentions nothing omitted.
        [InlineData("if(IsPosted, 1, 0) = @Main", "if(IsPosted, 1, 0) = @Main")]
        public void APredicate_FoldsToWhatIsLeft(string text, string expected)
        {
            ProbeFolded folded = ProbeFolding.Predicate(text, Declared, Left);

            Assert.Null(folded.Constant);
            Assert.Equal(expected, folded.Text);
        }

        /// <summary>A predicate that stops deciding anything folds to the value that lets every row through.</summary>
        /// <param name="text">The predicate as written.</param>
        [Theory]

        // The cases the rule is stated with.
        [InlineData("not (CustomerId = @Customer)")]
        [InlineData("Code = 'INV-1' or CustomerId = @Customer")]

        // Every kind of atom, whatever the parameter is to it.
        [InlineData("CustomerId = @Customer")]
        [InlineData("@Customer = CustomerId")]
        [InlineData("CustomerId + @Customer > 3")]
        [InlineData("@Customer is null")]
        [InlineData("coalesce(@Customer, CustomerId) = 1")]
        [InlineData("if(CustomerId = @Customer, IsPosted, false)")]
        [InlineData("(CustomerId = @Customer)")]

        // A list every element has left, and one whose omitted element is not bare.
        [InlineData("CustomerId in (@Customer)")]
        [InlineData("CustomerId in (@Other, (@Customer))")]
        [InlineData("CustomerId in (@Main, @Other + 1)")]
        [InlineData("@Customer in (1, 2)")]
        [InlineData("descendantOf(Account.Concept, @Other)")]
        [InlineData("ancestorOf(Account.Concept, @Customer, @Other)")]

        // An atom that mentions an omitted parameter outside an aggregation's condition.
        [InlineData("sum(Amount, IsPosted) > @Customer")]
        [InlineData("sum(@Customer, IsPosted) > 1")]

        // Absorbed all the way up.
        [InlineData("not (not (CustomerId = @Customer))")]
        [InlineData("not (CustomerId = @Customer and IsPosted) or Amount > 0")]
        [InlineData("CustomerId = @Customer and CustomerId = @Other")]
        public void APredicate_ThatDecidesNothing_FoldsToTrue(string text)
        {
            ProbeFolded folded = ProbeFolding.Predicate(text, Declared, Left);

            Assert.True(folded.Constant);
        }

        /// <summary>
        ///     Under a negation composed around the text, each atom reads as false instead, so that
        ///     the negation of what is left still lets every row through.
        /// </summary>
        /// <param name="text">The predicate as written.</param>
        /// <param name="expected">What is left of it, or the truth value it folded to.</param>
        [Theory]
        [InlineData("CustomerId = @Customer", "false")]
        [InlineData("CustomerId = @Customer or Amount > 0", "Amount > 0")]
        [InlineData("CustomerId = @Customer and Amount > 0", "false")]
        [InlineData("not (CustomerId = @Customer)", "false")]
        [InlineData("not (CustomerId = @Customer) or IsPosted", "IsPosted")]
        [InlineData("not (CustomerId = @Customer and IsPosted)", "not IsPosted")]
        [InlineData("CustomerId in (@Other)", "false")]
        [InlineData("CustomerId in (@Other, @Main)", "CustomerId in (@Main)")]

        // The condition of an aggregation starts a skeleton of its own: nothing above reaches it.
        [InlineData("count(Id, CustomerId = @Customer) > 2", "count(Id, true) > 2")]
        public void UnderAComposedNegation_AnAtomReadsAsFalse(string text, string expected)
        {
            ProbeFolded folded = ProbeFolding.Predicate(text, Declared, Left, negated: true);

            Assert.Equal(expected, folded.Text);
            Assert.Equal(expected is "false", folded.Constant is false);
            Assert.NotEqual(true, folded.Constant);
        }

        /// <summary>
        ///     Only a truth value folding produced is absorbed; one the author wrote stays where it
        ///     was written.
        /// </summary>
        /// <param name="text">The predicate as written.</param>
        /// <param name="expected">What is left of it.</param>
        [Theory]
        [InlineData("true and CustomerId = @Customer", "true")]
        [InlineData("false and CustomerId = @Customer", "false")]
        [InlineData("IsPosted and true and CustomerId = @Customer", "IsPosted and true")]
        [InlineData("not false and CustomerId = @Customer", "not false")]
        public void AWrittenTruthValue_IsLeftAsWritten(string text, string expected)
        {
            ProbeFolded folded = ProbeFolding.Predicate(text, Declared, Left);

            // Text is left, not a settled answer: the author's literal is bound like anything else.
            Assert.Null(folded.Constant);
            Assert.Equal(expected, folded.Text);
        }

        /// <summary>A text that mentions nothing omitted comes back as the tree it was.</summary>
        /// <param name="text">The text.</param>
        [Theory]
        [InlineData("PostingDate <= @AsOf")]
        [InlineData("CustomerId in (@Main, 2) and not IsPosted")]
        [InlineData("IsPosted and true")]
        [InlineData("customer = 1")]
        public void ATextThatMentionsNothingOmitted_IsUntouched(string text)
        {
            Assert.True(ProbeFolding.Predicate(text, Declared, Left).Untouched);
            Assert.True(ProbeFolding.Predicate(text, Declared, Left, negated: true).Untouched);
            Assert.True(ProbeFolding.Values(text, Declared, Left).Untouched);
        }

        /// <summary>Nothing left out leaves every text alone, whatever it mentions.</summary>
        [Fact]
        public void WithNothingOmitted_EveryTextIsUntouched()
        {
            Assert.True(ProbeFolding.Predicate("CustomerId = @Customer", Declared, []).Untouched);
            Assert.True(ProbeFolding.Values("sum(Amount, CustomerId = @Customer)", Declared, []).Untouched);
        }

        /// <summary>
        ///     Read as a value, an omitted parameter is the absent value of its declared type, written
        ///     the way an author would write it; an aggregation's condition is still a predicate.
        /// </summary>
        /// <param name="text">The values as written.</param>
        /// <param name="expected">What they fold to.</param>
        [Theory]

        // The case the rule is stated with.
        [InlineData("sum(Amount, CustomerId = @Customer)", "sum(Amount, true)")]

        // The absent value, in every position that is not a predicate's skeleton.
        [InlineData("@Customer", "cast(null, 'numeric')")]
        [InlineData("Id, @customer desc", "Id, cast(null, 'numeric') desc")]
        [InlineData("coalesce(@Customer, CustomerId, 0)", "coalesce(cast(null, 'numeric'), CustomerId, 0)")]
        [InlineData("CustomerId = @Customer", "CustomerId = cast(null, 'numeric')")]
        [InlineData("not (CustomerId = @Customer)", "not CustomerId = cast(null, 'numeric')")]
        [InlineData("CustomerId in (@Main, @Other)", "CustomerId in (@Main, cast(null, 'numeric'))")]
        [InlineData("if(CustomerId = @Customer, 1, 0)", "if(CustomerId = cast(null, 'numeric'), 1, 0)")]
        [InlineData("-@Customer + (@Other)", "-cast(null, 'numeric') + cast(null, 'numeric')")]

        // The conditions of aggregations, wherever the aggregation sits.
        [InlineData("count(Id, CustomerId = @Customer and IsPosted)", "count(Id, IsPosted)")]
        [InlineData("avg(Amount, CustomerId in (@Main, @Other))", "avg(Amount, CustomerId in (@Main))")]
        [InlineData("min(Amount, not (CustomerId = @Customer)) + 1", "min(Amount, true) + 1")]
        [InlineData("if(max(Amount, CustomerId = @Customer) > 5, 1, 0)", "if(max(Amount, true) > 5, 1, 0)")]
        [InlineData("sum(@Customer, IsPosted)", "sum(cast(null, 'numeric'), IsPosted)")]
        [InlineData("sum(if(CustomerId = @Customer, Amount, 0), CustomerId = @Other)", "sum(if(CustomerId = cast(null, 'numeric'), Amount, 0), true)")]
        public void AValue_FoldsToTheAbsentValueOfItsType(string text, string expected)
        {
            ProbeFolded folded = ProbeFolding.Values(text, Declared, Left);

            Assert.Equal(expected, folded.Text);
        }

        /// <summary>The absent value is written at the type the parameter was declared with.</summary>
        /// <param name="name">The parameter.</param>
        /// <param name="expected">The absent value it becomes.</param>
        [Theory]
        [InlineData("AsOf", "cast(null, 'date')")]
        [InlineData("Customer", "cast(null, 'numeric')")]
        [InlineData("Root", "cast(null, 'string')")]
        [InlineData("Posted", "cast(null, 'bool')")]
        [InlineData("Since", "cast(null, 'datetime')")]
        [InlineData("At", "cast(null, 'datetimeoffset')")]
        [InlineData("Key", "cast(null, 'guid')")]
        public void TheAbsentValue_TakesTheDeclaredType(string name, string expected)
        {
            Assert.Equal(expected, ProbeFolding.Values("@" + name, Declared, [name]).Text);

            // And the engine reads that text as that type: the column is the declared type's, and
            // can only ever be absent.
            CompiledQuery query = Compile(
                new QuerySpec { Root = LedgerFixture.Invoice, Select = "Id, @" + name },
                [name]);

            QueryexColumn column = query.Columns[1];
            Assert.Equal(Declared.Single(declaration => declaration.Name == name).Type, column.Type);
            Assert.Equal(QueryexNullity.Null, column.Nullity);
            Assert.Empty(query.Parameters);
        }

        /// <summary>
        ///     What folds away is never bound, so a mistake inside it is not reported — neither in
        ///     the part that mentions the parameter nor in what that part absorbs.
        /// </summary>
        /// <param name="filter">The filter text.</param>
        [Theory]
        [InlineData("Nothing = @Customer and IsPosted")]
        [InlineData("IsPosted and nothing(@Customer)")]
        [InlineData("CustomerId = @Customer or Nothing = 1")]
        [InlineData("IsPosted and (CustomerId = @Customer or Code + 1 = 2)")]
        public void WhatFoldsAway_IsNotBound(string filter)
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf(filter),
            };

            Assert.True(Engine.CompileQuery(spec, Options(["Customer"])).Succeeded);

            // The same text with nothing left out is refused: the mistake is really there.
            Assert.False(Engine.CompileQuery(spec, Options([])).Succeeded);
        }

        /// <summary>
        ///     A text that cannot be read is reported once, against its own place in the tree,
        ///     whether or not what stands around it folds away.
        /// </summary>
        [Fact]
        public void AnUnreadableLeaf_IsReportedOnce_WhereverItStands()
        {
            var unreadable = FilterTree.Leaf("(Amount > ");
            var omitted = FilterTree.Leaf("CustomerId = @Customer");

            // Beside a leaf that settles the disjunction; inside a rebase that mentions nothing
            // omitted; inside one that does; and inside a semi-join that folds away.
            (FilterTree Tree, string Location)[] trees =
            [
                (FilterTree.Or([omitted, unreadable]), "Filter.Or[1]"),
                (FilterTree.And([omitted, FilterTree.Via("Customer", unreadable)]), "Filter.And[1].Via[Customer]"),
                (
                    FilterTree.Via("Customer", FilterTree.And([FilterTree.Leaf("Id = @Customer"), unreadable])),
                    "Filter.Via[Customer].And[1]"),
                (
                    FilterTree.Not(FilterTree.Exists(
                        "InvoiceLine",
                        "Invoice",
                        FilterTree.Or([unreadable, FilterTree.Leaf("PartyId = @Customer")]))),
                    "Filter.Not.Exists[InvoiceLine].Or[0]"),
            ];

            foreach ((FilterTree tree, string location) in trees)
            {
                QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                    new QuerySpec { Root = LedgerFixture.Invoice, Select = "Id", Filter = tree },
                    Options(["Customer"]));

                QueryexDiagnostic diagnostic = Assert.Single(result.Diagnostics);
                Assert.StartsWith("QX2", diagnostic.Code, StringComparison.Ordinal);
                Assert.Equal(location, diagnostic.Location);
            }
        }

        /// <summary>An omitted parameter takes no slot, and the ones that are left keep theirs.</summary>
        [Fact]
        public void AnOmittedParameter_TakesNoSlot()
        {
            CompiledQuery query = Compile(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id, @Customer",
                    Filter = FilterTree.Leaf("CustomerId = @Customer and PostingDate <= @AsOf"),
                    OrderBy = "coalesce(@Customer, Id)",
                },
                ["Customer"]);

            QueryexParameterSlot slot = Assert.Single(query.Parameters);
            Assert.Equal("AsOf", slot.DeclaredName);
        }

        /// <summary>Leaving nothing out compiles to exactly what leaving the option unset does.</summary>
        [Fact]
        public void AnEmptySet_CompilesAsNoSetAtAll()
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, sum(Amount, CustomerId = @Customer)",
                Filter = FilterTree.Leaf("PostingDate <= @AsOf and CustomerId in (@Main, @Other)"),
                Having = FilterTree.Leaf("count(Id, CustomerId = @Customer) > 2"),
                OrderBy = "Centre.Name",
            };

            QueryCompilationOptions unset = new()
            {
                QueryexVersion = QueryexLanguage.Version,
                Schema = LedgerFixture.Schema,
                Parameters = Declared,
            };

            Assert.Empty(unset.OmittedParameters);
            Assert.True(ProbeCacheKeys.ShareQueryEntry(spec, unset, spec, Options([])));

            CompiledQuery plain = Compile(new QueryexEngine(), spec, unset);
            CompiledQuery empty = Compile(new QueryexEngine(), spec, Options([]));

            Assert.Equal(plain.Sql, empty.Sql);
            Assert.Equal(plain.Parameters, empty.Parameters);
            Assert.Equal(
                plain.Columns.Select(column => (column.Text, column.Type, column.Nullity, column.IsGroupingKey)),
                empty.Columns.Select(column => (column.Text, column.Type, column.Nullity, column.IsGroupingKey)));
        }

        /// <summary>A name that is not a declared parameter is the caller's mistake.</summary>
        /// <param name="name">The name left out.</param>
        [Theory]
        [InlineData("Nobody")]
        [InlineData("@Customer")]
        [InlineData("")]
        [InlineData(null)]
        public void AnUndeclaredName_IsRefused(string? name)
        {
            QuerySpec spec = new() { Root = LedgerFixture.Invoice, Select = "Id" };

            ArgumentException thrown = Assert.Throws<ArgumentException>(
                () => Engine.CompileQuery(spec, Options(["Customer", name!])));

            Assert.Equal("options", thrown.ParamName);
        }

        /// <summary>The option itself cannot be set to nothing.</summary>
        [Fact]
        public void ANullSet_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => Options(null!));
        }

        /// <summary>
        ///     A compilation is remembered under the parameters it left out, however the caller
        ///     spelled or ordered them.
        /// </summary>
        [Fact]
        public void TheOmittedSet_IsPartOfTheKey()
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("CustomerId = @Customer and PostingDate <= @AsOf"),
            };

            Assert.False(ProbeCacheKeys.ShareQueryEntry(spec, Options([]), spec, Options(["Customer"])));
            Assert.False(ProbeCacheKeys.ShareQueryEntry(spec, Options(["Customer"]), spec, Options(["AsOf"])));
            Assert.False(ProbeCacheKeys.ShareQueryEntry(spec, Options(["Customer"]), spec, Options(["Customer", "AsOf"])));

            // Even a parameter the query never mentions: the key is cheap to compute because it does
            // not look inside the texts.
            Assert.False(ProbeCacheKeys.ShareQueryEntry(spec, Options([]), spec, Options(["Main"])));

            Assert.True(ProbeCacheKeys.ShareQueryEntry(
                spec,
                Options(["Customer", "AsOf"]),
                spec,
                Options(["ASOF", "customer", "Customer"])));
        }

        /// <summary>
        ///     One text bound with a parameter folded out of it, the same under a negation, and with
        ///     nothing folded are three different bindings.
        /// </summary>
        [Fact]
        public void WhatWasFolded_IsPartOfABindingsKey()
        {
            Assert.True(ProbeCacheKeys.DistinguishesFolding(LedgerFixture.Schema, LedgerFixture.Invoice));
        }

        /// <summary>
        ///     One engine asked for the same query with and without a parameter serves each its own
        ///     compilation, in either order and again from the cache.
        /// </summary>
        [Fact]
        public void OneEngine_KeepsTheCompilationsApart()
        {
            const string Text = "(CustomerId = @Customer or IsPosted) and Amount > 0";

            QueryexEngine engine = new();
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.And([FilterTree.Leaf(Text), FilterTree.Not(FilterTree.Leaf(Text))]),
            };

            CompiledQuery whole = Compile(engine, spec, Options([]));
            CompiledQuery folded = Compile(engine, spec, Options(["Customer"]));
            CompiledQuery wholeAgain = Compile(engine, spec, Options([]));
            CompiledQuery foldedAgain = Compile(engine, spec, Options(["customer"]));

            Assert.Same(whole.Sql, wholeAgain.Sql);
            Assert.Same(folded.Sql, foldedAgain.Sql);
            Assert.NotEqual(whole.Sql, folded.Sql);

            // The one leaf text is bound three ways on this engine: whole, folded where it is
            // asserted, and folded where it is denied. Something is left of it under both
            // polarities, and what is left differs: asserted it keeps only the amount, denied it
            // keeps the posting flag as well.
            Assert.Contains(
                "WHERE (([T].[Amount] > @qx0_p0) AND NOT ((([T].[IsPosted] = 1) AND ([T].[Amount] > @qx0_p0))))\n",
                folded.Sql,
                StringComparison.Ordinal);

            Assert.DoesNotContain(folded.Parameters, slot => slot.DeclaredName is not null);

            // The other way round on the same engine, so the denied binding is asked for first and
            // the asserted one cannot be served it.
            CompiledQuery mirrored = Compile(
                engine,
                spec with { Filter = FilterTree.And([FilterTree.Not(FilterTree.Leaf(Text)), FilterTree.Leaf(Text)]) },
                Options(["Customer"]));

            Assert.Contains(
                "WHERE (NOT ((([T].[IsPosted] = 1) AND ([T].[Amount] > @qx0_p0))) AND ([T].[Amount] > @qx0_p0))\n",
                mirrored.Sql,
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     Two ordering terms that are the same only because a parameter was left out of one of
        ///     them are written once and not reported; two that were written the same still are.
        /// </summary>
        [Fact]
        public void AnOrderingTerm_RepeatedOnlyByFolding_IsDroppedNotReported()
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, sum(Amount, CustomerId = @Customer), sum(Amount, CustomerId = @Other)",
                OrderBy = "sum(Amount, CustomerId = @Customer) desc, sum(Amount, CustomerId = @Other)",
            };

            // As written the two measures differ, and the query compiles with both terms.
            CompiledQuery whole = Compile(spec, []);
            Assert.EndsWith("ORDER BY [c1] DESC, [c2] ASC\n;\n", whole.Sql, StringComparison.Ordinal);

            // With both parameters left out they are one measure: ordered by once, under the
            // direction of the term that came first.
            CompiledQuery folded = Compile(spec, ["Customer", "Other"]);
            Assert.EndsWith("ORDER BY [c1] DESC\n;\n", folded.Sql, StringComparison.Ordinal);

            // One left out is enough when what is left equals a term that was never touched.
            CompiledQuery beside = Compile(
                spec with { OrderBy = "sum(Amount, true), sum(Amount, CustomerId = @Other) desc" },
                ["Other"]);

            Assert.EndsWith("ORDER BY [c2] ASC\n;\n", beside.Sql, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A term written twice is still reported when other terms of the list had parameters
        ///     left out of them: what is reported about a text does not depend on what was omitted
        ///     beside it.
        /// </summary>
        [Fact]
        public void AnOrderingTerm_WrittenTwice_IsStillReported()
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    OrderBy = "coalesce(@Customer, Amount), Id, Id desc, coalesce(@Other, Amount)",
                },
                Options(["Customer", "Other"]));

            QueryexDiagnostic diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("QX4008", diagnostic.Code);
            Assert.Equal("OrderBy", diagnostic.Location);
            Assert.Equal(new QueryexSpan(33, 7), diagnostic.Span);
        }

        /// <summary>
        ///     A filter that folds away entirely leaves a query with no filter, whichever way it was
        ///     composed: nothing left out can ever leave a filter that denies every row.
        /// </summary>
        [Fact]
        public void AWholeTree_ThatFoldsAway_LeavesNoFilter()
        {
            CompiledQuery gone = Compile(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.Or([FilterTree.Leaf("IsPosted"), FilterTree.Leaf("CustomerId = @Customer")]),
                },
                ["Customer"]);

            Assert.DoesNotContain("WHERE", gone.Sql, StringComparison.Ordinal);

            // Under a composed negation the leaf reads as false where it stands, and the negation
            // of that lets every row through just the same.
            CompiledQuery denied = Compile(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.Not(FilterTree.Leaf("CustomerId = @Customer")),
                },
                ["Customer"]);

            Assert.DoesNotContain("WHERE", denied.Sql, StringComparison.Ordinal);
            Assert.Equal(gone.Sql, denied.Sql);
        }

        /// <summary>
        ///     Every query of the corpus that declares parameters and compiles with nothing left out
        ///     still compiles when each of them is left out in turn and when all of them are — and
        ///     binds nothing for a parameter that was left out.
        /// </summary>
        /// <remarks>
        ///     A stored definition is checked with nothing left out, and whoever runs it leaves out
        ///     whatever they like, so leaving a parameter out must never be what gets a query
        ///     refused: the author could not have seen it coming, and the reader cannot fix it.
        /// </remarks>
        [Fact]
        public void EveryCorpusQuery_SurvivesLeavingItsParametersOut()
        {
            QueryexEngine engine = new();
            int compiled = 0;
            foreach (QueryCase entry in QueryCorpus.All.Where(c => c.Parameters.Count > 0 && c.Diagnostics.Count == 0))
            {
                string[] all = [.. entry.Parameters.Select(parameter => parameter.Name)];
                foreach (string[] omitted in all.Select(name => new[] { name }).Append(all))
                {
                    QueryexResult<CompiledQuery> result = engine.CompileQuery(
                        entry.Spec,
                        entry.Options(LedgerFixture.Schema) with { OmittedParameters = omitted });

                    Assert.True(
                        result.Succeeded,
                        entry.Id + " without " + string.Join(", ", omitted) + ": "
                            + string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

                    compiled++;
                    Assert.DoesNotContain(
                        result.Value.Parameters,
                        slot => slot.DeclaredName is not null
                            && omitted.Contains(slot.DeclaredName, StringComparer.OrdinalIgnoreCase));
                }
            }

            // The check is only worth something while the corpus keeps feeding it.
            Assert.True(compiled > 20, "too few corpus queries declare parameters");
        }

        /// <summary>
        ///     The absent value an omitted parameter becomes counts against the ceiling on bound
        ///     nodes exactly as the parameter did: no more, and no less.
        /// </summary>
        /// <param name="select">The select list.</param>
        /// <param name="orderBy">The ordering list.</param>
        [Theory]
        [InlineData("Id, coalesce(@Customer, 0), coalesce(@Other, 0), coalesce(@Customer, 1), coalesce(@Other, 1)", "Id")]
        [InlineData("Id", "coalesce(@Customer, Amount), coalesce(@Other, Id)")]
        [InlineData("Id, @Customer, @Other, @Root, @AsOf, @Posted", "Id")]
        public void AnAbsentValue_CountsAsTheParameterItReplaced(string select, string orderBy)
        {
            QueryexEngine engine = new();
            QuerySpec spec = new() { Root = LedgerFixture.Invoice, Select = select, OrderBy = orderBy };

            int whole = SmallestCeiling(engine, spec, Options([]));
            int folded = SmallestCeiling(engine, spec, Options(["Customer", "Other", "Root", "AsOf", "Posted"]));

            Assert.Equal(whole, folded);
        }

        /// <summary>
        ///     Every query of the corpus that declares parameters fits, with any of them left out,
        ///     under the tightest ceiling on bound nodes it fits under with none left out.
        /// </summary>
        /// <remarks>
        ///     A stored definition is checked against its ceilings with nothing left out. If
        ///     leaving a parameter out could make it bigger, a definition written close to a
        ///     ceiling would be accepted and then refused when someone ran it.
        /// </remarks>
        [Fact]
        public void EveryCorpusQuery_FitsItsTightestCeiling_WithItsParametersLeftOut()
        {
            QueryexEngine engine = new();
            int checkedCompilations = 0;
            foreach (QueryCase entry in QueryCorpus.All.Where(c => c.Parameters.Count > 0 && c.Diagnostics.Count == 0))
            {
                QueryCompilationOptions options = entry.Options(LedgerFixture.Schema);
                int ceiling = SmallestCeiling(engine, entry.Spec, options with { OmittedParameters = [] });

                string[] all = [.. entry.Parameters.Select(parameter => parameter.Name)];
                foreach (string[] omitted in all.Select(name => new[] { name }).Append(all))
                {
                    QueryexResult<CompiledQuery> result = engine.CompileQuery(
                        entry.Spec,
                        options with
                        {
                            OmittedParameters = omitted,
                            Limits = options.Limits with { MaxTypedNodes = ceiling },
                        });

                    checkedCompilations++;
                    Assert.True(
                        result.Succeeded,
                        entry.Id + " without " + string.Join(", ", omitted) + " at " + ceiling + ": "
                            + string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));
                }
            }

            // The check is only worth something while the corpus keeps feeding it.
            Assert.True(checkedCompilations > 20, "too few corpus queries declare parameters");
        }

        /// <summary>Finds the tightest ceiling on bound nodes a query compiles under.</summary>
        /// <param name="engine">The engine.</param>
        /// <param name="spec">The query.</param>
        /// <param name="options">The options, whose other ceilings are left as they are.</param>
        /// <returns>The smallest ceiling the query compiles under.</returns>
        private static int SmallestCeiling(QueryexEngine engine, QuerySpec spec, QueryCompilationOptions options)
        {
            // The query has to compile as given, or there is nothing to search for.
            int fits = options.Limits.MaxTypedNodes;
            _ = Compile(engine, spec, options);

            // Whether a query fits is monotonic in the ceiling, so the boundary is found by halving.
            int tooSmall = 0;
            while (fits - tooSmall > 1)
            {
                int middle = tooSmall + ((fits - tooSmall) / 2);
                QueryexResult<CompiledQuery> result = engine.CompileQuery(
                    spec,
                    options with { Limits = options.Limits with { MaxTypedNodes = middle } });

                if (result.Succeeded)
                {
                    fits = middle;
                }
                else
                {
                    // Nothing but the ceiling being searched may be what refused it.
                    Assert.All(result.Diagnostics, diagnostic => Assert.Equal("QX5004", diagnostic.Code));
                    tooSmall = middle;
                }
            }

            return fits;
        }

        /// <summary>Builds the options a test compiles under.</summary>
        /// <param name="omitted">The names left out.</param>
        /// <returns>The options.</returns>
        private static QueryCompilationOptions Options(IReadOnlyCollection<string> omitted)
        {
            return new QueryCompilationOptions
            {
                QueryexVersion = QueryexLanguage.Version,
                Schema = LedgerFixture.Schema,
                Parameters = Declared,
                OmittedParameters = omitted,
            };
        }

        /// <summary>Compiles a query and returns it, failing the test if it did not compile.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="omitted">The names left out.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compile(QuerySpec spec, IReadOnlyCollection<string> omitted)
        {
            return Compile(Engine, spec, Options(omitted));
        }

        /// <summary>Compiles a query on one engine, failing the test if it did not compile.</summary>
        /// <param name="engine">The engine.</param>
        /// <param name="spec">The query.</param>
        /// <param name="options">The options.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compile(QueryexEngine engine, QuerySpec spec, QueryCompilationOptions options)
        {
            QueryexResult<CompiledQuery> result = engine.CompileQuery(spec, options);

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            return result.Value;
        }
    }
}
