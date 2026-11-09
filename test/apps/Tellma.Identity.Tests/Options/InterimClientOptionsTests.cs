// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.Tests.Options
{
    /// <summary>
    ///     The interim agent clients: a keyed section whose entries a configuration layer
    ///     overrides or turns off one by one, each a third-party native client whose client id is
    ///     its key, validated beside the client list and seeded as a third-party client.
    /// </summary>
    public sealed class InterimClientOptionsTests
    {
        private static readonly TellmaIdentityOptionsValidator Production = new(new FakeEnvironment(Environments.Production));

        /// <summary>The three entries the standalone host ships, as configuration.</summary>
        private static readonly Dictionary<string, string?> Shipped = new()
        {
            ["Seed:InterimClients:claude-code:Kind"] = "Native",
            ["Seed:InterimClients:claude-code:RequireConsent"] = "true",
            ["Seed:InterimClients:claude-code:ThirdParty"] = "true",
            ["Seed:InterimClients:claude-code:RedirectUris:0"] = "http://127.0.0.1/callback",
            ["Seed:InterimClients:codex:Kind"] = "Native",
            ["Seed:InterimClients:codex:RequireConsent"] = "true",
            ["Seed:InterimClients:codex:ThirdParty"] = "true",
            ["Seed:InterimClients:codex:RedirectUris:0"] = "http://127.0.0.1/callback",
            ["Seed:InterimClients:cursor:Kind"] = "Native",
            ["Seed:InterimClients:cursor:RequireConsent"] = "true",
            ["Seed:InterimClients:cursor:ThirdParty"] = "true",
            ["Seed:InterimClients:cursor:RedirectUris:0"] = "http://127.0.0.1/callback",
        };

        [Fact]
        public void A_layer_overrides_one_entry_and_turns_another_off_leaving_the_rest_intact()
        {
            TellmaIdentityOptions options = Bind(new Dictionary<string, string?>
            {
                ["Seed:InterimClients:claude-code:RedirectUris:0"] = "http://127.0.0.1/oauth/callback",
                ["Seed:InterimClients:cursor:Enabled"] = "false",
            });

            Assert.Equal(["claude-code", "codex", "cursor"], options.Seed.InterimClients.Keys.Order(StringComparer.Ordinal));

            TellmaIdentitySeedClientOptions claude = options.Seed.InterimClients["claude-code"];
            Assert.Equal(["http://127.0.0.1/oauth/callback"], claude.RedirectUris);
            Assert.True(claude.ThirdParty);
            Assert.True(claude.Enabled);

            TellmaIdentitySeedClientOptions codex = options.Seed.InterimClients["codex"];
            Assert.Equal(["http://127.0.0.1/callback"], codex.RedirectUris);
            Assert.True(codex.Enabled);

            Assert.False(options.Seed.InterimClients["cursor"].Enabled);
        }

        [Fact]
        public void Every_entry_takes_its_key_as_client_id()
        {
            TellmaIdentityOptions options = Bind([]);

            foreach ((string key, TellmaIdentitySeedClientOptions client) in options.Seed.InterimClients)
            {
                Assert.Equal(key, client.ClientId);
            }
        }

        [Fact]
        public void The_shipped_entries_are_valid()
        {
            Assert.Equal(ValidateOptionsResult.Success, Production.Validate(null, Valid(Bind([]))));
        }

        [Fact]
        public void An_entry_whose_client_id_differs_from_its_key_fails_naming_it()
        {
            TellmaIdentityOptions options = Bind(new Dictionary<string, string?>
            {
                ["Seed:InterimClients:codex:ClientId"] = "codex-cli",
            });

            ValidateOptionsResult result = Production.Validate(null, Valid(options));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("TellmaIdentity:Seed:InterimClients:codex", StringComparison.Ordinal)
                && f.Contains("must equal its key", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("Seed:InterimClients:codex:ThirdParty", "false")]
        [InlineData("Seed:InterimClients:codex:Kind", "Cli")]
        public void An_entry_that_is_not_a_third_party_native_client_fails(string key, string value)
        {
            TellmaIdentityOptions options = Bind(new Dictionary<string, string?> { [key] = value });

            ValidateOptionsResult result = Production.Validate(null, Valid(options));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("TellmaIdentity:Seed:InterimClients:codex", StringComparison.Ordinal)
                && f.Contains("Native entry with ThirdParty", StringComparison.Ordinal));
        }

        [Fact]
        public void A_disabled_entry_is_not_validated()
        {
            TellmaIdentityOptions options = Bind(new Dictionary<string, string?>
            {
                ["Seed:InterimClients:codex:Kind"] = "Cli",
                ["Seed:InterimClients:codex:Enabled"] = "false",
            });

            Assert.Equal(ValidateOptionsResult.Success, Production.Validate(null, Valid(options)));
        }

        [Fact]
        public void An_entry_that_registers_a_client_the_client_list_registers_fails()
        {
            TellmaIdentityOptions options = Bind(new Dictionary<string, string?>
            {
                ["Seed:Clients:0:ClientId"] = "Cursor",
                ["Seed:Clients:0:Kind"] = "Cli",
            });

            ValidateOptionsResult result = Production.Validate(null, Valid(options));

            Assert.True(result.Failed);
            Assert.Contains(result.Failures!, static f => f.Contains("TellmaIdentity:Seed:InterimClients:cursor", StringComparison.Ordinal)
                && f.Contains("TellmaIdentity:Seed:Clients:0", StringComparison.Ordinal));
        }

        [Fact]
        public void A_shipped_entry_seeds_a_third_party_client_with_a_loopback_redirect()
        {
            TellmaIdentityOptions options = Valid(Bind([]));

            OpenIddictApplicationDescriptor descriptor = ClientDescriptorFactory.SeededClient(
                options, options.Seed.InterimClients["claude-code"]);

            Assert.Equal("claude-code", descriptor.ClientId);
            Assert.Equal(OpenIddictConstants.ClientTypes.Public, descriptor.ClientType);
            Assert.Equal(OpenIddictConstants.ApplicationTypes.Native, descriptor.ApplicationType);
            Assert.Equal(OpenIddictConstants.ConsentTypes.Explicit, descriptor.ConsentType);
            Assert.False(descriptor.Properties.ContainsKey(TellmaClientProperties.FirstParty));
            Assert.True(TellmaClientProperties.IsSet(descriptor.Properties, TellmaClientProperties.CallsDistributionApis));
            Assert.Equal([new Uri("http://127.0.0.1/callback")], descriptor.RedirectUris);
        }

        [Fact]
        public void Seed_entries_are_listed_with_the_paths_that_name_them()
        {
            TellmaIdentityOptions options = Bind(new Dictionary<string, string?>
            {
                ["Seed:Clients:0:ClientId"] = "tellma-cli",
                ["Seed:Clients:0:Kind"] = "Cli",
            });

            IReadOnlyList<TellmaIdentitySeedEntry> entries = options.Seed.ListEntries();

            Assert.Equal("TellmaIdentity:Seed:Clients:0", entries[0].Path);
            Assert.Null(entries[0].InterimKey);
            Assert.Equal(
                ["TellmaIdentity:Seed:InterimClients:claude-code", "TellmaIdentity:Seed:InterimClients:codex", "TellmaIdentity:Seed:InterimClients:cursor"],
                entries.Skip(1).Select(static e => e.Path).Order(StringComparer.Ordinal));
        }

        /// <summary>Binds the shipped entries with a deployment layer on top, as the engine binds its section.</summary>
        private static TellmaIdentityOptions Bind(Dictionary<string, string?> layer)
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(Shipped)
                .AddInMemoryCollection(layer)
                .Build();

            TellmaIdentityOptions options = new();
            configuration.Bind(options);
            options.Seed.ApplyInterimClientIds();
            return options;
        }

        /// <summary>Completes the options with the production settings validation also checks.</summary>
        private static TellmaIdentityOptions Valid(TellmaIdentityOptions options)
        {
            options.Issuer = new Uri("https://identity.example.com");
            options.ConnectionString = "Server=.;Database=Id;Trusted_Connection=True";
            options.Keys.Signing.Source = TellmaIdentityCertificateSourceKind.PfxFile;
            options.Keys.Signing.PfxFiles.Add(new TellmaIdentityPfxFileOptions { Path = "/etc/tellma/signing.pfx" });
            options.Keys.Encryption.Source = TellmaIdentityCertificateSourceKind.PfxFile;
            options.Keys.Encryption.PfxFiles.Add(new TellmaIdentityPfxFileOptions { Path = "/etc/tellma/encryption.pfx" });
            return options;
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
