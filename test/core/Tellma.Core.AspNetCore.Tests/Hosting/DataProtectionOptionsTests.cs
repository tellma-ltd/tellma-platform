// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Hosting
{
    /// <summary>
    ///     The Data Protection section's own rules: a cloud key ring comes as a blob and the Key Vault
    ///     key that wraps it, both absolute https URLs, and never beside a shared folder.
    /// </summary>
    public sealed class DataProtectionOptionsTests
    {
        private const string Blob = "https://acme.blob.core.windows.net/keys/ring.xml";

        private const string Key = "https://acme.vault.azure.net/keys/ring";

        [Theory]
        [InlineData(null, null, null)]
        [InlineData(null, null, "/var/keys")]
        [InlineData(Blob, Key, null)]
        public void A_whole_key_ring_is_accepted(string? blobUri, string? keyId, string? keyRingPath)
        {
            Assert.True(Validate(blobUri, keyId, keyRingPath).Succeeded);
        }

        [Theory]
        [InlineData(Blob, null, "BlobUri is set without KeyId")]
        [InlineData(null, Key, "KeyId is set without BlobUri")]
        [InlineData("http://acme.blob.core.windows.net/keys/ring.xml", Key, "BlobUri is 'http://acme.blob.core.windows.net/keys/ring.xml', which is not an absolute https URL")]
        [InlineData(Blob, "keys/ring", "KeyId is 'keys/ring', which is not an absolute https URL")]
        public void A_key_ring_configured_by_halves_is_refused(string? blobUri, string? keyId, string expected)
        {
            ValidateOptionsResult result = Validate(blobUri, keyId, keyRingPath: null);

            Assert.True(result.Failed);
            Assert.Contains(expected, result.FailureMessage, StringComparison.Ordinal);
        }

        [Fact]
        public void A_shared_folder_beside_a_blob_is_refused()
        {
            ValidateOptionsResult result = Validate(Blob, Key, "/var/keys");

            Assert.Contains("KeyRingPath and BlobUri are both set", result.FailureMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_host_s_options_check_reports_a_key_ring_by_halves()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(static options => options.Settings["Tellma:DataProtection:BlobUri"] = Blob);
            await using (app)
            {
                TellmaCompositionException exception = await Assert.ThrowsAsync<TellmaCompositionException>(
                    () => app.StartAsync(TestContext.Current.CancellationToken));
                Assert.Contains(exception.Problems, static problem => problem.Problem.Contains("BlobUri is set without KeyId", StringComparison.Ordinal));
            }
        }

        /// <summary>Validates one configuration of the section.</summary>
        /// <param name="blobUri">The blob.</param>
        /// <param name="keyId">The Key Vault key.</param>
        /// <param name="keyRingPath">The shared folder.</param>
        /// <returns>The result.</returns>
        private static ValidateOptionsResult Validate(string? blobUri, string? keyId, string? keyRingPath)
        {
            return new DataProtectionHostOptionsValidator().Validate(
                name: null,
                new DataProtectionHostOptions { BlobUri = blobUri, KeyId = keyId, KeyRingPath = keyRingPath });
        }
    }
}
