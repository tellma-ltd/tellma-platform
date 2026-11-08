// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     A rebase onto a navigation's target and a semi-join over another entity's rows, run on a
    ///     real server: each is the exact complement of its negation, and one compiled statement
    ///     answers for every user.
    /// </summary>
    /// <remarks>
    ///     The corpus already compares every such case with the second implementation. These assert
    ///     what a comparison of two answers cannot: that a criterion and its negation partition the
    ///     rows between them — the rows whose navigation reaches nothing, and the rows nothing points
    ///     at, among them — and which rows those are, so the two implementations cannot agree on a
    ///     wrong answer.
    /// </remarks>
    /// <param name="server">The server the fixture put the rows on.</param>
    [Collection(SqlServerGroup.Name)]
    [Trait("Category", "Integration")]
    public sealed class FilterStructureTests(SqlServerFixture server)
    {
        /// <summary>The invoices with no customer, which every rebase onto the customer reaches nothing from.</summary>
        private static readonly int[] WithoutCustomer = [2, 4, 6];

        /// <summary>The invoices with no lines, which no semi-join over the lines finds anything for.</summary>
        private static readonly int[] WithoutLines = [2, 6];

        /// <summary>The lines with no party, which a rebase onto the party reaches nothing from.</summary>
        private static readonly int[] WithoutParty = [2, 4];

        /// <summary>The lines of invoices with no customer, which a rebase through to the customer reaches nothing from.</summary>
        private static readonly int[] WithoutCustomerThroughInvoice = [5];

        /// <summary>The server the fixture put the rows on.</summary>
        private readonly SqlServerFixture _server = server;

        /// <summary>The engine under test.</summary>
        private readonly QueryexEngine _engine = new();

        /// <summary>A rebased criterion and its negation split the invoices between them.</summary>
        /// <param name="navigation">The rebase's path.</param>
        /// <param name="criterion">The criterion, against the path's target.</param>
        /// <param name="expected">The invoices the criterion keeps.</param>
        [Theory]
        [InlineData("Customer", "Name = 'Acme'", new[] { 3 })]
        [InlineData("Customer", "Gender = 'F'", new[] { 5 })]
        [InlineData("Customer", "Gender is null", new[] { 1 })]
        [InlineData("Customer.Region", "Name = 'North'", new[] { 1, 3 })]
        [InlineData("Customer", "Manager.Name = 'Acme'", new[] { 1, 5 })]
        [InlineData("Centre", "Name = 'Ops'", new[] { 2, 4, 6 })]
        public async Task AViaAndItsNegation_PartitionTheRows(string navigation, string criterion, int[] expected)
        {
            var positive = FilterTree.Via(navigation, FilterTree.Leaf(criterion));

            IReadOnlySet<int> kept = await Ids(positive);
            IReadOnlySet<int> rest = await Ids(FilterTree.Not(positive));

            Assert.Equal(expected.Order(), kept.Order());
            AssertPartition(kept, rest, LedgerFixture.Invoice);

            if (navigation.StartsWith("Customer", StringComparison.Ordinal))
            {
                // A row whose navigation reaches nothing is outside the criterion and inside its
                // negation, whatever the criterion asks of the target.
                Assert.Superset(WithoutCustomer.ToHashSet(), rest.ToHashSet());
            }
        }

        /// <summary>
        ///     A child filtered by a criterion on its owner: the rebase and its negation split the
        ///     lines between them, and a line whose optional owner is absent is in the negation.
        /// </summary>
        /// <param name="navigation">The rebase's path from the line.</param>
        /// <param name="criterion">The criterion, against the path's target.</param>
        /// <param name="expected">The lines the criterion keeps.</param>
        [Theory]
        [InlineData("Party", "Name = 'Acme'", new[] { 1, 6 })]
        [InlineData("Party", "Gender is null", new[] { 5 })]
        [InlineData("Invoice.Customer", "Name = 'Acme'", new[] { 3, 4 })]
        public async Task AViaFromAChildAndItsNegation_PartitionTheChildRows(
            string navigation,
            string criterion,
            int[] expected)
        {
            var positive = FilterTree.Via(navigation, FilterTree.Leaf(criterion));

            IReadOnlySet<int> kept = await Ids(positive, LedgerFixture.InvoiceLine);
            IReadOnlySet<int> rest = await Ids(FilterTree.Not(positive), LedgerFixture.InvoiceLine);

            Assert.Equal(expected.Order(), kept.Order());
            AssertPartition(kept, rest, LedgerFixture.InvoiceLine);

            int[] unreached = navigation == "Party" ? WithoutParty : WithoutCustomerThroughInvoice;
            Assert.Superset(unreached.ToHashSet(), rest.ToHashSet());
        }

        /// <summary>A semi-join and its negation split the invoices between them.</summary>
        /// <param name="criterion">The criterion, against the lines.</param>
        /// <param name="expected">The invoices the semi-join keeps.</param>
        [Theory]
        [InlineData("Amount < 0", new[] { 1 })]
        [InlineData("Amount > 10", new[] { 3, 4, 5 })]
        [InlineData("Memo is null", new[] { 1, 4, 5 })]
        [InlineData("Party.Name = 'Acme'", new[] { 1, 5 })]
        [InlineData("Party.Name is null", new[] { 1, 3 })]
        public async Task AnExistsAndItsNegation_PartitionTheRows(string criterion, int[] expected)
        {
            var positive = FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf(criterion));

            IReadOnlySet<int> kept = await Ids(positive);
            IReadOnlySet<int> rest = await Ids(FilterTree.Not(positive));

            Assert.Equal(expected.Order(), kept.Order());
            AssertPartition(kept, rest, LedgerFixture.Invoice);

            // Nothing points at an invoice without lines, so it is in every negation.
            Assert.Superset(WithoutLines.ToHashSet(), rest.ToHashSet());
        }

        /// <summary>
        ///     One statement serves every user: the semi-join's criterion names the user through the
        ///     statement's context slot, so the text compiled for one is the text run for another.
        /// </summary>
        [Fact]
        public async Task OneStatement_AnswersForEveryUser()
        {
            QuerySpec spec = Spec(FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("CreatedById = me()")));

            CompiledQuery first = Compile(spec);
            CompiledQuery second = Compile(spec with { Filter = FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("CreatedById = me()")) });
            Assert.Equal(first.Sql, second.Sql);
            Assert.Equal(QueryexParameterOrigin.UserId, Assert.Single(first.Parameters).Origin);

            (int User, int[] Invoices)[] users = [(1, [1, 4]), (2, [1, 3, 5]), (3, [3, 5])];
            foreach ((int user, int[] invoices) in users)
            {
                InterpreterContext context = InterpreterContext.Fixed with { UserId = user };

                IReadOnlySet<int> answered = Identifiers(await DifferentialTests.ExecuteOn(_server, first, context));
                IReadOnlySet<int> expected = Identifiers(ReferenceQuery.Run(spec, context));

                Assert.Equal(invoices.Order(), answered.Order());
                Assert.Equal(expected.Order(), answered.Order());
            }
        }

        /// <summary>Asserts that two sets of rows split every row of an entity between them.</summary>
        /// <param name="kept">What the criterion keeps.</param>
        /// <param name="rest">What its negation keeps.</param>
        /// <param name="entity">The entity whose rows they are.</param>
        private static void AssertPartition(IReadOnlySet<int> kept, IReadOnlySet<int> rest, EntityDescriptor entity)
        {
            Assert.Empty(kept.Intersect(rest));
            Assert.Equal(
                LedgerData.Rows(entity).Count,
                kept.Count + rest.Count);
        }

        /// <summary>The rows a filter keeps, on the server, checked against the second implementation.</summary>
        /// <param name="filter">The filter.</param>
        /// <param name="root">The entity queried; the invoices when not given.</param>
        /// <returns>The rows' keys.</returns>
        private async Task<IReadOnlySet<int>> Ids(FilterTree filter, EntityDescriptor? root = null)
        {
            QuerySpec spec = Spec(filter) with { Root = root ?? LedgerFixture.Invoice };
            IReadOnlySet<int> answered = Identifiers(
                await DifferentialTests.ExecuteOn(_server, Compile(spec), InterpreterContext.Fixed));

            Assert.Equal(
                Identifiers(ReferenceQuery.Run(spec, InterpreterContext.Fixed)).Order(),
                answered.Order());

            return answered;
        }

        /// <summary>Compiles a query, failing the test if it did not compile.</summary>
        /// <param name="spec">The query.</param>
        /// <returns>The compiled query.</returns>
        private CompiledQuery Compile(QuerySpec spec)
        {
            QueryexResult<CompiledQuery> result = _engine.CompileQuery(spec, new QueryCompilationOptions
            {
                QueryexVersion = QueryexLanguage.Version,
                Schema = LedgerFixture.Schema,
            });

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            return result.Value;
        }

        /// <summary>A query for the keys of the invoices a filter keeps.</summary>
        /// <param name="filter">The filter.</param>
        /// <returns>The query.</returns>
        private static QuerySpec Spec(FilterTree filter)
        {
            return new QuerySpec { Root = LedgerFixture.Invoice, Select = "Id", Filter = filter };
        }

        /// <summary>The keys in a one-column answer.</summary>
        /// <param name="rows">The answer.</param>
        /// <returns>The keys.</returns>
        private static HashSet<int> Identifiers(IReadOnlyList<IReadOnlyList<QxValue>> rows)
        {
            HashSet<int> keys = [];
            foreach (IReadOnlyList<QxValue> row in rows)
            {
                Assert.True(keys.Add(decimal.ToInt32(row[0].AsNumber.Value)), "a key appeared twice");
            }

            return keys;
        }
    }
}
