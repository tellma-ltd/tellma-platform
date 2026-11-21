// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Claims;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Authentication;

namespace Tellma.Core.AspNetCore.Tests.Authentication
{
    /// <summary>
    ///     How a bearer token's claims name its principal: a user, a service account, or, for a token
    ///     the platform cannot place, nobody.
    /// </summary>
    public sealed class PrincipalBindingTests
    {
        [Fact]
        public void An_individual_token_binds_a_user_with_its_assurance()
        {
            PrincipalPart part = PrincipalBinding.FromBearer(Principal(
                ("sub", "sub-1"), ("azp", "cli"), ("sid", "sid-1"), ("tellma_kind", "individual"),
                ("acr", "urn:tellma:acr:mfa"), ("auth_time", "1700000000"), ("tellma_acr_auth_time", "1700000100")));

            Assert.Equal(PrincipalKind.User, part.Kind);
            Assert.Equal("sub-1", part.Subject);
            Assert.Equal("cli", part.ClientId);
            Assert.Equal("sid-1", part.SessionId);
            Assert.Null(part.SessionHandle);
            Assert.Equal("urn:tellma:acr:mfa", part.Assurance?.Acr);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), part.Assurance?.AuthTime);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000100), part.Assurance?.AcrAuthTime);
        }

        [Fact]
        public void A_service_token_binds_its_client_as_a_service_account()
        {
            PrincipalPart part = PrincipalBinding.FromBearer(Principal(("sub", "svc-1"), ("client_id", "svc-1"), ("tellma_kind", "service")));

            Assert.Equal(PrincipalKind.ServiceAccount, part.Kind);
            Assert.Equal("svc-1", part.Subject);
            Assert.Equal("svc-1", part.ClientId);
            Assert.Null(part.SessionId);
            Assert.Null(part.Assurance);
        }

        [Fact]
        public void A_token_without_a_kind_is_a_user_when_it_carries_a_sign_in_time()
        {
            PrincipalPart part = PrincipalBinding.FromBearer(Principal(("sub", "sub-1"), ("azp", "cli"), ("auth_time", "1700000000")));
            Assert.Equal(PrincipalKind.User, part.Kind);
        }

        [Fact]
        public void A_token_without_a_kind_or_a_sign_in_time_is_a_service_account()
        {
            PrincipalPart part = PrincipalBinding.FromBearer(Principal(("sub", "svc-1"), ("client_id", "svc-1")));
            Assert.Equal(PrincipalKind.ServiceAccount, part.Kind);
        }

        [Theory]
        [InlineData("robot")]
        [InlineData("Individual")]
        [InlineData("")]
        public void A_token_of_an_unknown_kind_binds_nobody(string kind)
        {
            PrincipalPart part = PrincipalBinding.FromBearer(Principal(("sub", "sub-1"), ("azp", "cli"), ("tellma_kind", kind), ("auth_time", "1700000000")));
            Assert.Same(PrincipalPart.Anonymous, part);
        }

        [Fact]
        public void A_user_token_without_a_subject_binds_nobody()
        {
            PrincipalPart part = PrincipalBinding.FromBearer(Principal(("azp", "cli"), ("tellma_kind", "individual")));
            Assert.Same(PrincipalPart.Anonymous, part);
        }

        [Fact]
        public void A_service_token_without_a_client_binds_nobody()
        {
            PrincipalPart part = PrincipalBinding.FromBearer(Principal(("sub", "svc-1"), ("tellma_kind", "service")));
            Assert.Same(PrincipalPart.Anonymous, part);
        }

        [Theory]
        [InlineData("-5")]
        [InlineData("1.5")]
        [InlineData("99999999999999999999")]
        [InlineData("soon")]
        public void A_malformed_sign_in_time_is_no_sign_in_time(string value)
        {
            PrincipalPart part = PrincipalBinding.FromBearer(Principal(("sub", "sub-1"), ("tellma_kind", "individual"), ("auth_time", value)));
            Assert.Null(part.Assurance?.AuthTime);
        }

        /// <summary>An authenticated principal with the claims given.</summary>
        /// <param name="claims">The claims, as type and value.</param>
        /// <returns>The principal.</returns>
        private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(
                claims.Select(static claim => new Claim(claim.Type, claim.Value)),
                TellmaAuthentication.BearerScheme,
                "sub",
                null));
        }
    }
}
