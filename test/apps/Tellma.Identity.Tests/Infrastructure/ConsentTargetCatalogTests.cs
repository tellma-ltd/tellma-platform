// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     The places a consent or device-approval screen names: a distribution as its host, a
    ///     tenant as the host and its id, hosts in ASCII form so a look-alike cannot pass for a
    ///     familiar one, and anything else exactly as requested.
    /// </summary>
    public sealed class ConsentTargetCatalogTests
    {
        [Theory]
        [InlineData("https://acme.example.com", "acme.example.com")]
        [InlineData("https://acme.example.com/17/mcp", "acme.example.com/17")]
        [InlineData("https://localhost:7110", "localhost:7110")]
        [InlineData("https://localhost:7110/2147483647/mcp", "localhost:7110/2147483647")]
        [InlineData("https://acme.example.com:443", "https://acme.example.com:443")]
        [InlineData("https://acme.example.com/017/mcp", "https://acme.example.com/017/mcp")]
        [InlineData("urn:tellma:identity", "urn:tellma:identity")]
        public void Each_resource_is_described_as_its_place(string resource, string expected)
        {
            Assert.Equal([expected], ConsentTargetCatalog.Describe([resource]));
        }

        [Fact]
        public void A_host_in_another_script_is_shown_in_its_ascii_form()
        {
            // The first letter is Cyrillic: rendered as Unicode it reads as the Latin "acme".
            Assert.Equal(["xn--cme-5cd.example.com"], ConsentTargetCatalog.Describe(["https://аcme.example.com"]));
        }

        [Fact]
        public void Places_are_listed_once_in_the_order_requested()
        {
            Assert.Equal(
                ["acme.example.com/18", "acme.example.com", "acme.example.com/17"],
                ConsentTargetCatalog.Describe(
                [
                    "https://acme.example.com/18/mcp",
                    "https://acme.example.com",
                    "https://acme.example.com/17/mcp",
                    "https://acme.example.com/18/mcp",
                ]));
        }

        [Fact]
        public void A_request_naming_no_resource_names_no_place()
        {
            Assert.Empty(ConsentTargetCatalog.Describe([]));
        }
    }
}
