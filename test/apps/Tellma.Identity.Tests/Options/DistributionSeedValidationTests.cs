// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Identity.Options;

namespace Tellma.Identity.Tests.Options
{
    /// <summary>
    ///     Startup validation of <c>Distribution</c> seed entries: the slug, the origin, the
    ///     back-channel endpoint and, outside Development, both secrets. Every failure names its
    ///     entry, no two entries may register the same client, and only a <c>Native</c> entry may
    ///     be third-party.
    /// </summary>
    public sealed class DistributionSeedValidationTests
    {
        private static readonly TellmaIdentityOptionsValidator Production = new(new FakeEnvironment(Environments.Production));
        private static readonly TellmaIdentityOptionsValidator Development = new(new FakeEnvironment(Environments.Development));

        [Fact]
        public void A_complete_entry_is_accepted_in_production()
        {
            Assert.Equal(ValidateOptionsResult.Success, Production.Validate(null, With(Entry())));
        }

        [Theory]
        [InlineData("acme-eu-2")]
        [InlineData("a")]
        [InlineData("abcdefghijklmno")]
        public void A_slug_of_lowercase_letters_digits_and_hyphens_is_accepted(string slug)
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.ClientId = slug;

            Assert.Equal(ValidateOptionsResult.Success, Production.Validate(null, With(entry)));
        }

        [Theory]
        [InlineData("abcdefghijklmnop")]
        [InlineData("Acme")]
        [InlineData("1acme")]
        [InlineData("-acme")]
        [InlineData("acme_eu")]
        [InlineData("acme.eu")]
        public void A_slug_outside_the_grammar_fails_naming_the_entry(string slug)
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.ClientId = slug;

