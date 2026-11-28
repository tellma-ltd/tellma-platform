// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Claims;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Core.Calendars;

namespace Tellma.Core.AspNetCore.Tests.Realtime
{
    /// <summary>
    ///     The hub claims: minted only from a context the guard bound, copied to Azure SignalR
    ///     Service's token without anything else of the principal, and read back only when each is
    ///     present once and well formed.
    /// </summary>
    public sealed class TellmaHubClaimsTests
    {
        /// <summary>A context the guard bound: a tenant, a user and a session handle.</summary>
        private static readonly RequestContext Bound = RequestContext.Tenantless(new GregorianCalendarSystem(), DateTimeOffset.UnixEpoch) with
        {
            Tenant = new TenantDescriptor(12, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
            Kind = PrincipalKind.User,
            Subject = "sub-9",
            UserId = 34,
            SessionHandle = "handle-1",
        };

        [Fact]
        public void The_identity_holds_the_tenant_the_user_and_the_session_alone()
        {
            ClaimsIdentity identity = TellmaHubClaims.Create(Bound);

            Assert.Equal(
                [(TellmaHubClaims.TenantId, "12"), (TellmaHubClaims.UserId, "34"), (TellmaHubClaims.SessionHandle, "handle-1")],
                identity.Claims.Select(static claim => (claim.Type, claim.Value)));
            Assert.Equal(TellmaHubClaims.AuthenticationType, identity.AuthenticationType);
        }

        [Theory]
        [InlineData("tenant")]
        [InlineData("user")]
        [InlineData("session")]
        public void A_context_without_a_tenant_a_user_or_a_session_mints_nothing(string missing)
        {
            RequestContext context = missing switch
            {
                "tenant" => Bound with { Tenant = null },
                "user" => Bound with { UserId = null },
                _ => Bound with { SessionHandle = null },
            };

            _ = Assert.Throws<InvalidOperationException>(() => TellmaHubClaims.Create(context));
        }

        [Fact]
        public void The_service_s_claims_are_the_subject_and_the_hub_claims_and_nothing_else()
        {
            ClaimsPrincipal user = new(new ClaimsIdentity(
                [new Claim("sub", "sub-9"), new Claim("email", "ann@example.com"), new Claim("sid", "sid-1"), new Claim("name", "Ann")],
                "Tellma.Session"));
            user.AddIdentity(TellmaHubClaims.Create(Bound));

            Assert.Equal(
                [("sub", "sub-9"), (TellmaHubClaims.TenantId, "12"), (TellmaHubClaims.UserId, "34"), (TellmaHubClaims.SessionHandle, "handle-1")],
                TellmaHubClaims.Select(user).Select(static claim => (claim.Type, claim.Value)));
        }

        [Fact]
        public void A_principal_with_every_claim_once_reads_back()
        {
            ClaimsPrincipal user = new(new ClaimsIdentity([new Claim("sub", "sub-9")], "Tellma.Session"));
            user.AddIdentity(TellmaHubClaims.Create(Bound));

            Assert.True(TellmaHubClaims.TryRead(user, out HubConnectionClaims? claims));
            Assert.Equal(new HubConnectionClaims(12, 34, "sub-9", "handle-1"), claims);
            Assert.True(TellmaHubClaims.Carries(user));
        }

        [Theory]
        [InlineData("no-subject")]
        [InlineData("no-hub-claims")]
        [InlineData("two-tenants")]
        [InlineData("tenant-not-a-number")]
        [InlineData("tenant-signed")]
        [InlineData("user-zero")]
        [InlineData("empty-handle")]
        public void A_principal_missing_a_claim_or_carrying_one_twice_or_malformed_reads_nothing(string defect)
        {
            List<Claim> claims =
            [
                new("sub", "sub-9"),
                new(TellmaHubClaims.TenantId, "12"),
                new(TellmaHubClaims.UserId, "34"),
                new(TellmaHubClaims.SessionHandle, "handle-1"),
            ];
            switch (defect)
            {
                case "no-subject":
                    _ = claims.RemoveAll(static claim => claim.Type == "sub");
                    break;
                case "no-hub-claims":
                    _ = claims.RemoveAll(static claim => claim.Type != "sub");
                    break;
                case "two-tenants":
                    claims.Add(new Claim(TellmaHubClaims.TenantId, "13"));
                    break;
                case "tenant-not-a-number":
                    claims[1] = new Claim(TellmaHubClaims.TenantId, "twelve");
                    break;
                case "tenant-signed":
                    claims[1] = new Claim(TellmaHubClaims.TenantId, "+12");
                    break;
                case "user-zero":
                    claims[2] = new Claim(TellmaHubClaims.UserId, "0");
                    break;
                default:
                    claims[3] = new Claim(TellmaHubClaims.SessionHandle, string.Empty);
                    break;
            }

            // The claims split across two identities, as the self-hosted principal holds them.
            ClaimsPrincipal user = new(new ClaimsIdentity(claims.Where(static claim => claim.Type == "sub"), "Tellma.Session"));
            user.AddIdentity(new ClaimsIdentity(claims.Where(static claim => claim.Type != "sub"), TellmaHubClaims.AuthenticationType));

            Assert.False(TellmaHubClaims.TryRead(user, out HubConnectionClaims? read));
            Assert.Null(read);
        }

        [Fact]
        public void A_principal_without_hub_claims_carries_none()
        {
            Assert.False(TellmaHubClaims.Carries(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "sub-9")], "Tellma.Session"))));
            Assert.False(TellmaHubClaims.TryRead(null, out _));
        }
    }
}
