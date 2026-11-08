// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Security.Claims;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     The management API's versioning: every management route lives under a version prefix,
    ///     every action names its endpoint from a closed set, and every call that reaches an action
    ///     is counted and logged with its caller.
    /// </summary>
    public sealed class ManagementApiTests
    {
        [Fact]
        public void A_version_maps_under_its_own_segment()
        {
            ManagementApiAttribute attribute = new(ManagementApiVersions.V1);

            Assert.Equal(1, attribute.Version);
            Assert.Equal("v1", attribute.Segment);
            Assert.Equal("api/identity/v1", attribute.Template);
            Assert.Equal("api/identity/v2", ManagementApiVersions.RoutePrefix(2));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void A_version_below_one_is_refused(int version)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ManagementApiAttribute(version));
        }

        [Fact]
        public void An_endpoint_name_outside_the_closed_set_is_refused()
        {
            Assert.Throws<ArgumentException>(() => new ManagementApiEndpointAttribute("service-accounts/{clientId}"));
        }

        [Fact]
        public void Every_management_action_is_versioned_and_names_its_endpoint_once()
        {
            Type[] controllers = [.. typeof(ManagementApiAttribute).Assembly.GetTypes()
                .Where(static type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)];

            List<(int Version, string Endpoint)> seen = [];
            foreach (Type controller in controllers)
            {
                ManagementApiAttribute? version = controller.GetCustomAttribute<ManagementApiAttribute>();
                foreach (MethodInfo action in Actions(controller))
                {
                    string?[] templates = [.. action.GetCustomAttributes<HttpMethodAttribute>().Select(static a => a.Template)];
                    if (version is null)
                    {
                        // A controller outside the management API never maps a route under its
                        // prefix: an unversioned management route would bypass the version rule.
                        Assert.DoesNotContain(
                            templates,
                            static template => template?.StartsWith("api/identity", StringComparison.OrdinalIgnoreCase) == true);
                        continue;
                    }

                    // Relative to the version prefix, never rooted, so the prefix always applies.
                    Assert.All(templates, template => Assert.False(
                        template is null || template.StartsWith('/') || template.StartsWith("api/", StringComparison.OrdinalIgnoreCase),
                        $"{controller.Name}.{action.Name} maps a route outside its version prefix."));

                    ManagementApiEndpointAttribute endpoint = Assert.Single(
                        action.GetCustomAttributes<ManagementApiEndpointAttribute>());
                    seen.Add((version.Version, endpoint.Name));
                }
            }

            // Each endpoint name is used once per version, and every name in the closed set is
            // in use, so the tag values are exactly the documented set.
            Assert.Equal(seen.Count, seen.Distinct().Count());
            Assert.Equal(
                ManagementApiEndpoints.All.Order(StringComparer.Ordinal),
                seen.Where(static s => s.Version == ManagementApiVersions.V1).Select(static s => s.Endpoint).Order(StringComparer.Ordinal));
        }

        [Fact]
        public void A_call_is_counted_with_its_version_and_endpoint_and_logged_with_its_client()
        {
            using ServiceProvider provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
            IMeterFactory meterFactory = provider.GetRequiredService<IMeterFactory>();
            using MetricCollector<long> calls =
                new(meterFactory, IdentityMetrics.MeterName, IdentityMetrics.ApiCallsInstrumentName);
            FakeLogger<ManagementApiCallFilter> logger = new();

            ManagementApiCallFilter filter = new("v1", new IdentityMetrics(meterFactory), logger);
            filter.OnResourceExecuting(Context(ManagementApiEndpoints.Invite, clientId: "acme-svc"));

            CollectedMeasurement<long> measurement = Assert.Single(calls.GetMeasurementSnapshot());
            Assert.Equal(1, measurement.Value);
            Assert.Equal("v1", measurement.Tags[IdentityMetrics.ApiVersionTag]);
            Assert.Equal(ManagementApiEndpoints.Invite, measurement.Tags[IdentityMetrics.ApiEndpointTag]);

            // The client is in the log, never in the metric's tags.
            Assert.DoesNotContain("acme-svc", measurement.Tags.Values.Select(static value => value as string));
            FakeLogRecord record = Assert.Single(logger.Collector.GetSnapshot());
            Assert.Equal(LogLevel.Information, record.Level);
            Assert.Contains("acme-svc", record.Message, StringComparison.Ordinal);
            Assert.Contains("v1", record.Message, StringComparison.Ordinal);
            Assert.Contains(ManagementApiEndpoints.Invite, record.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_action_without_an_endpoint_name_is_refused_rather_than_counted()
        {
            using ServiceProvider provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
            IMeterFactory meterFactory = provider.GetRequiredService<IMeterFactory>();
            using MetricCollector<long> calls =
                new(meterFactory, IdentityMetrics.MeterName, IdentityMetrics.ApiCallsInstrumentName);

            ManagementApiCallFilter filter = new("v1", new IdentityMetrics(meterFactory), new FakeLogger<ManagementApiCallFilter>());

            Assert.Throws<InvalidOperationException>(() => filter.OnResourceExecuting(Context(endpoint: null, clientId: "acme-svc")));
            Assert.Empty(calls.GetMeasurementSnapshot());
        }

        [Fact]
        public void The_caller_is_the_client_id_or_else_the_subject()
        {
            Assert.Equal("acme-svc", ManagementApiCaller.ClientIdOf(Principal(("client_id", "acme-svc"), ("sub", "acme-svc-sub"))));
            Assert.Equal("acme-svc", ManagementApiCaller.ClientIdOf(Principal(("sub", "acme-svc"))));
            Assert.Null(ManagementApiCaller.ClientIdOf(Principal()));
        }

        /// <summary>The public instance actions a controller declares.</summary>
        private static IEnumerable<MethodInfo> Actions(Type controller)
        {
            return controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(static method => method.GetCustomAttributes<HttpMethodAttribute>().Any());
        }

        /// <summary>A resource-filter context for an action with the given endpoint name.</summary>
        private static ResourceExecutingContext Context(string? endpoint, string clientId)
        {
            ActionDescriptor descriptor = new()
            {
                DisplayName = "TestAction",
                EndpointMetadata = endpoint is null ? [] : [new ManagementApiEndpointAttribute(endpoint)],
            };

            DefaultHttpContext http = new() { User = Principal(("client_id", clientId)) };
            return new ResourceExecutingContext(
                new ActionContext(http, new RouteData(), descriptor),
                [],
                Array.Empty<IValueProviderFactory>());
        }

        /// <summary>An authenticated principal with the given claims.</summary>
        private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(
                claims.Select(static claim => new Claim(claim.Type, claim.Value)), "test"));
        }
    }
}
