// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Emit
{
    /// <summary>
    ///     Restricting a query to the keys a host holds in a table of its own.
    /// </summary>
    /// <remarks>
    ///     The shapes the emission takes are pinned by the stored snapshots; these pin what the
    ///     snapshots cannot show: the caller errors, the join a restriction shares with the rest of
    ///     the query, the ceiling it counts toward, and the cache key it is part of.
    /// </remarks>
    public sealed class RestrictionTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>A table name in every accepted form is kept as written.</summary>
        /// <param name="source">The table name.</param>
        [Theory]
        [InlineData("@tb2_keys")]
        [InlineData("@keys")]
        [InlineData("@_")]
        [InlineData("@K9")]
        public void AWellFormedTableSource_IsAccepted(string source)
        {
            KeySetRestriction restriction = new("Id", source);

            Assert.Equal(source, restriction.TableSource);
        }

        /// <summary>A table name that could not reach SQL as written is refused when it is built.</summary>
        /// <param name="source">The table name.</param>
        [Theory]
        [InlineData("")]
        [InlineData("@")]
        [InlineData("keys")]
        [InlineData("@9keys")]
        [InlineData("@tb keys")]
        [InlineData("@tb;DROP")]
        [InlineData("@tb]")]
        [InlineData("@@keys")]
        [InlineData("#keys")]
        [InlineData("@tb-keys")]
        [InlineData("@clé")]
        public void AMalformedTableSource_IsRefused(string source)
        {
            ArgumentException thrown = Assert.Throws<ArgumentException>(() => new KeySetRestriction("Id", source));

            Assert.Equal("TableSource", thrown.ParamName);
        }

        /// <summary>A table name longer than the backend allows a variable's is refused.</summary>
        [Fact]
        public void AnOverlongTableSource_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => new KeySetRestriction("Id", "@" + new string('k', 128)));

            KeySetRestriction longest = new("Id", "@" + new string('k', 127));
            Assert.Equal(128, longest.TableSource.Length);
        }

        /// <summary>A table name in the engine's reserved namespace is refused, whatever its case.</summary>
        /// <param name="source">The table name.</param>
        [Theory]
        [InlineData("@qx0_p0")]
        [InlineData("@QX1_keys")]
        [InlineData("@Qxkeys")]
        public void AReservedTableSource_IsRefused(string source)
        {
            Assert.Throws<ArgumentException>(() => new KeySetRestriction("Id", source));
        }

        /// <summary>A path with nothing to resolve is refused when it is built.</summary>
        /// <param name="path">The path.</param>
        [Theory]
        [InlineData("")]
        [InlineData(".Id")]
        [InlineData("Customer.")]
        [InlineData("Customer..Id")]
        public void AMalformedPath_IsRefused(string path)
        {
            ArgumentException thrown = Assert.Throws<ArgumentException>(() => new KeySetRestriction(path, "@keys"));

            Assert.Equal("Path", thrown.ParamName);
        }

        /// <summary>The checks hold for a copy made with a changed member too.</summary>
        [Fact]
        public void ACopyWithAMalformedMember_IsRefused()
        {
            KeySetRestriction restriction = new("Id", "@keys");

            Assert.Throws<ArgumentException>(() => restriction with { TableSource = "@qx_keys" });
            Assert.Throws<ArgumentException>(() => restriction with { Path = string.Empty });
        }

        /// <summary>A path that does not fit the schema is a caller error at compilation.</summary>
        /// <param name="path">The path.</param>
        [Theory]
        [InlineData("Nothing")]
        [InlineData("Customer.Nothing")]
        [InlineData("Nothing.Id")]
        [InlineData("Customer")]
        [InlineData("Customer.Region")]
        [InlineData("Location")]
        [InlineData("Amount.Id")]
        public void APathThatDoesNotFit_IsACallerError(string path)
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Restrictions = [new KeySetRestriction(path, "@keys")],
            };

            ArgumentException thrown = Assert.Throws<ArgumentException>(() => Engine.CompileQuery(spec, Options()));

            Assert.Equal("spec", thrown.ParamName);
            Assert.Contains("Restrictions[0]", thrown.Message, StringComparison.Ordinal);
        }

        /// <summary>The caller error names which restriction was wrong.</summary>
        [Fact]
        public void TheCallerError_NamesTheRestriction()
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Restrictions = [new KeySetRestriction("Id", "@a"), new KeySetRestriction("Customer", "@b")],
            };

            ArgumentException thrown = Assert.Throws<ArgumentException>(() => Engine.CompileQuery(spec, Options()));

            Assert.Contains("Restrictions[1]", thrown.Message, StringComparison.Ordinal);
        }

        /// <summary>The list refuses a missing restriction where it is supplied.</summary>
        [Fact]
        public void ANullRestriction_IsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Restrictions = [null!],
            });

            Assert.Throws<ArgumentNullException>(() => new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Restrictions = null!,
            });
        }

        /// <summary>A path resolves ignoring case, as a path in the select list does.</summary>
        [Fact]
        public void APath_ResolvesIgnoringCase()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Restrictions = [new KeySetRestriction("CUSTOMER.region.ID", "@keys")],
            });

            Assert.Contains(
                "WHERE ([P2].[Id] IN (SELECT [Id] FROM @keys))\n",
                query.Sql,
                StringComparison.Ordinal);
        }

        /// <summary>
        ///     A restriction through a navigation another clause reads shares that clause's join, and
        ///     the join keeps the kind the navigation calls for.
        /// </summary>
        [Fact]
        public void ARestriction_SharesTheJoinsOfTheOtherClauses()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Customer.Name, Centre.Name",
                Restrictions = [new KeySetRestriction("Customer.Id", "@keys"), new KeySetRestriction("Centre.Id", "@centres")],
            });

            Assert.Equal(
                """
                SELECT
                    [P1].[AgentName] AS [c0],
                    [P2].[SegmentName] AS [c1]
                FROM [gl].[Documents] AS [T]
                LEFT JOIN [dbo].[Agents] AS [P1] ON [P1].[AgentId] = [T].[AgentFk]
                INNER JOIN [gl].[Segments] AS [P2] ON [P2].[SegmentId] = [T].[SegmentId]
                WHERE (([P1].[AgentId] IN (SELECT [Id] FROM @keys)) AND ([P2].[SegmentId] IN (SELECT [Id] FROM @centres)))
                ;

                """.ReplaceLineEndings("\n"),
                query.Sql);
        }

        /// <summary>A filter already known to be true adds nothing beside the restriction.</summary>
        [Fact]
        public void AFilterKnownToBeTrue_LeavesTheRestrictionAlone()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.And([]),
                Restrictions = [new KeySetRestriction("Id", "@keys")],
            });

            Assert.Contains(
                "WHERE ([T].[DocumentId] IN (SELECT [Id] FROM @keys))\n",
                query.Sql,
                StringComparison.Ordinal);
        }

        /// <summary>A restriction's joins count toward the join ceiling, and the problem names it.</summary>
        [Fact]
        public void ARestrictionsJoins_CountTowardTheCeiling()
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Restrictions = [new KeySetRestriction("Id", "@keys"), new KeySetRestriction("Customer.Region.Id", "@regions")],
                },
                Options() with { Limits = QueryexLimits.Default with { MaxJoins = 1 } });

            Assert.False(result.Succeeded);
            QueryexDiagnostic problem = Assert.Single(result.Diagnostics);
            Assert.Equal("QX5006", problem.Code);
            Assert.Equal("Restrictions[1]", problem.Location);
        }

        /// <summary>
        ///     A join a user's clause shares with a restriction is the user's: when it pushes the
        ///     count over the ceiling, the problem is reported on the clause the user wrote.
        /// </summary>
        [Fact]
        public void ASharedJoinOverTheCeiling_IsReportedOnTheUsersClause()
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Customer.Region.Name",
            };

            QueryCompilationOptions tight = Options() with { Limits = QueryexLimits.Default with { MaxJoins = 1 } };
            QueryexDiagnostic alone = Assert.Single(Engine.CompileQuery(spec, tight).Diagnostics);
            QueryexDiagnostic shared = Assert.Single(Engine.CompileQuery(
                spec with { Restrictions = [new KeySetRestriction("Customer.Region.Id", "@keys")] },
                tight).Diagnostics);

            Assert.Equal("QX5006", shared.Code);
            Assert.Equal("Select[0]", shared.Location);
            Assert.Equal(new QueryexSpan(0, 20), shared.Span);
            Assert.Equal((alone.Code, alone.Location, alone.Span), (shared.Code, shared.Location, shared.Span));
        }

        /// <summary>The restrictions are part of what a compilation is remembered under.</summary>
        [Fact]
        public void Restrictions_ArePartOfTheCacheKey()
        {
            QuerySpec plain = new() { Root = LedgerFixture.Invoice, Select = "Id" };
            QuerySpec one = plain with { Restrictions = [new KeySetRestriction("Id", "@keys")] };

            Assert.False(ProbeCacheKeys.ShareQueryEntry(plain, one, Options()));
            Assert.False(ProbeCacheKeys.ShareQueryEntry(
                one,
                plain with { Restrictions = [new KeySetRestriction("Id", "@other")] },
                Options()));
            Assert.False(ProbeCacheKeys.ShareQueryEntry(
                plain with { Restrictions = [new KeySetRestriction("Id", "@a"), new KeySetRestriction("CustomerId", "@b")] },
                plain with { Restrictions = [new KeySetRestriction("CustomerId", "@b"), new KeySetRestriction("Id", "@a")] },
                Options()));
            Assert.True(ProbeCacheKeys.ShareQueryEntry(
                one,
                plain with { Restrictions = [new KeySetRestriction("Id", "@keys")] },
                Options()));

            // And the compiled answers differ accordingly.
            Assert.DoesNotContain("@keys", Compile(plain).Sql, StringComparison.Ordinal);
            Assert.Contains("@keys", Compile(one).Sql, StringComparison.Ordinal);
        }

        /// <summary>Compiles a query, failing the test if it did not compile.</summary>
        /// <param name="spec">The query.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compile(QuerySpec spec)
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(spec, Options());
            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            return result.Value;
        }

        /// <summary>The options every compilation here runs under.</summary>
        /// <returns>The options.</returns>
        private static QueryCompilationOptions Options()
        {
            return new QueryCompilationOptions
            {
                QueryexVersion = QueryexLanguage.Version,
                Schema = LedgerFixture.Schema,
            };
        }
    }
}
