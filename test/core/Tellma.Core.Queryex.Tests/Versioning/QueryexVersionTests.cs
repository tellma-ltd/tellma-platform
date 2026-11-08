// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Versioning
{
    /// <summary>
    ///     The stamp a host stores with an expression and supplies back when that expression is
    ///     validated or compiled again.
    /// </summary>
    /// <remarks>
    ///     Only one version exists, so nothing here can yet show two versions compiling the same
    ///     text differently. What it can show is that the engine asks for the stamp, refuses one it
    ///     cannot honour, and already tells two versions apart everywhere the answer is
    ///     remembered — which is what has to be true before a second version can be added without
    ///     rewriting the callers or the caches.
    /// </remarks>
    public sealed class QueryexVersionTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>
        ///     The supported range is what the engine says it is, at both ends.
        /// </summary>
        [Fact]
        public void TheSupportedRange_HasBothItsEnds()
        {
            Assert.True(QueryexLanguage.IsSupported(QueryexLanguage.Version));
            Assert.True(QueryexLanguage.IsSupported(QueryexLanguage.Minimum));

            // Below the floor is text this engine can no longer honour; above the ceiling is text a
            // newer engine wrote under rules this one does not have. Optimism in either direction
            // compiles the text under whatever rules happen to be here.
            Assert.False(QueryexLanguage.IsSupported(QueryexLanguage.Minimum - 1));
            Assert.False(QueryexLanguage.IsSupported(QueryexLanguage.Version + 1));
            Assert.False(QueryexLanguage.IsSupported(0));
            Assert.False(QueryexLanguage.IsSupported(-1));
        }

        /// <summary>
        ///     A version this engine does not implement is refused where it is supplied, not
        ///     carried inward to be discovered by whatever reads it first.
        /// </summary>
        /// <param name="version">The unsupported version.</param>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        public void AnUnsupportedVersion_IsRefusedWhereItIsSupplied(int version)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ValidationOptions
                {
                    QueryexVersion = version,
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = QueryexMode.Value,
                });

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new QueryCompilationOptions
                {
                    QueryexVersion = version,
                    Schema = LedgerFixture.Schema,
                });

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DiscoveryOptions { QueryexVersion = version });
        }

        /// <summary>
        ///     An unsupported version is a caller error rather than a diagnostic: the text may be
        ///     perfectly well-formed, and what is wrong is the pairing of this engine with data it
        ///     cannot faithfully compile.
        /// </summary>
        [Fact]
        public void AnUnsupportedVersion_IsNotADiagnostic()
        {
            ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
                () => new ValidationOptions
                {
                    QueryexVersion = QueryexLanguage.Version + 1,
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = QueryexMode.Value,
                });

            Assert.Equal(QueryexLanguage.Version + 1, thrown.ActualValue);
        }

        /// <summary>
        ///     Discovery defaults to the current version, because it answers a question about text
        ///     being written now and its answer is never stored. The two entry points whose answers
        ///     outlive the call have no default at all.
        /// </summary>
        [Fact]
        public void Discovery_DefaultsToTheCurrentVersion()
        {
            Assert.Equal(QueryexLanguage.Version, new DiscoveryOptions().QueryexVersion);

            // Settable all the same: a host reopening a stored expression for editing describes it
            // under the rules it was written against, not under today's.
            DiscoveryOptions reopened = new() { QueryexVersion = QueryexLanguage.Minimum };
            Assert.Equal(QueryexLanguage.Minimum, reopened.QueryexVersion);
        }

        /// <summary>
        ///     The version a host supplies is the version the work is done under, and supplying the
        ///     current one changes nothing about the answer.
        /// </summary>
        [Fact]
        public void TheCurrentVersion_CompilesAsItAlwaysHas()
        {
            QueryexResult<ValidatedExpression> validated = Engine.Validate(
                "Amount + 1",
                new ValidationOptions
                {
                    QueryexVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = QueryexMode.Value,
                });

            Assert.True(validated.Succeeded);

            QueryexResult<CompiledQuery> compiled = Engine.CompileQuery(
                new QuerySpec { Root = LedgerFixture.Invoice, Select = "Amount" },
                new QueryCompilationOptions
                {
                    QueryexVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                });

            Assert.True(compiled.Succeeded);
        }

        /// <summary>
        ///     Two versions are told apart by the key a bound tree is remembered under, before
        ///     there are two versions to tell apart.
        /// </summary>
        /// <remarks>
        ///     Pinned through a probe because the options records refuse any version this engine
        ///     does not implement, so the difference cannot be produced through the public surface
        ///     while only one exists. It is exactly the case that stops being hypothetical on the
        ///     day a second version ships, and by then a cache that never distinguished them would
        ///     already be serving one version's tree for the other's request.
        /// </remarks>
        [Fact]
        public void TwoVersions_AreDifferentCacheEntries()
        {
            Assert.True(ProbeCacheKeys.DistinguishesQueryexVersions(
                QueryexLanguage.Version,
                QueryexLanguage.Version + 1,
                LedgerFixture.Schema,
                LedgerFixture.Invoice));
        }

        /// <summary>
        ///     A leaf stamped with a version this engine does not compile is refused when it is
        ///     built, below the floor and above the ceiling alike.
        /// </summary>
        /// <param name="version">The unsupported version.</param>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(QueryexLanguage.Version + 1)]
        public void AStampedLeaf_WithAnUnsupportedVersion_IsRefused(int version)
        {
            ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
                () => FilterTree.Leaf("IsPosted", version));

            Assert.Equal(version, thrown.ActualValue);
            Assert.Equal("queryexVersion", thrown.ParamName);
        }

        /// <summary>A stamped leaf still refuses empty text, as the unstamped form does.</summary>
        [Fact]
        public void AStampedLeaf_WithEmptyText_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => FilterTree.Leaf("  ", QueryexLanguage.Version));
        }

        /// <summary>A leaf carries the stamp it was built with, and the one-argument form none.</summary>
        [Fact]
        public void ALeaf_CarriesItsStamp()
        {
            var stamped = (FilterTree.LeafNode)FilterTree.Leaf("IsPosted", QueryexLanguage.Minimum);
            var unstamped = (FilterTree.LeafNode)FilterTree.Leaf("IsPosted");

            Assert.Equal(QueryexLanguage.Minimum, stamped.QueryexVersion);
            Assert.Null(unstamped.QueryexVersion);
        }

        /// <summary>
        ///     A tree mixing stamped and unstamped leaves compiles, and while one version exists it
        ///     compiles to exactly what the unstamped tree does.
        /// </summary>
        [Fact]
        public void ATreeMixingBothForms_Compiles()
        {
            QuerySpec mixed = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.And(
                [
                    FilterTree.Leaf("IsPosted", QueryexLanguage.Version),
                    FilterTree.Or([FilterTree.Leaf("Amount > 0"), FilterTree.Leaf("Count > 3", QueryexLanguage.Minimum)]),
                ]),
            };

            QuerySpec plain = mixed with
            {
                Filter = FilterTree.And(
                [
                    FilterTree.Leaf("IsPosted"),
                    FilterTree.Or([FilterTree.Leaf("Amount > 0"), FilterTree.Leaf("Count > 3")]),
                ]),
            };

            QueryexResult<CompiledQuery> stamped = Engine.CompileQuery(mixed, Options());
            QueryexResult<CompiledQuery> unstamped = Engine.CompileQuery(plain, Options());

            Assert.True(stamped.Succeeded);
            Assert.True(unstamped.Succeeded);
            Assert.Equal(unstamped.Value.Sql, stamped.Value.Sql);
        }

        /// <summary>
        ///     Two trees that differ only in one leaf's stamp are different compilations: once a
        ///     second version exists, the stamp changes what the leaf binds to.
        /// </summary>
        [Fact]
        public void TreesDifferingOnlyInALeafStamp_AreDifferentCacheEntries()
        {
            QuerySpec unstamped = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("IsPosted"),
            };

            QuerySpec stamped = unstamped with { Filter = FilterTree.Leaf("IsPosted", QueryexLanguage.Version) };

            Assert.False(ProbeCacheKeys.ShareQueryEntry(unstamped, stamped, Options()));
            Assert.True(ProbeCacheKeys.ShareQueryEntry(
                stamped,
                unstamped with { Filter = FilterTree.Leaf("IsPosted", QueryexLanguage.Version) },
                Options()));
        }

        /// <summary>
        ///     When a query is compiled, a stamped leaf binds under its own stamp and an unstamped
        ///     one under the version the compilation names.
        /// </summary>
        /// <remarks>
        ///     Pinned through a probe with a stamp this engine does not have, because while one
        ///     version exists the bound trees are identical and nothing public could tell the two
        ///     bindings apart.
        /// </remarks>
        [Fact]
        public void ACompiledLeaf_BindsUnderItsOwnStamp()
        {
            var tree = FilterTree.And(
            [
                ProbeLeafBinding.StampedLeaf("IsPosted", QueryexLanguage.Version + 1),
                FilterTree.Not(FilterTree.Leaf("Amount > 0")),
            ]);

            Assert.Equal(
                [
                    new ProbeLeafVersion("IsPosted", QueryexLanguage.Version + 1),
                    new ProbeLeafVersion("Amount > 0", QueryexLanguage.Version),
                ],
                ProbeLeafBinding.Compiled(tree, QueryexLanguage.Version, LedgerFixture.Schema, LedgerFixture.Invoice));
        }

        /// <summary>
        ///     When a query is discovered, a stamped leaf binds under its own stamp and an unstamped
        ///     one under the version the discovery names.
        /// </summary>
        [Fact]
        public void ADiscoveredLeaf_BindsUnderItsOwnStamp()
        {
            var tree = FilterTree.Or(
            [
                FilterTree.Leaf("Amount > 0"),
                ProbeLeafBinding.StampedLeaf("IsPosted", QueryexLanguage.Version + 1),
            ]);

            Assert.Equal(
                [
                    new ProbeLeafVersion("Amount > 0", QueryexLanguage.Version),
                    new ProbeLeafVersion("IsPosted", QueryexLanguage.Version + 1),
                ],
                ProbeLeafBinding.Discovered(tree, LedgerFixture.Schema, LedgerFixture.Invoice));
        }

        /// <summary>Discovery reads a stamped leaf as readily as an unstamped one.</summary>
        [Fact]
        public void Discovery_ReadsAStampedLeaf()
        {
            DiscoveryResult result = Engine.DiscoverQuery(
                new QueryDiscoverySpec
                {
                    Select = "Id",
                    Filter = FilterTree.Leaf("PostingDate >= @From", QueryexLanguage.Version),
                },
                new DiscoveryOptions { Schema = LedgerFixture.Schema, Root = LedgerFixture.Invoice });

            ParameterUse from = Assert.Single(result.Parameters);
            Assert.Equal(QueryexType.QxDate, from.InferredType);
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
