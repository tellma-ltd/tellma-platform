// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using System.Text;
using Tellma.Connector.MarminAe.Tests.Infrastructure;

namespace Tellma.Connector.MarminAe.Tests.Auth
{
    /// <summary>How long a token is treated as good for, given what the vendor said.</summary>
    /// <remarks>
    ///     <para>
    ///         The vendor publishes no sample token response and no type for the expiry it carries,
    ///         so what is asserted here is <em>tolerance</em>: every plausible encoding is accepted,
    ///         and none of them is claimed to be the one the vendor actually sends. The live suite
    ///         attaches the real body, which is what will eventually narrow this.
    ///     </para>
    ///     <para>
    ///         Getting the expiry wrong is cheap by design — the single re-authentication retry is
    ///         what makes it correct, and a misread only costs a round trip — so every path here
    ///         yields a usable lifetime rather than a failure.
    ///     </para>
    /// </remarks>
    public class MarminAeTokenExpiryTests
    {
        private static readonly DateTimeOffset Now = new(2026, 5, 7, 9, 0, 0, TimeSpan.Zero);

        public static TheoryData<string, int> AcceptedEncodings()
        {
            // Each row: the JSON value of expires_at, and how many seconds past the harness clock it
            // should be understood to mean.
            DateTimeOffset expiry = Now.AddSeconds(300);

            return new TheoryData<string, int>
            {
                { expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), 300 },
                { expiry.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), 300 },
                { "\"" + expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + "\"", 300 },
                { "\"" + expiry.ToString("O", CultureInfo.InvariantCulture) + "\"", 300 },
                { "\"2026-05-07T09:05:00Z\"", 300 },
                { "\"2026-05-07T13:05:00+04:00\"", 300 },
                { "\"2026-05-07T09:05:00\"", 300 },
                { "300", 300 },
            };
        }

        [Theory]
        [MemberData(nameof(AcceptedEncodings))]
        public async Task Accepts_every_plausible_encoding_of_the_expiry(string expiresAt, int expectedSeconds)
        {
            using var harness = MarminAeHarness.Create();
            harness.OnToken((_, _) => Task.FromResult(MarminAeResponses.Json(
                HttpStatusCode.OK,
                $$"""{"token":"t","expires_at":{{expiresAt}}}""")));

            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            // One second short of the refresh margin the cache still holds; one second past it the
            // provider goes back to the wire. Together those pin where the expiry landed.
            harness.Time.Advance(TimeSpan.FromSeconds(expectedSeconds - 61));
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, harness.TokenRequestCount);

            harness.Time.Advance(TimeSpan.FromSeconds(2));
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Reads_a_lifetime_from_expires_in_when_there_is_no_expires_at()
        {
            using var harness = MarminAeHarness.Create();
            harness.OnToken((_, _) => Task.FromResult(MarminAeResponses.Json(
                HttpStatusCode.OK, /*lang=json,strict*/ """{"token":"t","expires_in":600}""")));

            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            harness.Time.Advance(TimeSpan.FromSeconds(538));
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Falls_back_to_the_tokens_own_expiry_claim()
        {
            // The envelope says nothing readable, but the token is a JWT and states its own lifetime.
            string token = Jwt(Now.AddSeconds(900));
            using var harness = MarminAeHarness.Create();
            harness.OnToken((_, _) => Task.FromResult(MarminAeResponses.Json(
                HttpStatusCode.OK, $$"""{"token":"{{token}}","expires_at":"not-a-time"}""")));

            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            harness.Time.Advance(TimeSpan.FromSeconds(838));
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Treats_an_unreadable_expiry_as_a_short_lifetime_rather_than_a_failure()
        {
            using var harness = MarminAeHarness.Create();
            harness.OnToken((_, _) => Task.FromResult(MarminAeResponses.Json(
                HttpStatusCode.OK, /*lang=json,strict*/ """{"token":"opaque-not-a-jwt"}""")));

            string token = await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal("opaque-not-a-jwt", token);

            // Five minutes, minus the refresh margin: still cached at four, back to the wire at six.
            harness.Time.Advance(TimeSpan.FromSeconds(180));
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            Assert.Equal(1, harness.TokenRequestCount);

            harness.Time.Advance(TimeSpan.FromSeconds(180));
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            Assert.Equal(2, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Buys_a_usable_instant_for_a_token_that_arrives_already_stale()
        {
            // An expiry inside the refresh margin on arrival would otherwise be discarded and
            // re-requested on every single call, which is a request storm rather than a refresh.
            using var harness = MarminAeHarness.Create();
            harness.OnToken((ordinal, _) => Task.FromResult(MarminAeResponses.Token(
                "token-" + ordinal.ToString(CultureInfo.InvariantCulture),
                TimeSpan.FromSeconds(5))));

            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal(1, harness.TokenRequestCount);
        }

        [Fact]
        public async Task Refuses_to_believe_an_absurdly_distant_expiry()
        {
            using var harness = MarminAeHarness.Create();
            harness.OnToken((ordinal, _) => Task.FromResult(MarminAeResponses.Token(
                "token-" + ordinal.ToString(CultureInfo.InvariantCulture),
                TimeSpan.FromDays(5 * 365))));

            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);
            harness.Time.Advance(TimeSpan.FromDays(2));
            await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, harness.TokenRequestCount);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("null")]
        [InlineData("true")]
        [InlineData("{}")]
        [InlineData("[]")]
        [InlineData("\"\"")]
        [InlineData("\"not-a-time\"")]
        public async Task Never_fails_over_an_expiry_it_cannot_read(string expiresAt)
        {
            using var harness = MarminAeHarness.Create();
            harness.OnToken((_, _) => Task.FromResult(MarminAeResponses.Json(
                HttpStatusCode.OK, $$"""{"token":"t","expires_at":{{expiresAt}}}""")));

            Assert.Equal("t", await harness.TokenProvider.GetTokenAsync(TestContext.Current.CancellationToken));
        }

        private static string Jwt(DateTimeOffset expiry)
        {
            string payload = Base64Url(
                Encoding.UTF8.GetBytes(
                    string.Create(
                        CultureInfo.InvariantCulture, $$"""{"exp":{{expiry.ToUnixTimeSeconds()}}}""")));

            return string.Concat(Base64Url("{}"u8.ToArray()), ".", payload, ".", Base64Url([1, 2, 3]));
        }

        private static string Base64Url(byte[] value)
        {
            return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
