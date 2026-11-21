// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Services;

namespace Tellma.Identity.Tests.Services
{
    /// <summary>
    ///     The resource grant rule: a requested resource is granted when it equals a granted one
    ///     exactly, or when it is exactly one tenant's resource under a granted origin. Every other
    ///     shape is refused.
    /// </summary>
    public sealed class ResourceGrantEvaluatorTests
    {
        private const string Origin = "https://acme.example.com";

        private static readonly ResourceGrantEvaluator Evaluator = new();

        private static readonly HashSet<string> OriginGrant = new(StringComparer.Ordinal) { Origin };

        [Theory]
        [InlineData(Origin)]
        [InlineData(Origin + "/17/mcp")]
        [InlineData(Origin + "/1/mcp")]
        [InlineData(Origin + "/2147483647/mcp")]
        public void The_origin_and_one_tenants_resource_under_it_are_granted(string requested)
        {
            Assert.True(Evaluator.IsGranted(OriginGrant, requested));
        }

        [Theory]
        [InlineData(Origin + "/017/mcp")]
        [InlineData(Origin + "/17/mcp/")]
        [InlineData(Origin + "/17/other")]
        [InlineData(Origin + "/17/mcp?x")]
        [InlineData(Origin + "/17/mcp#x")]
        [InlineData("https://other.example.com/17/mcp")]
        [InlineData(Origin + "/0/mcp")]
        [InlineData(Origin + "/00/mcp")]
        [InlineData(Origin + "/-17/mcp")]
        [InlineData(Origin + "/+17/mcp")]
        [InlineData(Origin + "/17.0/mcp")]
        [InlineData(Origin + "/1e3/mcp")]
        [InlineData(Origin + "/ 17/mcp")]
        [InlineData(Origin + "/17 /mcp")]
        [InlineData(Origin + "/２/mcp")]
        [InlineData(Origin + "/2147483648/mcp")]
        [InlineData(Origin + "/99999999999/mcp")]
        [InlineData(Origin + "//mcp")]
        [InlineData(Origin + "/mcp")]
        [InlineData(Origin + "/17/MCP")]
        [InlineData(Origin + "/17/mcp/mcp")]
        [InlineData(Origin + "/a/17/mcp")]
        [InlineData(Origin + "/17/18/mcp")]
        [InlineData(Origin + "/")]
        [InlineData(Origin + "/17")]
        [InlineData("HTTPS://ACME.EXAMPLE.COM/17/mcp")]
        [InlineData("https://ACME.example.com")]
        [InlineData("https://acme.example.com:443/17/mcp")]
        [InlineData("http://acme.example.com/17/mcp")]
        [InlineData("https://user@acme.example.com/17/mcp")]
        [InlineData("https://acme.example.com.evil.example/17/mcp")]
        [InlineData("https://evil.example/https://acme.example.com/17/mcp")]
        [InlineData("")]
        [InlineData("*")]
        [InlineData(Origin + "/*/mcp")]
        public void Every_other_shape_is_refused(string requested)
        {
            Assert.False(Evaluator.IsGranted(OriginGrant, requested));
        }

        [Fact]
        public void A_granted_resource_with_a_path_never_anchors_the_pattern()
        {
            HashSet<string> granted = new(StringComparer.Ordinal) { Origin + "/17/mcp", Origin + "/", Origin + "/api" };

            // Each is still granted exactly...
            Assert.True(Evaluator.IsGranted(granted, Origin + "/17/mcp"));
            Assert.True(Evaluator.IsGranted(granted, Origin + "/api"));

            // ...but nothing is derived under it, nor under the origin it sits on.
            Assert.False(Evaluator.IsGranted(granted, Origin + "/17/mcp/18/mcp"));
            Assert.False(Evaluator.IsGranted(granted, Origin + "//18/mcp"));
            Assert.False(Evaluator.IsGranted(granted, Origin + "/api/18/mcp"));
            Assert.False(Evaluator.IsGranted(granted, Origin + "/18/mcp"));
            Assert.False(Evaluator.IsGranted(granted, Origin));
        }

