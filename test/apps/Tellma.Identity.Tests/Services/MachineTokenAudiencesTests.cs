// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Tellma.Identity.Services;
using Tellma.Identity.Tests.Provisioning;

namespace Tellma.Identity.Tests.Services
{
    /// <summary>
    ///     A machine token's audiences: an explicit resource is the whole audience, with no
    ///     platform audience added; without one, the requested scopes' platform audiences; and the
    ///     distribution API scope never without a resource.
    /// </summary>
    public sealed class MachineTokenAudiencesTests : IAsyncLifetime
    {
        private const string IdentityAudience = TellmaIdentityConstants.IdentityAudience;
        private const string Origin = "https://acme.example.com";

        private readonly ServiceProvider _services = BuildServices();

        [Fact]
        public async Task An_explicit_resource_is_the_whole_audience()
        {
            MachineAudienceResult result = await ResolveAsync(
                [TellmaIdentityConstants.ControlPlaneScope, TellmaIdentityConstants.IdentityScope], [Origin]);

            Assert.Null(result.Error);
            Assert.Equal([Origin], result.Audiences);
        }

        [Fact]
        public async Task An_explicit_resource_with_the_distribution_api_scope_is_the_whole_audience()
        {
            MachineAudienceResult result = await ResolveAsync(
                [TellmaIdentityConstants.ApiScope, TellmaIdentityConstants.IdentityScope], [Origin, Origin + "/17/mcp"]);

            Assert.Null(result.Error);
            Assert.Equal([Origin, Origin + "/17/mcp"], result.Audiences);
        }

        [Fact]
        public async Task Without_a_resource_the_requested_scopes_platform_audiences_are_the_audience()
        {
            MachineAudienceResult controlPlane = await ResolveAsync(
                [TellmaIdentityConstants.ControlPlaneScope, TellmaIdentityConstants.IdentityScope], []);
            Assert.Null(controlPlane.Error);
            Assert.Equal(
                [TellmaIdentityConstants.ControlPlaneAudience, IdentityAudience],
                controlPlane.Audiences.Order(StringComparer.Ordinal));

            MachineAudienceResult identity = await ResolveAsync([TellmaIdentityConstants.IdentityScope], []);
            Assert.Equal([IdentityAudience], identity.Audiences);
        }

        [Fact]
        public async Task The_distribution_api_scope_requires_an_explicit_resource()
        {
            // Its scope's resources are every distribution's origin, which no machine token may
            // carry at once.
            MachineAudienceResult result = await ResolveAsync(
                [TellmaIdentityConstants.ApiScope, TellmaIdentityConstants.IdentityScope], []);

            Assert.Equal(MachineTokenAudiences.ResourceRequiredDescription, result.Error);
            Assert.Empty(result.Audiences);
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            // The scopes as the seeder registers them, the distribution API scope holding two
            // distributions' origins.
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            IOpenIddictScopeManager scopeManager = _services.GetRequiredService<IOpenIddictScopeManager>();
            foreach ((string name, string[] resources) in (ValueTuple<string, string[]>[])
                [
                    (TellmaIdentityConstants.ApiScope, [Origin, "https://globex.example.com"]),
                    (TellmaIdentityConstants.IdentityScope, [IdentityAudience]),
                    (TellmaIdentityConstants.ControlPlaneScope, [TellmaIdentityConstants.ControlPlaneAudience]),
                ])
            {
                OpenIddictScopeDescriptor descriptor = new() { Name = name };
                descriptor.Resources.UnionWith(resources);
                await scopeManager.CreateAsync(descriptor, cancellationToken);
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
        }

        /// <summary>Builds the in-memory scope store the resolver reads.</summary>
        private static ServiceProvider BuildServices()
        {
            ServiceCollection services = new();
            services.AddInMemoryOpenIddict();
            return services.BuildServiceProvider();
        }

        /// <summary>Resolves a machine token's audiences.</summary>
        private Task<MachineAudienceResult> ResolveAsync(string[] scopes, string[] resources)
        {
            return MachineTokenAudiences.ResolveAsync(
                scopes,
                resources,
                _services.GetRequiredService<IOpenIddictScopeManager>(),
                TestContext.Current.CancellationToken);
        }
    }
}
