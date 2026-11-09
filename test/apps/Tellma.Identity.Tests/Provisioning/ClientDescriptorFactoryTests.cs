// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using OpenIddict.Abstractions;
using Tellma.Identity.Options;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.Tests.Provisioning
{
    /// <summary>
    ///     Least-privilege guarantees of the seeded-client descriptors — in particular that only
    ///     the clients that legitimately call distribution APIs are eligible to receive a new
    ///     distribution's audience at provisioning time.
    /// </summary>
    public sealed class ClientDescriptorFactoryTests
    {
        private static readonly TellmaIdentityOptions Options = BuildOptions();

        [Theory]
        [InlineData(TellmaIdentitySeedClientKind.Cli)]
        [InlineData(TellmaIdentitySeedClientKind.Native)]
        public void Cli_and_native_clients_may_receive_distribution_audiences(TellmaIdentitySeedClientKind kind)
        {
            OpenIddictApplicationDescriptor descriptor = ClientDescriptorFactory.SeededClient(
                Options, new TellmaIdentitySeedClientOptions { ClientId = "client", Kind = kind });

            Assert.True(TellmaClientProperties.IsSet(descriptor.Properties, TellmaClientProperties.CallsDistributionApis));
        }

        [Fact]
        public void The_control_plane_is_never_eligible_for_distribution_audiences()
        {
            OpenIddictApplicationDescriptor descriptor = ClientDescriptorFactory.SeededClient(
                Options,
                new TellmaIdentitySeedClientOptions
                {
                    ClientId = "control-plane",
                    Kind = TellmaIdentitySeedClientKind.ControlPlane,
                    ClientSecret = "a-strong-secret",
                });

            Assert.False(TellmaClientProperties.IsSet(descriptor.Properties, TellmaClientProperties.CallsDistributionApis));

            // Its only audiences are the control-plane surface and the management API — never a
            // distribution origin.
            Assert.DoesNotContain(
                descriptor.Permissions,
                permission => permission.StartsWith(OpenIddictConstants.Permissions.Prefixes.Resource, StringComparison.Ordinal)
                    && permission.Contains("app.tellma.com", StringComparison.Ordinal));
        }

        [Fact]
        public void A_third_party_native_client_gets_explicit_consent_and_no_first_party_marker()
        {
            OpenIddictApplicationDescriptor descriptor = ClientDescriptorFactory.SeededClient(
                Options,
                new TellmaIdentitySeedClientOptions
                {
                    ClientId = "claude-code",
                    Kind = TellmaIdentitySeedClientKind.Native,
                    RequireConsent = true,
                    ThirdParty = true,
                    RedirectUris = { "http://127.0.0.1/callback" },
                });

            Assert.Equal(OpenIddictConstants.ConsentTypes.Explicit, descriptor.ConsentType);
            Assert.False(descriptor.Properties.ContainsKey(TellmaClientProperties.FirstParty));

            // It is still a public native client that onboarding grants every distribution's origin.
            Assert.Equal(OpenIddictConstants.ClientTypes.Public, descriptor.ClientType);
            Assert.True(TellmaClientProperties.IsSet(descriptor.Properties, TellmaClientProperties.CallsDistributionApis));
        }

        [Fact]
        public void A_third_party_client_gets_explicit_consent_even_without_RequireConsent()
        {
            OpenIddictApplicationDescriptor descriptor = ClientDescriptorFactory.SeededClient(
                Options,
                new TellmaIdentitySeedClientOptions { ClientId = "agent", Kind = TellmaIdentitySeedClientKind.Native, ThirdParty = true });

            Assert.Equal(OpenIddictConstants.ConsentTypes.Explicit, descriptor.ConsentType);
            Assert.False(descriptor.Properties.ContainsKey(TellmaClientProperties.FirstParty));
        }

        [Theory]
        [InlineData(TellmaIdentitySeedClientKind.Cli, false)]
        [InlineData(TellmaIdentitySeedClientKind.Native, false)]
        [InlineData(TellmaIdentitySeedClientKind.Native, true)]
        public void A_first_party_seeded_client_keeps_the_marker_and_its_consent_setting(
            TellmaIdentitySeedClientKind kind, bool requireConsent)
        {
            OpenIddictApplicationDescriptor descriptor = ClientDescriptorFactory.SeededClient(
                Options, new TellmaIdentitySeedClientOptions { ClientId = "client", Kind = kind, RequireConsent = requireConsent });

            Assert.True(TellmaClientProperties.IsSet(descriptor.Properties, TellmaClientProperties.FirstParty));
            Assert.Equal(
                requireConsent ? OpenIddictConstants.ConsentTypes.Explicit : OpenIddictConstants.ConsentTypes.Implicit,
                descriptor.ConsentType);
        }

        [Theory]
        [InlineData(TellmaIdentitySeedClientKind.Cli)]
        [InlineData(TellmaIdentitySeedClientKind.ControlPlane)]
        public void Only_a_native_entry_may_be_third_party(TellmaIdentitySeedClientKind kind)
        {
            Assert.Throws<ArgumentException>(() => ClientDescriptorFactory.SeededClient(
                Options,
                new TellmaIdentitySeedClientOptions
                {
                    ClientId = "client",
                    Kind = kind,
                    ThirdParty = true,
                    ClientSecret = "a-strong-secret",
                }));
        }

        private static TellmaIdentityOptions BuildOptions()
        {
            TellmaIdentityOptions options = new()
            {
                Issuer = new Uri("https://identity.tellma.com"),
            };
            return options;
        }
    }
}
