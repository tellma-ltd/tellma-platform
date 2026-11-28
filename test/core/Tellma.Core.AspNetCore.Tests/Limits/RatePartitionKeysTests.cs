// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System.Net;
using System.Security.Claims;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Limits;

namespace Tellma.Core.AspNetCore.Tests.Limits
{
    /// <summary>
    ///     The partitions of the rate and concurrency policies: every spelling of one tenant or one
    ///     address is one key, a person and a service account never share a namespace, and the
    ///     report lane follows a strictly read priority only where the endpoint honors it.
    /// </summary>
    public sealed class RatePartitionKeysTests
    {
        [Theory]
        [InlineData("1", "1")]
        [InlineData("01", "1")]
        [InlineData("0001", "1")]
        [InlineData("42", "42")]
        public void Every_spelling_of_a_tenant_is_one_key(string segment, string key)
        {
            Assert.Equal(key, RatePartitionKeys.Tenant(Request(segment)));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("+1")]
        [InlineData(" 1")]
        [InlineData("1.0")]
        [InlineData("99999999999")]
        [InlineData("acme")]
        public void A_segment_that_names_no_tenant_shares_the_unknown_key(string segment)
        {
            Assert.Equal(RatePartitionKeys.Unknown, RatePartitionKeys.Tenant(Request(segment)));
        }

        [Fact]
        public void A_route_without_a_tenant_shares_the_unknown_key()
        {
            Assert.Equal(RatePartitionKeys.Unknown, RatePartitionKeys.Tenant(new DefaultHttpContext()));
        }

        [Fact]
        public void A_person_is_keyed_by_subject_and_a_service_account_by_client()
        {
            Assert.Equal("u:sub-1", RatePartitionKeys.Subject(Principal(("sub", "sub-1"), ("azp", "svc"))));
            Assert.Equal("c:svc", RatePartitionKeys.Subject(Principal(("azp", "svc"))));
            Assert.Equal("c:svc", RatePartitionKeys.Subject(Principal(("client_id", "svc"))));
            Assert.Equal("c:svc", RatePartitionKeys.Subject(Principal(("azp", "svc"), ("client_id", "other"))));
        }

        [Fact]
        public void A_subject_that_spells_a_client_key_cannot_take_the_clients_bucket()
        {
            Assert.NotEqual(
                RatePartitionKeys.Subject(Principal(("azp", "svc"))),
                RatePartitionKeys.Subject(Principal(("sub", "c:svc"))));
        }

        [Fact]
        public void An_anonymous_or_nameless_caller_shares_the_unknown_key()
        {
            Assert.Equal(RatePartitionKeys.Unknown, RatePartitionKeys.Subject(new ClaimsPrincipal(new ClaimsIdentity())));
            Assert.Equal(RatePartitionKeys.Unknown, RatePartitionKeys.Subject(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "sub-1")]))));
            Assert.Equal(RatePartitionKeys.Unknown, RatePartitionKeys.Subject(Principal(("sub", ""), ("azp", ""))));
        }

        [Fact]
        public void The_tenant_subject_key_joins_the_tenant_and_the_caller()
        {
            DefaultHttpContext context = Request("007");
            context.User = Principal(("sub", "sub-1"));

            Assert.Equal("7:u:sub-1", RatePartitionKeys.TenantSubject(context));
        }

        [Theory]
        [InlineData("203.0.113.7", "203.0.113.7")]
        [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
        [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
        [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
        [InlineData("fe80::1%3", "fe80::/64")]
        public void A_client_address_is_keyed_by_its_ipv4_form_or_its_ipv6_64(string address, string key)
        {
            DefaultHttpContext context = new();
            context.Connection.RemoteIpAddress = IPAddress.Parse(address);

            Assert.Equal(key, RatePartitionKeys.ClientAddress(context));
        }

        [Fact]
        public void A_request_without_an_address_shares_the_unknown_key()
        {
            Assert.Equal(RatePartitionKeys.Unknown, RatePartitionKeys.ClientAddress(new DefaultHttpContext()));
        }

        [Theory]
        [InlineData("low", true)]
        [InlineData(" low\t", true)]
        [InlineData("LOW", false)]
        [InlineData("Low", false)]
        [InlineData("lowest", false)]
        [InlineData("high", false)]
        [InlineData("", false)]
        public void Only_the_exact_value_low_asks_for_the_batch_lane(string value, bool low)
        {
            Assert.Equal(low, RatePartitionKeys.IsLowPriority(new StringValues(value)));
        }

        [Fact]
        public void A_repeated_or_missing_priority_is_the_interactive_lane()
        {
            Assert.False(RatePartitionKeys.IsLowPriority(new StringValues(["low", "low"])));
            Assert.False(RatePartitionKeys.IsLowPriority(StringValues.Empty));
        }

        [Fact]
        public void The_priority_chooses_the_lane_only_where_the_endpoint_honors_it()
        {
            DefaultHttpContext context = new();
            context.Request.Headers[TellmaHeaders.Priority] = "low";

            Assert.Equal(RateLane.Batch, RatePartitionKeys.Lane(context, honorsPriority: true));
            Assert.Equal(RateLane.Interactive, RatePartitionKeys.Lane(context, honorsPriority: false));
            Assert.Equal(RateLane.Interactive, RatePartitionKeys.Lane(new DefaultHttpContext(), honorsPriority: true));
        }

        [Fact]
        public void The_lane_tags_are_a_closed_set()
        {
            Assert.Equal("interactive", RatePartitionKeys.TagOf(RateLane.Interactive));
            Assert.Equal("batch", RatePartitionKeys.TagOf(RateLane.Batch));
            Assert.Null(RatePartitionKeys.TagOf(RateLane.None));
        }

        /// <summary>A request whose route names a tenant segment.</summary>
        /// <param name="segment">The segment.</param>
        /// <returns>The request.</returns>
        internal static DefaultHttpContext Request(string segment)
        {
            DefaultHttpContext context = new();
            context.Request.RouteValues["tenantId"] = segment;
            return context;
        }

        /// <summary>An authenticated principal with the given claims.</summary>
        /// <param name="claims">The claims, as type and value.</param>
        /// <returns>The principal.</returns>
        internal static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        {
            return new ClaimsPrincipal(new ClaimsIdentity([.. claims.Select(static claim => new Claim(claim.Type, claim.Value))], "test"));
        }
    }
}
