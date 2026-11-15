// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Identity.Options;

namespace Tellma.Identity.Tests.Options
{
    /// <summary>
    ///     The document-client options: the defaults, the bounds startup validation holds them to,
    ///     and that no seeded client may take a client id that names a metadata document.
    /// </summary>
    public sealed class ClientMetadataOptionsTests
    {
        private readonly TellmaIdentityOptionsValidator _validator = new();

        [Fact]
        public void The_defaults_are_the_documented_ones_and_valid()
        {
            TellmaIdentityClientMetadataOptions defaults = new TellmaIdentityOptions().ClientMetadata;

            Assert.True(defaults.Enabled);
            Assert.Equal(5120, defaults.MaxBytes);
            Assert.Equal(TimeSpan.FromSeconds(5), defaults.Timeout);
            Assert.Equal(TimeSpan.FromMinutes(1), defaults.MinCache);
            Assert.Equal(TimeSpan.FromHours(24), defaults.MaxCache);
            Assert.Equal(10, defaults.FetchesPerHostPerMinute);
            Assert.Equal(10000, defaults.MaxClients);
            Assert.Equal(TimeSpan.FromHours(1), defaults.UnusedClientLifetime);
            Assert.Equal(ValidateOptionsResult.Success, _validator.Validate(null, ValidOptions()));
        }

        [Theory]
        [InlineData("MaxBytes")]
        [InlineData("MaxBytesTooLarge")]
        [InlineData("Timeout")]
        [InlineData("TimeoutTooLong")]
        [InlineData("MinCache")]
        [InlineData("MaxCache")]
        [InlineData("FetchesPerHostPerMinute")]
        [InlineData("MaxClients")]
        [InlineData("UnusedClientLifetime")]
        public void An_out_of_range_value_fails_startup(string setting)
        {
            TellmaIdentityOptions options = ValidOptions();
            TellmaIdentityClientMetadataOptions metadata = options.ClientMetadata;
            switch (setting)
            {
                case "MaxBytes":
                    metadata.MaxBytes = 0;
                    break;
                case "MaxBytesTooLarge":
                    metadata.MaxBytes = TellmaIdentityOptionsValidator.MaxClientMetadataBytes + 1;
                    break;
                case "Timeout":
                    metadata.Timeout = TimeSpan.Zero;
                    break;
                case "TimeoutTooLong":
                    metadata.Timeout = TellmaIdentityOptionsValidator.MaxClientMetadataTimeout + TimeSpan.FromSeconds(1);
                    break;
                case "MinCache":
                    metadata.MinCache = TimeSpan.Zero;
                    break;
                case "MaxCache":
                    metadata.MaxCache = metadata.MinCache - TimeSpan.FromSeconds(1);
                    break;
                case "FetchesPerHostPerMinute":
                    metadata.FetchesPerHostPerMinute = 0;
                    break;
                case "MaxClients":
                    metadata.MaxClients = -1;
                    break;
                default:
                    metadata.UnusedClientLifetime = TimeSpan.Zero;
                    break;
            }

            ValidateOptionsResult result = _validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static failure => failure.Contains("TellmaIdentity:ClientMetadata:", StringComparison.Ordinal));
        }

        [Fact]
        public void A_cap_of_zero_is_accepted_and_admits_no_new_document_client()
        {
            TellmaIdentityOptions options = ValidOptions();
            options.ClientMetadata.MaxClients = 0;

            Assert.Equal(ValidateOptionsResult.Success, _validator.Validate(null, options));
        }

        [Fact]
        public void A_seeded_client_id_naming_a_metadata_document_fails_startup()
        {
            TellmaIdentityOptions options = ValidOptions();
            options.Seed.Clients.Add(new TellmaIdentitySeedClientOptions
            {
                ClientId = "https://agent.example.com/client.json",
                Kind = TellmaIdentitySeedClientKind.Native,
                RedirectUris = { "http://127.0.0.1/callback" },
            });

            // Seed entries are validated by the start-up check, which knows the environment.
            ValidateOptionsResult result = new TellmaIdentityOptionsValidator(new ProductionEnvironment()).Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static failure => failure.Contains("metadata document", StringComparison.Ordinal));
        }

        /// <summary>A complete, production-shaped configuration the tests then change.</summary>
        private static TellmaIdentityOptions ValidOptions()
        {
            TellmaIdentityOptions options = new()
            {
                Issuer = new Uri("https://identity.example.com"),
                ConnectionString = "Server=.;Database=Id;Trusted_Connection=True",
            };
            options.Keys.Signing.Source = TellmaIdentityCertificateSourceKind.PfxFile;
            options.Keys.Signing.PfxFiles.Add(new TellmaIdentityPfxFileOptions { Path = "/etc/tellma/signing.pfx" });
            options.Keys.Encryption.Source = TellmaIdentityCertificateSourceKind.PfxFile;
            options.Keys.Encryption.PfxFiles.Add(new TellmaIdentityPfxFileOptions { Path = "/etc/tellma/encryption.pfx" });
            return options;
        }

        /// <summary>A production host environment.</summary>
        private sealed class ProductionEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
        {
            /// <inheritdoc />
            public string EnvironmentName { get; set; } = "Production";

            /// <inheritdoc />
            public string ApplicationName { get; set; } = "Tellma.Identity.Tests";

            /// <inheritdoc />
            public string ContentRootPath { get; set; } = string.Empty;

            /// <inheritdoc />
            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        }
    }
}