            ValidateOptionsResult result = Production.Validate(null, With(entry));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, f => f.Contains($"TellmaIdentity:Seed:Clients:0 ('{slug}')", StringComparison.Ordinal)
                && f.Contains("slug", StringComparison.Ordinal));
        }

        [Fact]
        public void A_missing_origin_fails()
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.Origin = null;

            ValidateOptionsResult result = Production.Validate(null, With(entry));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("Origin is required", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("https://acme.example.com/app")]
        [InlineData("https://acme.example.com/?x=1")]
        [InlineData("https://acme.example.com/#top")]
        [InlineData("https://user@acme.example.com")]
        [InlineData("acme.example.com")]
        [InlineData("ftp://acme.example.com")]
        public void An_origin_that_is_not_a_bare_https_origin_fails(string origin)
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.Origin = origin;

            ValidateOptionsResult result = Production.Validate(null, With(entry));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("('acme'): Origin must", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("https://acme.example.com")]
        [InlineData("https://acme.example.com/")]
        [InlineData("https://acme.example.com:8443")]
        public void A_bare_https_origin_is_accepted(string origin)
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.Origin = origin;

            Assert.Equal(ValidateOptionsResult.Success, Production.Validate(null, With(entry)));
        }

        [Fact]
        public void An_http_origin_fails_outside_development()
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.Origin = "http://localhost:7130";

            Assert.True(Production.Validate(null, With(entry)).Failed);
        }

        [Fact]
        public void An_http_origin_is_accepted_in_development()
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.Origin = "http://localhost:7130";

            Assert.Equal(ValidateOptionsResult.Success, Development.Validate(null, With(entry)));
        }

        [Fact]
        public void A_relative_back_channel_endpoint_fails()
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.BackchannelLogoutUri = "/bff/backchannel-logout";

            ValidateOptionsResult result = Production.Validate(null, With(entry));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("BackchannelLogoutUri", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void A_missing_secret_fails_outside_development(bool clientSecretMissing, bool serviceSecretMissing)
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.ClientSecret = clientSecretMissing ? " " : entry.ClientSecret;
            entry.ServiceClientSecret = serviceSecretMissing ? null : entry.ServiceClientSecret;

            ValidateOptionsResult result = Production.Validate(null, With(entry));

            Assert.True(result.Failed);
            string missing = clientSecretMissing ? "ClientSecret is required" : "ServiceClientSecret is required";
            Assert.Contains(result.Failures!, f => f.Contains(missing, StringComparison.Ordinal)
                && f.Contains("('acme')", StringComparison.Ordinal));
        }

        [Fact]
        public void Missing_secrets_are_accepted_in_development()
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.ClientSecret = null;
            entry.ServiceClientSecret = null;

            Assert.Equal(ValidateOptionsResult.Success, Development.Validate(null, With(entry)));
        }

        [Fact]
        public void Two_entries_that_register_the_same_client_fail()
        {
            // A distribution registers <slug>-svc too, so a CLI entry of that name would be
            // overwritten by the distribution at every start, and the distribution by it.
            TellmaIdentitySeedClientOptions cli = new()
            {
                ClientId = "acme-svc",
                Kind = TellmaIdentitySeedClientKind.Cli,
            };

            ValidateOptionsResult result = Production.Validate(null, With(Entry(), cli));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("'acme-svc'", StringComparison.Ordinal)
                && f.Contains("TellmaIdentity:Seed:Clients:0", StringComparison.Ordinal));
        }

        [Fact]
        public void Two_entries_whose_client_ids_differ_only_in_case_fail_naming_both()
        {
            // The store's unique index on the client id is case-insensitive, so these would collide
            // there with a raw constraint violation instead of this message.
            TellmaIdentitySeedClientOptions cli = new()
            {
                ClientId = "ACME-svc",
                Kind = TellmaIdentitySeedClientKind.Cli,
            };

            ValidateOptionsResult result = Production.Validate(null, With(cli, Entry()));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("TellmaIdentity:Seed:Clients:1 ('acme')", StringComparison.Ordinal)
                && f.Contains("TellmaIdentity:Seed:Clients:0 ('ACME-svc')", StringComparison.Ordinal));
        }

        [Fact]
        public void A_third_party_native_entry_is_accepted()
        {
            TellmaIdentitySeedClientOptions agent = new()
            {
                ClientId = "claude-code",
                Kind = TellmaIdentitySeedClientKind.Native,
                RequireConsent = true,
                ThirdParty = true,
                RedirectUris = { "http://127.0.0.1/callback" },
            };

            Assert.Equal(ValidateOptionsResult.Success, Production.Validate(null, With(agent)));
        }

        [Theory]
        [InlineData(TellmaIdentitySeedClientKind.Cli)]
        [InlineData(TellmaIdentitySeedClientKind.ControlPlane)]
        [InlineData(TellmaIdentitySeedClientKind.Distribution)]
        public void ThirdParty_on_any_kind_but_native_fails_naming_the_entry(TellmaIdentitySeedClientKind kind)
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.Kind = kind;
            entry.ThirdParty = true;

            ValidateOptionsResult result = Production.Validate(null, With(entry));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("TellmaIdentity:Seed:Clients:0 ('acme')", StringComparison.Ordinal)
                && f.Contains("ThirdParty", StringComparison.Ordinal));
        }

        [Fact]
        public void An_entry_without_a_client_id_fails()
        {
            TellmaIdentitySeedClientOptions entry = Entry();
            entry.ClientId = null;

            Assert.True(Production.Validate(null, With(entry)).Failed);
        }

        /// <summary>A production-shaped configuration carrying the given seed entries.</summary>
        private static TellmaIdentityOptions With(params TellmaIdentitySeedClientOptions[] entries)
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
            foreach (TellmaIdentitySeedClientOptions entry in entries)
            {
                options.Seed.Clients.Add(entry);
            }

            return options;
        }

        /// <summary>A complete distribution entry the negative tests then break.</summary>
        private static TellmaIdentitySeedClientOptions Entry()
        {
            return new TellmaIdentitySeedClientOptions
            {
                ClientId = "acme",
                Kind = TellmaIdentitySeedClientKind.Distribution,
                Origin = "https://acme.example.com",
                ClientSecret = "bff-secret-0123456789abcdef",
                ServiceClientSecret = "svc-secret-0123456789abcdef",
            };
        }

        /// <summary>A host environment exposing only its name.</summary>
        /// <param name="environmentName">The environment name.</param>
        private sealed class FakeEnvironment(string environmentName) : IHostEnvironment
        {
            /// <inheritdoc />
            public string EnvironmentName { get; set; } = environmentName;

            /// <inheritdoc />
            public string ApplicationName { get; set; } = "Tellma.Identity.Tests";

            /// <inheritdoc />
            public string ContentRootPath { get; set; } = string.Empty;

            /// <inheritdoc />
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
