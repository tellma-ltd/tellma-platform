// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net.Http.Headers;
using Tellma.Identity.Services.ClientMetadata;

namespace Tellma.Identity.Tests.Services.ClientMetadata
{
    /// <summary>
    ///     Redirect matching for document clients — exact, except that a loopback redirect matches
    ///     any port — and the cache lifetime a response's headers give, clamped to the window.
    /// </summary>
    public sealed class DocumentClientRedirectsTests
    {
        [Theory]
        [InlineData("http://127.0.0.1/callback", "http://127.0.0.1/callback")]
        [InlineData("http://127.0.0.1/callback", "http://127.0.0.1:51234/callback")]
        [InlineData("http://127.0.0.1:8400/callback", "http://127.0.0.1:51234/callback")]
        [InlineData("http://[::1]/callback", "http://[::1]:51234/callback")]
        [InlineData("http://localhost/callback", "http://localhost:51234/callback")]
        [InlineData("https://agent.example.com/cb", "https://agent.example.com/cb")]
        [InlineData("com.example.agent:/oauth/callback", "com.example.agent:/oauth/callback")]
        public void A_requested_redirect_matching_the_document_is_accepted(string documented, string requested)
        {
            Assert.True(DocumentClientRedirects.Matches([new Uri(documented)], requested));
        }

        [Theory]
        [InlineData("http://127.0.0.1/callback", "http://127.0.0.1:51234/other")]
        [InlineData("http://127.0.0.1/callback", "http://[::1]:51234/callback")]
        [InlineData("http://127.0.0.1/callback", "https://127.0.0.1:51234/callback")]
        [InlineData("http://127.0.0.1/callback", "http://127.0.0.1:51234/callback?x=1")]
        [InlineData("http://127.0.0.1/callback", "http://127.0.0.2:51234/callback")]
        [InlineData("https://agent.example.com/cb", "https://agent.example.com:8443/cb")]
        [InlineData("https://agent.example.com/cb", "https://agent.example.com/cb/")]
        [InlineData("https://agent.example.com/cb", "https://AGENT.example.com/cb")]
        public void Any_other_redirect_is_refused(string documented, string requested)
        {
            Assert.False(DocumentClientRedirects.Matches([new Uri(documented)], requested));
        }

        [Theory]
        [InlineData("http://127.0.0.1:8400/callback", "http://127.0.0.1/callback")]
        [InlineData("http://[::1]:8400/callback", "http://[::1]/callback")]
        [InlineData("http://127.0.0.1/callback", "http://127.0.0.1/callback")]
        [InlineData("https://agent.example.com:8443/cb", "https://agent.example.com:8443/cb")]
        public void A_loopback_redirect_is_stored_without_its_port(string documented, string stored)
        {
            Assert.Equal(stored, DocumentClientRedirects.ToStored(new Uri(documented)));
        }

        [Fact]
        public void Max_age_less_age_is_the_lifetime()
        {
            using HttpResponseMessage response = new();
            response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = TimeSpan.FromMinutes(10) };
            response.Headers.Age = TimeSpan.FromMinutes(4);

            Assert.Equal(TimeSpan.FromMinutes(6), ClientMetadataCacheLifetime.FromHeaders(response.Headers, null, DateTimeOffset.UnixEpoch));
        }

        [Fact]
        public void No_store_and_no_cache_ask_for_no_lifetime()
        {
            using HttpResponseMessage noStore = new();
            noStore.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true, MaxAge = TimeSpan.FromHours(1) };
            using HttpResponseMessage noCache = new();
            noCache.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, MaxAge = TimeSpan.FromHours(1) };

            Assert.Equal(TimeSpan.Zero, ClientMetadataCacheLifetime.FromHeaders(noStore.Headers, null, DateTimeOffset.UnixEpoch));
            Assert.Equal(TimeSpan.Zero, ClientMetadataCacheLifetime.FromHeaders(noCache.Headers, null, DateTimeOffset.UnixEpoch));
        }

        [Fact]
        public void Expires_less_date_is_the_lifetime_without_max_age()
        {
            DateTimeOffset date = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
            using HttpResponseMessage response = new() { Content = new StringContent("{}") };
            response.Headers.Date = date;
            response.Content.Headers.Expires = date.AddHours(2);

            Assert.Equal(TimeSpan.FromHours(2), ClientMetadataCacheLifetime.FromHeaders(response.Headers, response.Content.Headers, date.AddHours(1)));
        }

        [Fact]
        public void A_response_stating_nothing_has_no_lifetime()
        {
            using HttpResponseMessage response = new();

            Assert.Equal(TimeSpan.Zero, ClientMetadataCacheLifetime.FromHeaders(response.Headers, null, DateTimeOffset.UnixEpoch));
        }

        [Theory]
        [InlineData(0, 60)]
        [InlineData(30, 60)]
        [InlineData(600, 600)]
        [InlineData(86_400, 86_400)]
        [InlineData(10_000_000, 86_400)]
        public void The_lifetime_is_clamped_to_the_window(int seconds, int clamped)
        {
            Assert.Equal(
                TimeSpan.FromSeconds(clamped),
                ClientMetadataCacheLifetime.Clamp(TimeSpan.FromSeconds(seconds), TimeSpan.FromMinutes(1), TimeSpan.FromHours(24)));
        }
    }
}
