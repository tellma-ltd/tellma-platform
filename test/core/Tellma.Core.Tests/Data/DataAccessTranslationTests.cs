// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Data;

namespace Tellma.Core.Tests.Data
{
    /// <summary>
    ///     The translation of the executor's availability failures outside the stack pipelines: an
    ///     exhausted retry and an ambiguous commit the probe did not find committed become the
    ///     database's unavailability, worth a retry after a second; a proven commit and every other
    ///     failure pass through as they are.
    /// </summary>
    public sealed class DataAccessTranslationTests
    {
        [Fact]
        public async Task An_exhausted_retry_becomes_the_database_s_unavailability()
        {
            DataAccessRetryExhaustedException exhausted = new(new TimeoutException("The lock wait timed out."), attempts: 3);

            DependencyUnavailableException unavailable = await Assert.ThrowsAsync<DependencyUnavailableException>(
                () => DataAccessTranslation.RunAsync(() => Task.FromException<int>(exhausted)));

            Assert.Equal(("database", TimeSpan.FromSeconds(1)), (unavailable.Dependency, unavailable.RetryAfter));
            Assert.Same(exhausted, unavailable.InnerException);
        }

        [Theory]
        [InlineData(CommitVerdict.NotCommitted)]
        [InlineData(CommitVerdict.Unknown)]
        public async Task An_ambiguous_commit_the_probe_did_not_find_becomes_the_database_s_unavailability(CommitVerdict verdict)
        {
            DataAccessAmbiguousException ambiguous = new(new TimeoutException("The connection dropped."), verdict);

            DependencyUnavailableException unavailable = await Assert.ThrowsAsync<DependencyUnavailableException>(
                () => DataAccessTranslation.RunAsync(() => Task.FromException(ambiguous)));

            Assert.Same(ambiguous, unavailable.InnerException);
        }

        [Fact]
        public async Task A_proven_commit_and_any_other_failure_pass_through()
        {
            DataAccessAmbiguousException committed = new(new TimeoutException("The connection dropped."), CommitVerdict.Committed);
            InvalidOperationException other = new("A defect.");

            Assert.Same(committed, await Assert.ThrowsAsync<DataAccessAmbiguousException>(() => DataAccessTranslation.RunAsync(() => Task.FromException(committed))));
            Assert.Same(other, await Assert.ThrowsAsync<InvalidOperationException>(() => DataAccessTranslation.RunAsync(() => Task.FromException<int>(other))));
            Assert.Null(DataAccessTranslation.Translate(committed));
            Assert.Equal(7, await DataAccessTranslation.RunAsync(static () => Task.FromResult(7)));
        }
    }
}
