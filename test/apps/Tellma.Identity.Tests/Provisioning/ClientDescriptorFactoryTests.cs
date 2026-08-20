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
