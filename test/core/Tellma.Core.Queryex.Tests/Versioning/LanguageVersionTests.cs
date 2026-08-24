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
    public sealed class LanguageVersionTests
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
                    LanguageVersion = version,
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = QueryexMode.Value,
                });

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new QueryCompilationOptions
                {
                    LanguageVersion = version,
                    Schema = LedgerFixture.Schema,
                });

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DiscoveryOptions { LanguageVersion = version });
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
                    LanguageVersion = QueryexLanguage.Version + 1,
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
            Assert.Equal(QueryexLanguage.Version, new DiscoveryOptions().LanguageVersion);

            // Settable all the same: a host reopening a stored expression for editing describes it
            // under the rules it was written against, not under today's.
            DiscoveryOptions reopened = new() { LanguageVersion = QueryexLanguage.Minimum };
            Assert.Equal(QueryexLanguage.Minimum, reopened.LanguageVersion);
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
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = QueryexMode.Value,
                });

            Assert.True(validated.Succeeded);

            QueryexResult<CompiledQuery> compiled = Engine.CompileQuery(
                new QuerySpec { Root = LedgerFixture.Invoice, Select = "Amount" },
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
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
            Assert.True(ProbeCacheKeys.DistinguishesLanguageVersions(
                QueryexLanguage.Version,
                QueryexLanguage.Version + 1,
                LedgerFixture.Schema,
                LedgerFixture.Invoice));
        }
    }
}
