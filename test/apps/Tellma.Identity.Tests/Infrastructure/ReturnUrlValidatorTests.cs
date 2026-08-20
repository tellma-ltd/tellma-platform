// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>The return-URL validator accepts only safe local targets.</summary>
    public sealed class ReturnUrlValidatorTests
    {
        [Theory]
        [InlineData("/Identity/Account/Login")]
        [InlineData("/connect/authorize?client_id=acme")]
        [InlineData("/")]
        public void Local_urls_are_valid(string url)
        {
            Assert.True(ReturnUrlValidator.IsValid(url));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("https://evil.example.com/phish")]
        [InlineData("//evil.example.com")]
        [InlineData("/\\evil.example.com")]
        [InlineData("http://localhost/legit-but-absolute")]
        public void Non_local_or_protocol_relative_urls_are_rejected(string? url)
        {
            Assert.False(ReturnUrlValidator.IsValid(url));
        }

        [Fact]
        public void Sanitize_falls_back_for_unsafe_input()
        {
            Assert.Equal("/fallback", ReturnUrlValidator.Sanitize("//evil.example.com", "/fallback"));
            Assert.Equal("/connect/authorize", ReturnUrlValidator.Sanitize("/connect/authorize", "/fallback"));
        }
    }
}
