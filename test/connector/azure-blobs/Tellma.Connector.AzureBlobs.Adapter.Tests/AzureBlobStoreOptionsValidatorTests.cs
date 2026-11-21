// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.AzureBlobs.Tests
{
    /// <summary>
    ///     The Azure blob store's settings rules: exactly one way to reach the account, an HTTPS
    ///     endpoint, a container prefix the largest tenant id still extends to a valid container
    ///     name, and positive limits.
    /// </summary>
    public sealed class AzureBlobStoreOptionsValidatorTests
    {
        private const string Emulator = "UseDevelopmentStorage=true";

        [Fact]
        public void The_defaults_with_an_endpoint_are_valid()
        {
            Assert.Empty(AzureBlobStoreOptionsValidator.Describe(new AzureBlobStoreOptions { ServiceUri = new Uri("https://acme.blob.core.windows.net") }));
        }

        [Fact]
        public void The_defaults_with_a_connection_string_are_valid()
        {
            Assert.Empty(AzureBlobStoreOptionsValidator.Describe(new AzureBlobStoreOptions { ConnectionString = Emulator }));
        }

        [Fact]
        public void Neither_an_endpoint_nor_a_connection_string_is_refused()
        {
            string failure = Assert.Single(AzureBlobStoreOptionsValidator.Describe(new AzureBlobStoreOptions()));
            Assert.Contains("Neither Tellma:Blobs:Azure:ServiceUri nor Tellma:Blobs:Azure:ConnectionString", failure, StringComparison.Ordinal);
        }

        [Fact]
        public void Both_an_endpoint_and_a_connection_string_are_refused()
        {
            string failure = Assert.Single(AzureBlobStoreOptionsValidator.Describe(
                new AzureBlobStoreOptions { ServiceUri = new Uri("https://acme.blob.core.windows.net"), ConnectionString = Emulator }));
            Assert.Contains("are both set", failure, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("http://acme.blob.core.windows.net")]
        [InlineData("/relative")]
        public void An_endpoint_that_is_not_absolute_HTTPS_is_refused(string endpoint)
        {
            string failure = Assert.Single(AzureBlobStoreOptionsValidator.Describe(
                new AzureBlobStoreOptions { ServiceUri = new Uri(endpoint, UriKind.RelativeOrAbsolute) }));
            Assert.Contains("is not an absolute HTTPS address", failure, StringComparison.Ordinal);
            Assert.DoesNotContain(endpoint, failure, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("https://acme.blob.core.windows.net/?sv=2024-11-04&sig=c2VjcmV0", "https://acme.blob.core.windows.net")]
        [InlineData("https://acme.blob.core.windows.net/tenants", "https://acme.blob.core.windows.net")]
        [InlineData("https://acme.blob.core.windows.net/#part", "https://acme.blob.core.windows.net")]
        [InlineData("https://user:c2VjcmV0@acme.blob.core.windows.net/", "https://acme.blob.core.windows.net")]
        [InlineData("https://acme.example.test:8443/devstoreaccount1", "https://acme.example.test:8443")]
        public void An_endpoint_with_more_than_the_account_s_address_is_refused_naming_only_its_scheme_and_host(string endpoint, string shown)
        {
            string failure = Assert.Single(AzureBlobStoreOptionsValidator.Describe(new AzureBlobStoreOptions { ServiceUri = new Uri(endpoint) }));

            Assert.Contains($"Tellma:Blobs:Azure:ServiceUri for '{shown}' carries a path, a query, a fragment or user information", failure, StringComparison.Ordinal);
            Assert.DoesNotContain("c2VjcmV0", failure, StringComparison.Ordinal);
            Assert.DoesNotContain("sig=", failure, StringComparison.Ordinal);
            Assert.DoesNotContain("/tenants", failure, StringComparison.Ordinal);
        }

        [Fact]
        public void An_endpoint_with_a_trailing_slash_is_the_account_s_address()
        {
            Assert.Empty(AzureBlobStoreOptionsValidator.Describe(new AzureBlobStoreOptions { ServiceUri = new Uri("https://acme.blob.core.windows.net/") }));
        }

        [Theory]
        [InlineData("tellma-")]
        [InlineData("")]
        [InlineData("acme")]
        [InlineData("a1-b2-")]
        [InlineData("x123456789x123456789x123456789x123456789x123456789x1")]
        public void A_prefix_every_tenant_id_extends_to_a_valid_container_name_is_valid(string prefix)
        {
            Assert.Empty(AzureBlobStoreOptionsValidator.Describe(new AzureBlobStoreOptions { ConnectionString = Emulator, ContainerPrefix = prefix }));
        }

        [Theory]
        [InlineData("Tellma-")]
        [InlineData("-tellma")]
        [InlineData("tellma--")]
        [InlineData("tellma_")]
        [InlineData("tellma.")]
        [InlineData("x123456789x123456789x123456789x123456789x123456789x12")]
        public void A_prefix_that_yields_an_invalid_container_name_is_refused(string prefix)
        {
            string failure = Assert.Single(AzureBlobStoreOptionsValidator.Describe(new AzureBlobStoreOptions { ConnectionString = Emulator, ContainerPrefix = prefix }));
            Assert.Contains($"'{prefix}t2147483647'", failure, StringComparison.Ordinal);
        }

        [Fact]
        public void Limits_below_one_are_refused()
        {
            IReadOnlyList<string> failures = AzureBlobStoreOptionsValidator.Describe(
                new AzureBlobStoreOptions { ConnectionString = Emulator, MaxParallelism = 0, ReadBufferSize = 0 });

            Assert.Collection(
                failures,
                static failure => Assert.StartsWith("Tellma:Blobs:Azure:MaxParallelism is 0", failure, StringComparison.Ordinal),
                static failure => Assert.StartsWith("Tellma:Blobs:Azure:ReadBufferSize is 0", failure, StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("abc", true)]
        [InlineData("ab", false)]
        [InlineData("a-b", true)]
        [InlineData("a--b", false)]
        [InlineData("ab-", false)]
        [InlineData("aBc", false)]
        public void Container_names_follow_the_service_s_rules(string name, bool valid)
        {
            Assert.Equal(valid, AzureBlobStoreOptionsValidator.IsValidContainerName(name));
        }
    }
}
