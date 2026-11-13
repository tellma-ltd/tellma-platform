// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Net;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>How one of the rotating authority's next answers comes back, after the refresh token was spent.</summary>
    internal enum RotatingAnswer
    {
        /// <summary>Not before the caller gives up.</summary>
        Stalled,

        /// <summary>As a success whose body is not a token response.</summary>
        Malformed,
    }

    /// <summary>
    ///     The refresh grant as the platform's identity server answers it: each redemption spends the
    ///     refresh token presented and issues a new one, and a spent token is accepted again only within
    ///     the reuse leeway of its first redemption; past it, the token is refused.
    /// </summary>
    /// <param name="time">The clock the leeway runs on.</param>
    /// <param name="leeway">How long a spent refresh token is accepted again.</param>
    internal sealed class RotatingAuthority(TimeProvider time, TimeSpan leeway)
    {
        /// <summary>When each refresh token was first redeemed.</summary>
        private readonly ConcurrentDictionary<string, DateTimeOffset> _spent = new(StringComparer.Ordinal);

        /// <summary>How many redemptions were refused.</summary>
        private int _refusals;

        /// <summary>How many redemptions were refused because their token was spent beyond the leeway.</summary>
        public int Refusals => Volatile.Read(ref _refusals);

        /// <summary>How the next answers come back, in order; past the queue, tokens are answered at once.</summary>
        public ConcurrentQueue<RotatingAnswer> Answers { get; } = new();

        /// <summary>The token response for a new refresh token; the fake identity server's fresh tokens by default.</summary>
        public Func<string, HttpResponseMessage> Issue { get; set; } =
            static refreshToken => FakeIdentityServer.Tokens("sub-1", nonce: null, TimeSpan.FromHours(1), refreshToken: refreshToken);

        /// <summary>Answers a refresh grant.</summary>
        /// <param name="form">The grant's form.</param>
        /// <param name="cancellationToken">The caller's cancellation.</param>
        /// <returns>The answer.</returns>
        public async Task<HttpResponseMessage?> AnswerAsync(IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
        {
            // --- The token is spent on its first redemption, and refused once the leeway has passed ---
            string token = form.GetValueOrDefault("refresh_token") ?? string.Empty;
            DateTimeOffset now = time.GetUtcNow();
            DateTimeOffset spentAt = _spent.GetOrAdd(token, now);
            if (now - spentAt > leeway)
            {
                _ = Interlocked.Increment(ref _refusals);
                return FakeIdentityServer.Json(HttpStatusCode.BadRequest, /*lang=json,strict*/ """{"error":"invalid_grant"}""");
            }

            // --- The answer, which may never arrive or arrive unusable ---
            string next = "refresh-" + Guid.NewGuid().ToString("N");
            if (Answers.TryDequeue(out RotatingAnswer answer))
            {
                if (answer == RotatingAnswer.Stalled)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                return FakeIdentityServer.Json(HttpStatusCode.OK, "<html></html>");
            }

            return Issue(next);
        }
    }
}
