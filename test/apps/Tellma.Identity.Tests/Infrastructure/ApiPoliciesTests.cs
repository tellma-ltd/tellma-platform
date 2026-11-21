// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     The identity server's own APIs check their audience as well as their scope: the
    ///     management API needs the management audience, the operator API the control-plane audience,
    ///     so a token minted for a distribution is refused at both whatever scopes it carries. Both
    ///     are machine-to-machine surfaces, so a token that speaks for a person is refused too.
    /// </summary>
    public sealed class ApiPoliciesTests : IDisposable
    {
        private const string ManagementAudience = TellmaIdentityConstants.IdentityAudience;
        private const string DistributionOrigin = "https://acme.example.com";

        private readonly ServiceProvider _services = BuildServices();

        [Fact]
        public async Task The_management_policy_accepts_its_scope_with_the_management_audience()
        {
            Assert.True(await AuthorizeAsync(
                ApiPolicies.IdentityScope, Principal("tellma_identity", ManagementAudience)));
            Assert.True(await AuthorizeAsync(
                ApiPolicies.IdentityScope, Principal("tellma_control_plane tellma_identity", TellmaIdentityConstants.ControlPlaneAudience, ManagementAudience)));
        }

        [Theory]
        [InlineData(DistributionOrigin)]
        [InlineData(DistributionOrigin + "/17/mcp")]
        [InlineData(TellmaIdentityConstants.ControlPlaneAudience)]
        [InlineData("https://identity.tellma.com")]
        [InlineData("urn:tellma:identity:x")]
        [InlineData("URN:TELLMA:IDENTITY")]
        [InlineData("urn:tellma:identity ")]
        public async Task The_management_policy_refuses_a_principal_whose_audience_lacks_the_management_audience(string audience)
        {
            Assert.False(await AuthorizeAsync(ApiPolicies.IdentityScope, Principal("tellma_identity", audience)));
        }

        [Fact]
        public async Task The_management_policy_refuses_a_principal_with_no_audience_or_without_its_scope()
        {
            Assert.False(await AuthorizeAsync(ApiPolicies.IdentityScope, Principal("tellma_identity")));
            Assert.False(await AuthorizeAsync(ApiPolicies.IdentityScope, Principal("tellma_api", ManagementAudience)));
        }

        [Fact]
        public async Task The_operator_policy_accepts_its_scope_with_the_control_plane_audience()
        {
            Assert.True(await AuthorizeAsync(
                ApiPolicies.ControlPlaneScope,
                Principal("tellma_control_plane tellma_identity", TellmaIdentityConstants.ControlPlaneAudience, ManagementAudience)));
        }

        [Theory]
        [InlineData(DistributionOrigin)]
        [InlineData(ManagementAudience)]
        [InlineData("urn:tellma:control-plane:x")]
        [InlineData("URN:TELLMA:CONTROL-PLANE")]
        public async Task The_operator_policy_refuses_a_principal_whose_audience_lacks_the_control_plane_audience(string audience)
        {
            Assert.False(await AuthorizeAsync(
                ApiPolicies.ControlPlaneScope, Principal("tellma_control_plane tellma_identity", audience)));
        }

        [Fact]
        public async Task The_operator_policy_refuses_the_control_plane_audience_without_its_scope()
        {
            Assert.False(await AuthorizeAsync(
                ApiPolicies.ControlPlaneScope, Principal("tellma_identity", TellmaIdentityConstants.ControlPlaneAudience)));
        }

        [Fact]
        public async Task An_unauthenticated_principal_is_refused_by_both_policies()
        {
            ClaimsPrincipal anonymous = new(new ClaimsIdentity(
                [new Claim("scope", "tellma_control_plane tellma_identity"),
                 new Claim("aud", TellmaIdentityConstants.ControlPlaneAudience),
                 new Claim("aud", ManagementAudience)]));

            Assert.False(await AuthorizeAsync(ApiPolicies.IdentityScope, anonymous));
            Assert.False(await AuthorizeAsync(ApiPolicies.ControlPlaneScope, anonymous));
        }

        [Fact]
        public void HasAudience_reads_every_aud_claim_ordinally_and_never_splits_one()
        {
            ClaimsPrincipal principal = Principal("tellma_identity", DistributionOrigin, ManagementAudience);
            Assert.True(ApiPolicies.HasAudience(principal, ManagementAudience));
            Assert.True(ApiPolicies.HasAudience(principal, DistributionOrigin));
            Assert.False(ApiPolicies.HasAudience(principal, "https://ACME.example.com"));
            Assert.False(ApiPolicies.HasAudience(principal, string.Empty));

            // One claim holding two values separated by a space names neither of them.
            ClaimsPrincipal joined = Principal("tellma_identity", DistributionOrigin + " " + ManagementAudience);
            Assert.False(ApiPolicies.HasAudience(joined, ManagementAudience));
        }

        [Fact]
        public void Control_plane_authority_needs_both_the_scope_and_the_audience()
        {
            Assert.True(ApiPolicies.HasControlPlaneAuthority(
                Principal("tellma_control_plane", TellmaIdentityConstants.ControlPlaneAudience)));
            Assert.False(ApiPolicies.HasControlPlaneAuthority(Principal("tellma_control_plane", DistributionOrigin)));
            Assert.False(ApiPolicies.HasControlPlaneAuthority(Principal("tellma_control_plane tellma_identity", ManagementAudience)));
            Assert.False(ApiPolicies.HasControlPlaneAuthority(
                Principal("tellma_identity", TellmaIdentityConstants.ControlPlaneAudience)));
        }

        [Theory]
        [InlineData("individual")]
        [InlineData(null)]
        [InlineData("Service")]
        [InlineData("service individual")]
        public async Task Both_policies_refuse_a_principal_that_is_not_a_machine(string? kind)
        {
            // A person's token carrying the scope and the audience — what an interactive client
            // permitted the scope would obtain — is refused, and so is a token without the claim.
            ClaimsPrincipal management = WithKind(kind, "tellma_identity", ManagementAudience);
            ClaimsPrincipal controlPlane = WithKind(kind, "tellma_control_plane", TellmaIdentityConstants.ControlPlaneAudience);

            Assert.False(await AuthorizeAsync(ApiPolicies.IdentityScope, management));
            Assert.False(await AuthorizeAsync(ApiPolicies.ControlPlaneScope, controlPlane));
            Assert.False(ApiPolicies.HasControlPlaneAuthority(controlPlane));
        }

        [Fact]
        public async Task A_principal_with_two_kind_claims_is_refused_whatever_they_say()
        {
            ClaimsPrincipal principal = WithKind("service", "tellma_identity", ManagementAudience);
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("tellma_kind", "individual"));

            Assert.False(ApiPolicies.IsService(principal));
            Assert.False(await AuthorizeAsync(ApiPolicies.IdentityScope, principal));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _services.Dispose();
        }

        /// <summary>
        ///     An authenticated bearer machine principal with one scope claim and one claim per
        ///     audience: what a client-credentials token validates to.
        /// </summary>
        private static ClaimsPrincipal Principal(string scope, params string[] audiences)
        {
            return WithKind("service", scope, audiences);
        }

        /// <summary>An authenticated bearer principal of a given kind (none when null), with one scope claim and one claim per audience.</summary>
        private static ClaimsPrincipal WithKind(string? kind, string scope, params string[] audiences)
        {
            List<Claim> claims = [new Claim("scope", scope), new Claim("sub", "client")];
            if (kind is not null)
            {
                claims.Add(new Claim("tellma_kind", kind));
            }

            claims.AddRange(audiences.Select(static audience => new Claim("aud", audience)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
        }

        /// <summary>Registers authorization with the engine's policies.</summary>
        private static ServiceProvider BuildServices()
        {
            ServiceCollection services = new();
            services.AddLogging();
            services.AddAuthorization();
            services.Configure<AuthorizationOptions>(ApiPolicies.Configure);
            return services.BuildServiceProvider();
        }

        /// <summary>Evaluates one policy for a principal.</summary>
        private async Task<bool> AuthorizeAsync(string policy, ClaimsPrincipal principal)
        {
            AuthorizationResult result = await _services.GetRequiredService<IAuthorizationService>()
                .AuthorizeAsync(principal, resource: null, policy);
            return result.Succeeded;
        }
    }
}
