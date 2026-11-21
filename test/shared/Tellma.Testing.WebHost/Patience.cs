// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace Tellma.Testing.WebHost
{
    /// <summary>
    ///     How a test waits for what a correct host does on its own: a signal an endpoint raises, a timer
    ///     the host arms, a request it answers, an event it sends. Such a wait has no deadline to meet,
    ///     since nothing in the scenario runs out while it waits, so its limit only ends a hang. The limit
    ///     is far past anything a loaded runner reaches: a cold host on a busy machine takes seconds
    ///     where an idle one takes milliseconds, and a limit of a few seconds turns that slowness into a
    ///     failure.
    /// </summary>
    internal static class Patience
    {
        /// <summary>How long a test waits before it calls a wait a hang: two minutes.</summary>
        public static readonly TimeSpan Limit = TimeSpan.FromMinutes(2);

        /// <summary>How often <see cref="UntilAsync" /> looks at its condition again.</summary>
        private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(10);

        /// <summary>Waits for a task, ending a hang at the limit or when the test is cancelled.</summary>
        /// <param name="task">The task.</param>
        /// <returns>A task that completes as the task does.</returns>
        /// <exception cref="TimeoutException">The task did not complete within the limit.</exception>
        public static Task WaitAsync(Task task)
        {
            ArgumentNullException.ThrowIfNull(task);
            return task.WaitAsync(Limit, TestContext.Current.CancellationToken);
        }

        /// <summary>Waits for a task's result, ending a hang at the limit or when the test is cancelled.</summary>
        /// <typeparam name="T">The result's type.</typeparam>
        /// <param name="task">The task.</param>
        /// <returns>The task's result.</returns>
        /// <exception cref="TimeoutException">The task did not complete within the limit.</exception>
        public static Task<T> WaitAsync<T>(Task<T> task)
        {
            ArgumentNullException.ThrowIfNull(task);
            return task.WaitAsync(Limit, TestContext.Current.CancellationToken);
        }

        /// <summary>Waits until a condition the host reaches on its own threads holds, looking again every few milliseconds.</summary>
        /// <param name="condition">The condition.</param>
        /// <param name="awaited">What the condition stands for, named when the wait ends in a hang.</param>
        /// <returns>A task that completes once the condition holds.</returns>
        /// <exception cref="TimeoutException">The condition did not hold within the limit.</exception>
        public static async Task UntilAsync(Func<bool> condition, string awaited)
        {
            ArgumentNullException.ThrowIfNull(condition);
            long started = Stopwatch.GetTimestamp();
            while (!condition())
            {
                if (Stopwatch.GetElapsedTime(started) > Limit)
                {
                    throw new TimeoutException(string.Create(CultureInfo.InvariantCulture, $"Waited {Limit} for {awaited}."));
                }

                await Task.Delay(Poll, TestContext.Current.CancellationToken);
            }
        }

        /// <summary>
        ///     Waits until an endpoint signals that it reached a point, such as that it runs, failing at
        ///     once, with the request's outcome, when the request it serves ends first: an endpoint that
        ///     never got there never signals, and waiting out the limit would hide why.
        /// </summary>
        /// <param name="signal">Completed by the endpoint once it reached the point.</param>
        /// <param name="request">The request the endpoint serves.</param>
        /// <returns>A task that completes once the endpoint signalled.</returns>
        /// <exception cref="InvalidOperationException">The request ended before the endpoint signalled.</exception>
        /// <exception cref="TimeoutException">Neither happened within the limit.</exception>
        public static async Task SignalledAsync(Task signal, Task<HttpResponseMessage> request)
        {
            ArgumentNullException.ThrowIfNull(signal);
            ArgumentNullException.ThrowIfNull(request);
            _ = await WaitAsync(Task.WhenAny(signal, request));
            if (!signal.IsCompleted)
            {
                // The request is complete: say how it ended, which is what went wrong.
                Exception? failure = request.Exception?.GetBaseException();
                int? status = request.Status == TaskStatus.RanToCompletion ? (int)(await request).StatusCode : null;
                string outcome = status is { } code
                    ? string.Create(CultureInfo.InvariantCulture, $"was answered {code}")
                    : $"failed with {failure?.GetType().Name ?? "a cancellation"}";
                throw new InvalidOperationException($"The request {outcome} before its endpoint signalled.", failure);
            }

            await signal;
        }
    }
}
