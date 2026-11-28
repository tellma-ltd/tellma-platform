// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Binding
{
    /// <summary>The one path a parameter is compared with, as discovery reports it.</summary>
    public sealed class ComparedWithTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>Discovery against the fixture schema, from invoices.</summary>
        private static DiscoveryOptions Options => new()
        {
            Schema = LedgerFixture.Schema,
            Root = LedgerFixture.Invoice,
            Mode = QueryexMode.Filter,
        };

        /// <summary>A bare parameter meets the bare path it is compared with, by any operator and in either order.</summary>
        /// <param name="text">The predicate.</param>
        /// <param name="expected">The path <c>@p</c> meets, dotted, or null for none.</param>
        [Theory]
        [InlineData("CustomerId = @p", "CustomerId")]
        [InlineData("@p = CustomerId", "CustomerId")]
        [InlineData("Amount != @p", "Amount")]
        [InlineData("@p != Amount", "Amount")]
        [InlineData("PostingDate < @p", "PostingDate")]
        [InlineData("@p < PostingDate", "PostingDate")]
        [InlineData("PostingDate <= @p", "PostingDate")]
        [InlineData("@p <= PostingDate", "PostingDate")]
        [InlineData("Customer.Name > @p", "Customer.Name")]
        [InlineData("@p > Customer.Name", "Customer.Name")]
        [InlineData("Customer.Region.Id >= @p", "Customer.Region.Id")]
        [InlineData("@p >= Customer.Region.Id", "Customer.Region.Id")]

        // A list element, and a key of either hierarchy predicate, meet the tested path.
        [InlineData("CustomerId in (1, @p, 3)", "CustomerId")]
        [InlineData("Account.Concept in (@p)", "Account.Concept")]
        [InlineData("descendantOf(Account.Concept, @p)", "Account.Concept")]
        [InlineData("ancestorOf(Account.Concept, 'Cash', @p)", "Account.Concept")]

        // Meeting the same path twice, in any spelling, is still meeting one path.
        [InlineData("CustomerId = @p or @P != customerid", "CustomerId")]

        // Two different paths, and anything that is not bare on both sides, meet none.
        [InlineData("CustomerId = @p or CreatedById = @p", null)]
        [InlineData("@p = @q", null)]
        [InlineData("Amount = @p + 1", null)]
        [InlineData("Amount * 2 = @p", null)]
        [InlineData("(Amount) = (@p)", "Amount")]
        [InlineData("@p in (CustomerId, 2)", null)]
        [InlineData("Amount + 1 in (@p)", null)]
        [InlineData("contains(Memo, @p)", null)]
        [InlineData("if(IsPosted, Amount, 0) = @p", null)]
        public void AParameter_MeetsTheBarePathItIsComparedWith(string text, string? expected)
        {
            DiscoveryResult result = Engine.Discover(text, Options);

            ParameterUse use = result.Parameters.Single(p => p.Name.Equals("p", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(expected, use.ComparedWith is null ? null : string.Join('.', use.ComparedWith));
        }

        /// <summary>The worked example: each parameter meets its own path.</summary>
        [Fact]
        public void TheExample_GivesEachParameterItsPath()
        {
            DiscoveryResult result = Engine.Discover(
                "Invoice.CustomerId = @Customer and Invoice.PostingDate >= @From",
                new DiscoveryOptions
                {
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.InvoiceLine,
                    Mode = QueryexMode.Filter,
                });

            Assert.Empty(result.Diagnostics);
            Assert.Equal(["Invoice", "CustomerId"], Single(result, "Customer").ComparedWith);
            Assert.Equal(["Invoice", "PostingDate"], Single(result, "From").ComparedWith);
        }

        /// <summary>
        ///     Across a whole query every clause counts: a parameter meeting one path in the filter
        ///     and another in the select list meets none.
        /// </summary>
        [Fact]
        public void AcrossClauses_TwoPathsMeetNone()
        {
            DiscoveryResult result = Engine.DiscoverQuery(
                new QueryDiscoverySpec
                {
                    Select = "Id, if(CreatedById = @c, 1, 0)",
                    Filter = FilterTree.Leaf("CustomerId = @c and Amount > @a"),
                    OrderBy = "Amount",
                },
                new DiscoveryOptions { Schema = LedgerFixture.Schema, Root = LedgerFixture.Invoice });

            Assert.Empty(result.Diagnostics);
            Assert.Null(Single(result, "c").ComparedWith);
            Assert.Equal(["Amount"], Single(result, "a").ComparedWith);
        }

        /// <summary>
        ///     Inside a rebase the path reads from the query's root, behind the rebase's navigation;
        ///     inside a semi-join nothing is reported, and nothing there conflicts with what is.
        /// </summary>
        [Fact]
        public void InsideARebase_ThePathIsRootRelative_AndInsideASemiJoinNothingIsReported()
        {
            DiscoveryResult result = Engine.DiscoverQuery(
                new QueryDiscoverySpec
                {
                    Filter = FilterTree.And(
                    [
                        FilterTree.Via("Customer", FilterTree.Via("Region", FilterTree.Leaf("Name = @region"))),
                        FilterTree.Via("Customer", FilterTree.Leaf("@manager = ManagerId")),
                        FilterTree.Leaf("Customer.ManagerId = @manager"),
                        FilterTree.Exists(
                            "InvoiceLine",
                            "Invoice",
                            FilterTree.Leaf("Memo = @memo and Amount = @manager and PartyId = @party")),
                    ]),
                },
                new DiscoveryOptions { Schema = LedgerFixture.Schema, Root = LedgerFixture.Invoice });

            Assert.Empty(result.Diagnostics);
            Assert.Equal(["Customer", "Region", "Name"], Single(result, "region").ComparedWith);
            Assert.Equal(["Customer", "ManagerId"], Single(result, "manager").ComparedWith);
            Assert.Null(Single(result, "memo").ComparedWith);
            Assert.Null(Single(result, "party").ComparedWith);
        }

        /// <summary>Without a schema nothing is bound, so nothing is reported, like the inferred type.</summary>
        [Fact]
        public void WithoutASchema_NothingIsReported()
        {
            DiscoveryResult result = Engine.Discover("CustomerId = @p", new DiscoveryOptions());

            Assert.Null(Assert.Single(result.Parameters).ComparedWith);
        }

        /// <summary>A path that does not resolve is not met.</summary>
        [Fact]
        public void AnUnresolvedPath_IsNotMet()
        {
            DiscoveryResult result = Engine.Discover("Nonesuch = @p", Options);

            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "QX3001");
            Assert.Null(Single(result, "p").ComparedWith);
        }

        /// <summary>The one use of a parameter of a given name.</summary>
        /// <param name="result">What discovery reported.</param>
        /// <param name="name">The parameter's name.</param>
        /// <returns>The use.</returns>
        private static ParameterUse Single(DiscoveryResult result, string name)
        {
            return result.Parameters.Single(use => use.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