        [Theory]
        [InlineData("urn:tellma:control-plane")]
        [InlineData("https://acme.example.com:443")]
        [InlineData("https://ACME.example.com")]
        [InlineData("ftp://acme.example.com")]
        [InlineData("https://user@acme.example.com")]
        public void A_granted_value_that_is_not_a_canonical_origin_anchors_nothing_but_its_own_exact_match(string grantedValue)
        {
            HashSet<string> granted = new(StringComparer.Ordinal) { grantedValue };

            Assert.True(Evaluator.IsGranted(granted, grantedValue));
            Assert.False(Evaluator.IsGranted(granted, grantedValue + "/17/mcp"));
        }

        [Fact]
        public void An_origin_with_a_non_default_port_anchors_its_own_tenant_resources()
        {
            HashSet<string> granted = new(StringComparer.Ordinal) { "https://acme.example.com:8443" };

            Assert.True(Evaluator.IsGranted(granted, "https://acme.example.com:8443/17/mcp"));
            Assert.False(Evaluator.IsGranted(granted, Origin + "/17/mcp"));
        }

        [Fact]
        public void Matching_is_ordinal_whatever_comparer_the_granted_set_carries()
        {
            HashSet<string> caseInsensitive = new(StringComparer.OrdinalIgnoreCase) { Origin };

            Assert.False(Evaluator.IsGranted(caseInsensitive, "HTTPS://ACME.EXAMPLE.COM"));
            Assert.False(Evaluator.IsGrantedExactly(caseInsensitive, "https://ACME.example.com"));
            Assert.False(Evaluator.IsGranted(caseInsensitive, "https://ACME.example.com/17/mcp"));
            Assert.True(Evaluator.IsGranted(caseInsensitive, Origin + "/17/mcp"));
        }

        [Fact]
        public void An_exact_match_grants_no_tenant_resource()
        {
            Assert.True(Evaluator.IsGrantedExactly(OriginGrant, Origin));
            Assert.False(Evaluator.IsGrantedExactly(OriginGrant, Origin + "/17/mcp"));
            Assert.False(Evaluator.IsGrantedExactly(OriginGrant, string.Empty));
        }

        [Fact]
        public void Nothing_is_granted_from_an_empty_grant()
        {
            HashSet<string> none = new(StringComparer.Ordinal);

            Assert.False(Evaluator.IsGranted(none, Origin));
            Assert.False(Evaluator.IsGranted(none, Origin + "/17/mcp"));
        }

        [Theory]
        [InlineData(Origin + "/17/mcp", Origin, 17)]
        [InlineData("http://localhost:5000/1000/mcp", "http://localhost:5000", 1000)]
        [InlineData("http://[::1]:8080/5/mcp", "http://[::1]:8080", 5)]
        public void A_tenant_resource_splits_into_its_origin_and_tenant_id(string resource, string origin, int tenantId)
        {
            Assert.True(ResourceGrantEvaluator.TryParseTenantResource(resource, out string parsedOrigin, out int parsedTenantId));
            Assert.Equal(origin, parsedOrigin);
            Assert.Equal(tenantId, parsedTenantId);
        }

        [Theory]
        [InlineData(Origin, true)]
        [InlineData("http://localhost:5000", true)]
        [InlineData(Origin + "/", false)]
        [InlineData(Origin + "/path", false)]
        [InlineData(Origin + "?q", false)]
        [InlineData("https://acme.example.com:443", false)]
        [InlineData("https://ACME.example.com", false)]
        [InlineData("urn:tellma:control-plane", false)]
        [InlineData("acme.example.com", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Only_a_canonical_http_or_https_origin_is_an_origin(string? value, bool expected)
        {
            Assert.Equal(expected, ResourceGrantEvaluator.IsOrigin(value));
        }
    }
}
