// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The localization scope: entering, nesting and restoring on disposal, the flow of a scope
    ///     into the work it starts and never back out of it, and the request's localization beneath
    ///     every scope.
    /// </summary>
    public sealed class LocalizationScopeTests
    {
        private static readonly LocalizationContext English = new(NegotiatedCultures.Get("en", "GB"), new GregorianCalendarSystem(), TimeZoneInfo.Utc, 1);
        private static readonly LocalizationContext Arabic = new(NegotiatedCultures.Get("ar", "SA"), new UmAlQuraCalendarSystem(), TimeZoneInfo.Utc, 2);

        [Fact]
        public void Outside_every_scope_there_is_no_current_context()
        {
            Assert.Null(LocalizationScope.Current);
        }

        [Fact]
        public void A_scope_is_current_until_disposed()
        {
            IDisposable scope = LocalizationScope.Enter(English);
            Assert.Same(English, LocalizationScope.Current);

            scope.Dispose();
            Assert.Null(LocalizationScope.Current);
        }

        [Fact]
        public void An_inner_scope_wins_and_its_disposal_restores_the_outer()
        {
            using (LocalizationScope.Enter(English))
            {
                using (LocalizationScope.Enter(Arabic))
                {
                    Assert.Same(Arabic, LocalizationScope.Current);
                }

                Assert.Same(English, LocalizationScope.Current);
            }

            Assert.Null(LocalizationScope.Current);
        }

        [Fact]
        public void Disposing_a_scope_twice_restores_once()
        {
            using IDisposable outer = LocalizationScope.Enter(English);
            IDisposable inner = LocalizationScope.Enter(Arabic);

            inner.Dispose();
            inner.Dispose();

            Assert.Same(English, LocalizationScope.Current);
        }

        [Fact]
        public async Task A_scope_flows_into_awaited_work_and_a_scope_entered_there_never_flows_back()
        {
            using IDisposable scope = LocalizationScope.Enter(English);

            static async Task Inner()
            {
                // The outer scope reached the callee, across its own awaits.
                Assert.Same(English, LocalizationScope.Current);
                _ = LocalizationScope.Enter(Arabic);
                await Task.Yield();
                Assert.Same(Arabic, LocalizationScope.Current);
            }

            await Inner();
            await Task.Run(() => LocalizationScope.Enter(Arabic), TestContext.Current.CancellationToken);

            // Neither the callee's scope nor the task's, both left undisposed, reached the caller.
            Assert.Same(English, LocalizationScope.Current);
        }

        [Fact]
        public async Task Concurrent_flows_each_see_their_own_scope()
        {
            TaskCompletionSource bothEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int entered = 0;

            async Task<LocalizationContext?> In(LocalizationContext context)
            {
                using IDisposable scope = LocalizationScope.Enter(context);
                if (Interlocked.Increment(ref entered) == 2)
                {
                    bothEntered.SetResult();
                }

                await bothEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                return LocalizationScope.Current;
            }

            LocalizationContext?[] seen = await Task.WhenAll(Task.Run(() => In(English)), Task.Run(() => In(Arabic)));

            Assert.Same(English, seen[0]);
            Assert.Same(Arabic, seen[1]);
            Assert.Null(LocalizationScope.Current);
        }

        [Fact]
        public async Task The_requests_localization_lies_beneath_every_scope_and_stays_with_its_flow()
        {
            await Task.Run(
                () =>
                {
                    Assert.Null(LocalizationScope.Request);
                    _ = LocalizationScope.ApplyRequest(Arabic);

                    // Applying the request opens no scope.
                    Assert.Same(Arabic, LocalizationScope.Request);
                    Assert.Null(LocalizationScope.Current);

                    using (LocalizationScope.Enter(English))
                    {
                        Assert.Same(Arabic, LocalizationScope.Request);
                        Assert.Same(English, LocalizationScope.Current);
                    }
                },
                TestContext.Current.CancellationToken);

            Assert.Null(LocalizationScope.Request);
        }

        [Fact]
        public async Task Undoing_a_request_localization_restores_the_one_it_replaced_once()
        {
            await Task.Run(
                () =>
                {
                    // A worker running two units on one flow: the second applies nothing of its own.
                    IDisposable first = LocalizationScope.ApplyRequest(English);
                    IDisposable second = LocalizationScope.ApplyRequest(Arabic);
                    Assert.Same(Arabic, LocalizationScope.Request);

                    second.Dispose();
                    Assert.Same(English, LocalizationScope.Request);

                    // A second disposal does not undo the first application as well.
                    second.Dispose();
                    Assert.Same(English, LocalizationScope.Request);

                    first.Dispose();
                    Assert.Null(LocalizationScope.Request);
                },
                TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task Applying_no_request_localization_clears_it_until_undone()
        {
            await Task.Run(
                () =>
                {
                    _ = LocalizationScope.ApplyRequest(Arabic);
                    using (LocalizationScope.ApplyRequest(null))
                    {
                        Assert.Null(LocalizationScope.Request);
                    }

                    Assert.Same(Arabic, LocalizationScope.Request);
                },
                TestContext.Current.CancellationToken);
        }

        [Fact]
        public void A_null_scope_is_refused()
        {
            _ = Assert.Throws<ArgumentNullException>(() => LocalizationScope.Enter(null!));
        }
    }
}
