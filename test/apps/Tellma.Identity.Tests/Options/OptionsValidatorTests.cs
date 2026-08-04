// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Identity.Options;

namespace Tellma.Identity.Tests.Options
{
    /// <summary>Startup options validation guards insecure and incomplete configurations.</summary>
    public sealed class OptionsValidatorTests
    {
        private readonly TellmaIdentityOptionsValidator _validator = new();

        [Fact]
        public void A_complete_on_prem_configuration_is_accepted()
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
            options.Email.SmtpHost = "smtp.example.com";

            Assert.Equal(ValidateOptionsResult.Success, _validator.Validate(null, options));
        }

        [Fact]
        public void A_missing_issuer_fails()
        {
            TellmaIdentityOptions options = new() { ConnectionString = "x" };
            ConfigureDevKeysAndSink(options);

            Assert.True(_validator.Validate(null, options).Failed);
        }

        [Fact]
        public void Development_certificates_are_rejected_without_the_explicit_flag()
        {
            TellmaIdentityOptions options = new()
            {
                Issuer = new Uri("https://identity.example.com"),
                ConnectionString = "x",
            };
            options.Keys.Signing.Source = TellmaIdentityCertificateSourceKind.DevelopmentSelfSigned;
            options.Keys.Encryption.Source = TellmaIdentityCertificateSourceKind.DevelopmentSelfSigned;
            options.Email.SmtpHost = "smtp.example.com";

            // Development.AllowDevelopmentCertificates defaults to false.
            Assert.True(_validator.Validate(null, options).Failed);
        }

        [Fact]
        public void InProc_mode_requires_a_path_base_matching_the_issuer()
        {
            TellmaIdentityOptions options = new()
            {
                Mode = TellmaIdentityDeploymentMode.InProc,
                Issuer = new Uri("https://acme.app.tellma.com/id"),
                PathBase = "/id",
                ConnectionString = "x",
            };
            ConfigureDevKeysAndSink(options);

            Assert.Equal(ValidateOptionsResult.Success, _validator.Validate(null, options));
        }

        [Fact]
        public void InProc_mode_rejects_an_issuer_that_does_not_end_with_the_path_base()
        {
            TellmaIdentityOptions options = new()
            {
                Mode = TellmaIdentityDeploymentMode.InProc,
                Issuer = new Uri("https://acme.app.tellma.com/wrong"),
                PathBase = "/id",
                ConnectionString = "x",
            };
            ConfigureDevKeysAndSink(options);

            Assert.True(_validator.Validate(null, options).Failed);
        }

        [Fact]
        public void Missing_email_transport_fails_without_the_development_sink()
        {
            TellmaIdentityOptions options = new()
            {
                Issuer = new Uri("https://identity.example.com"),
                ConnectionString = "x",
            };
            options.Keys.Signing.Source = TellmaIdentityCertificateSourceKind.PfxFile;
            options.Keys.Signing.PfxFiles.Add(new TellmaIdentityPfxFileOptions { Path = "/s.pfx" });
            options.Keys.Encryption.Source = TellmaIdentityCertificateSourceKind.PfxFile;
            options.Keys.Encryption.PfxFiles.Add(new TellmaIdentityPfxFileOptions { Path = "/e.pfx" });

            // No SmtpHost and no email sink.
            Assert.True(_validator.Validate(null, options).Failed);
        }

        [Fact]
        public void A_blob_key_ring_without_a_key_vault_key_fails()
        {
            TellmaIdentityOptions options = ValidBaseOptions();
            options.DataProtection.BlobUri = new Uri("https://storage.example.com/keys/ring.xml");

            ValidateOptionsResult result = _validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("KeyVaultKeyUri", StringComparison.Ordinal));
        }

        [Fact]
        public void A_blob_key_ring_encrypted_with_a_key_vault_key_is_accepted()
        {
            TellmaIdentityOptions options = ValidBaseOptions();
            options.DataProtection.BlobUri = new Uri("https://storage.example.com/keys/ring.xml");
            options.DataProtection.KeyVaultKeyUri = new Uri("https://vault.example.com/keys/dp");

            Assert.Equal(ValidateOptionsResult.Success, _validator.Validate(null, options));
        }

        [Fact]
        public void An_http_issuer_fails_without_the_insecure_development_flag()
        {
            TellmaIdentityOptions options = ValidBaseOptions();
            options.Issuer = new Uri("http://identity.example.com");

            ValidateOptionsResult result = _validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("HTTPS", StringComparison.Ordinal));
        }

        [Fact]
        public void An_http_issuer_is_accepted_with_the_insecure_development_flag()
        {
            TellmaIdentityOptions options = ValidBaseOptions();
            options.Issuer = new Uri("http://localhost");
            options.Development.AllowInsecureHttp = true;

            Assert.Equal(ValidateOptionsResult.Success, _validator.Validate(null, options));
        }

        [Fact]
        public void A_standalone_issuer_with_a_path_segment_fails()
        {
            TellmaIdentityOptions options = ValidBaseOptions();
            options.Issuer = new Uri("https://identity.example.com/id");

            ValidateOptionsResult result = _validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("bare origin", StringComparison.Ordinal));
        }

        [Fact]
        public void Development_certificates_are_rejected_outside_the_development_environment()
        {
            // The opt-in flag is set, but the host environment is Production: the environment
            // guard must still refuse to boot.
            TellmaIdentityOptionsValidator validator = new(new FakeEnvironment("Production"));
            TellmaIdentityOptions options = new()
            {
                Issuer = new Uri("https://identity.example.com"),
                ConnectionString = "x",
            };
            ConfigureDevKeysAndSink(options);

            ValidateOptionsResult result = validator.Validate(null, options);

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("Development environment", StringComparison.Ordinal));
        }

        [Fact]
        public void Development_certificates_are_accepted_in_the_development_environment()
        {
            TellmaIdentityOptionsValidator validator = new(new FakeEnvironment("Development"));
            TellmaIdentityOptions options = new()
            {
                Issuer = new Uri("https://identity.example.com"),
                ConnectionString = "x",
            };
            ConfigureDevKeysAndSink(options);

            Assert.Equal(ValidateOptionsResult.Success, validator.Validate(null, options));
        }

        /// <summary>A complete, production-shaped configuration the negative tests then break.</summary>
        private static TellmaIdentityOptions ValidBaseOptions()
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
            options.Email.SmtpHost = "smtp.example.com";
            return options;
        }

        /// <summary>Configures development self-signed keys and the email sink for a dev-shaped options object.</summary>
        private static void ConfigureDevKeysAndSink(TellmaIdentityOptions options)
        {
            options.Development.AllowDevelopmentCertificates = true;
            options.Development.UseEmailSink = true;
            options.Keys.Signing.Source = TellmaIdentityCertificateSourceKind.DevelopmentSelfSigned;
            options.Keys.Encryption.Source = TellmaIdentityCertificateSourceKind.DevelopmentSelfSigned;
        }

        /// <summary>A minimal host environment exposing only the environment name.</summary>
        private sealed class FakeEnvironment(string environmentName) : Microsoft.Extensions.Hosting.IHostEnvironment
        {
            public string EnvironmentName { get; set; } = environmentName;

            public string ApplicationName { get; set; } = "Tellma.Identity.Tests";

            public string ContentRootPath { get; set; } = string.Empty;

            public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        }
    }
}
