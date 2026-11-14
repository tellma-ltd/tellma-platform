// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     How the startup check classifies a failed login to the catalog: a refusal for the database
    ///     is a missing catalog, a login cut off before saying why is one only when the server says
    ///     the database is absent, and every other failure stands as a reachability fault.
    /// </summary>
    public class LoginFailureClassificationTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_refusal_for_the_database_is_a_missing_catalog_without_asking_the_server()
        {
            Question question = new(false);

            Assert.Equal(CatalogHistoryState.MissingDatabase, await ClassifyAsync([18456, 4060], question));
            Assert.Equal(CatalogHistoryState.MissingDatabase, await ClassifyAsync([4060, 18456], question));
            Assert.Equal(0, question.Asked);
        }

        [Fact]
        public async Task A_cut_off_login_is_a_missing_catalog_when_the_server_says_the_database_is_absent()
        {
            Question question = new(false);

            Assert.Equal(CatalogHistoryState.MissingDatabase, await ClassifyAsync([233], question));
            Assert.Equal(1, question.Asked);
        }

        [Fact]
        public async Task A_cut_off_login_stands_when_the_database_exists()
        {
            Question question = new(true);

            Assert.Null(await ClassifyAsync([233], question));
            Assert.Equal(1, question.Asked);
        }

        [Fact]
        public async Task A_cut_off_login_stands_when_the_server_cannot_tell()
        {
            Question question = new(answer: null);

            Assert.Null(await ClassifyAsync([233], question));
            Assert.Equal(1, question.Asked);
        }

        [Fact]
        public async Task A_cut_off_login_stands_when_the_server_cannot_be_asked()
        {
            Question question = new(new InvalidOperationException("The server cannot be reached."));

            Assert.Null(await ClassifyAsync([233], question));
            Assert.Equal(1, question.Asked);
        }

        [Fact]
        public async Task A_cancelled_question_cancels_the_classification()
        {
            Question question = new(new OperationCanceledException());

            _ = await Assert.ThrowsAsync<OperationCanceledException>(() => ClassifyAsync([233], question));
        }

        [Theory]
        [InlineData(18456)]
        [InlineData(-2)]
        [InlineData(53)]
        [InlineData(10054)]
        public async Task Any_other_failure_stands_without_asking_the_server(int number)
        {
            Question question = new(false);

            Assert.Null(await ClassifyAsync([number], question));
            Assert.Equal(0, question.Asked);
        }

        /// <summary>Classifies a failure with the given error numbers.</summary>
        /// <param name="numbers">The error numbers.</param>
        /// <param name="question">The existence question.</param>
        /// <returns>The classification.</returns>
        private static Task<CatalogHistoryState?> ClassifyAsync(int[] numbers, Question question)
        {
            return SqlCatalogReader.ClassifyLoginFailureAsync(numbers, question.AskAsync, Token);
        }

        /// <summary>An existence question with a scripted answer, or a scripted failure, that counts how often it was asked.</summary>
        private sealed class Question
        {
            /// <summary>The answer.</summary>
            private readonly bool? _answer;

            /// <summary>The failure, thrown instead of answering.</summary>
            private readonly Exception? _failure;

            /// <summary>Creates a question that answers.</summary>
            /// <param name="answer">The answer.</param>
            public Question(bool? answer)
            {
                _answer = answer;
            }

            /// <summary>Creates a question that fails.</summary>
            /// <param name="failure">The failure.</param>
            public Question(Exception failure)
            {
                _failure = failure;
            }

            /// <summary>How many times the question was asked.</summary>
            public int Asked { get; private set; }

            /// <summary>Asks the question.</summary>
            /// <param name="cancellationToken">Cancels the question.</param>
            /// <returns>The answer.</returns>
            public async Task<bool?> AskAsync(CancellationToken cancellationToken)
            {
                Asked++;
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                return _failure is null ? _answer : throw _failure;
            }
        }
    }
}
