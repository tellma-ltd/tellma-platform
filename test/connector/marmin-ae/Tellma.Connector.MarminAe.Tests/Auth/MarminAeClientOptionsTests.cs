// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.MarminAe.Tests.Auth
{
    /// <summary>What a client refuses to be built with.</summary>
    /// <remarks>
    ///     Every one of these is a deployment mistake rather than a runtime condition, so failing at
    ///     construction is the point: a misconfigured client that only fails on its first
    ///     transmission fails at the worst possible moment.
    /// </remarks>
    public class MarminAeClientOptionsTests
    {
        [Fact]
        public void Normalizes_a_base_address_that_has_no_trailing_slash()
        {
            MarminAeClientOptions options = Options(new Uri("https://api-sandbox.ae.marmin.ai"));

            Assert.Equal("https://api-sandbox.ae.marmin.ai/", options.NormalizedBaseAddress.AbsoluteUri);
        }

        [Fact]
        public void Keeps_a_base_address_that_already_ends_in_a_slash()
        {
            MarminAeClientOptions options = Options(new Uri("https://gateway.example.com/marmin/"));

            Assert.Equal("https://gateway.example.com/marmin/", options.NormalizedBaseAddress.AbsoluteUri);
        }

        [Fact]
        public void Refuses_a_relative_base_address_at_construction()
        {
            using HttpClient httpClient = new();
            MarminAeClientOptions options = Options(new Uri("/api", UriKind.Relative));

            // At construction, not on the first transmission: an e-invoicing client discovering its
            // own misconfiguration mid-submission is the worst possible moment for it.
            Assert.Throws<ArgumentException>(() => new MarminAeClient(httpClient, options));
            Assert.Throws<ArgumentException>(() => new MarminAeTokenProvider(httpClient, options));
        }

        [Theory]
        [InlineData("https://gateway.example.com/marmin?tenant=1")]
        [InlineData("https://gateway.example.com/marmin#fragment")]
        public void Refuses_a_base_address_a_route_could_not_be_appended_to(string baseAddress)
        {
            // Appending a relative route to either would land after the query or replace the
            // fragment, quietly dropping the prefix the gateway needs.
            using HttpClient httpClient = new();
            MarminAeClientOptions options = Options(new Uri(baseAddress));

            Assert.Throws<ArgumentException>(() => new MarminAeClient(httpClient, options));
        }

        [Fact]
        public void Keeps_a_path_prefix_when_it_adds_the_missing_slash()
        {
            MarminAeClientOptions options = Options(new Uri("https://gateway.example.com/marmin"));

            Assert.Equal(
                "https://gateway.example.com/marmin/", options.NormalizedBaseAddress.AbsoluteUri);
        }

        [Fact]
        public void Refuses_a_negative_token_expiry_margin()
        {
            using HttpClient httpClient = new();
            MarminAeClientOptions options = Options() with { TokenExpirySkew = TimeSpan.FromSeconds(-1) };

            Assert.Throws<ArgumentException>(() => new MarminAeClient(httpClient, options));
        }

        [Fact]
        public void Refuses_a_non_positive_timeout_on_the_token_provider_too()
        {
            using HttpClient httpClient = new();
            MarminAeClientOptions options = Options() with { Timeout = TimeSpan.Zero };

            Assert.Throws<ArgumentException>(() => new MarminAeTokenProvider(httpClient, options));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Refuses_a_blank_client_id(string clientId)
        {
            using HttpClient httpClient = new();
            MarminAeClientOptions options = Options() with { ClientId = clientId };

            Assert.Throws<ArgumentException>(() => new MarminAeClient(httpClient, options));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Refuses_a_blank_client_secret(string clientSecret)
        {
            using HttpClient httpClient = new();
            MarminAeClientOptions options = Options() with { ClientSecret = clientSecret };

            Assert.Throws<ArgumentException>(() => new MarminAeClient(httpClient, options));
        }

        [Fact]
        public void Refuses_a_non_positive_timeout()
        {
            using HttpClient httpClient = new();
            MarminAeClientOptions options = Options() with { Timeout = TimeSpan.Zero };

            Assert.Throws<ArgumentException>(() => new MarminAeClient(httpClient, options));
        }

        [Fact]
        public void Refuses_a_missing_transport()
        {
            Assert.Throws<ArgumentNullException>(() => new MarminAeClient(null!, Options()));
        }

        [Fact]
        public void Defaults_the_timings_the_vendor_does_not_dictate()
        {
            MarminAeClientOptions options = Options();

            // Both are part of the public contract, so a change to either is a deliberate one.
            Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
            Assert.Equal(TimeSpan.FromSeconds(60), options.TokenExpirySkew);
        }

        [Fact]
        public void Points_at_the_sandbox_host_the_vendor_publishes()
        {
            Assert.Equal(
                "https://api-sandbox.ae.marmin.ai/", MarminAeClientOptions.SandboxBaseAddress.AbsoluteUri);
        }

        private static MarminAeClientOptions Options(Uri? baseAddress = null)
        {
            return new MarminAeClientOptions
            {
                BaseAddress = baseAddress ?? MarminAeClientOptions.SandboxBaseAddress,
                ClientId = "org_test",
                ClientSecret = "sk_test",
            };
        }
    }
}
