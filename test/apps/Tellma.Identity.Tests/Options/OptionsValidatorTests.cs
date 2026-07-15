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

        /// <summary>Configures development self-signed keys and the email sink for a dev-shaped options object.</summary>
        private static void ConfigureDevKeysAndSink(TellmaIdentityOptions options)
        {
            options.Development.AllowDevelopmentCertificates = true;
            options.Development.UseEmailSink = true;
            options.Keys.Signing.Source = TellmaIdentityCertificateSourceKind.DevelopmentSelfSigned;
            options.Keys.Encryption.Source = TellmaIdentityCertificateSourceKind.DevelopmentSelfSigned;
        }
    }
}
