// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
    ///     every action names its endpoint from a closed set, and every call — answered or refused —
    ///     is counted with its outcome and logged with its caller.
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
                if (version is null)
                {
                    // A controller outside the management API never maps a route under its prefix,
                    // from its class route or an action's, alone or combined: an unversioned
                    // management route would bypass the version rule and go uncounted.
                    foreach (string route in RoutesOf(controller))
                    {
                        Assert.False(
                            route.TrimStart('/').StartsWith("api/identity", StringComparison.OrdinalIgnoreCase),
                            $"{controller.Name} maps '{route}' outside the versioned management API.");
                    }

                    continue;
                }

                // The version attribute is the controller's only route provider, so its prefix
                // always applies.
                Assert.Single(controller.GetCustomAttributes().OfType<IRouteTemplateProvider>());

                foreach (MethodInfo action in Actions(controller))
                {
                    // Relative to the version prefix, never rooted, so the prefix always applies.
                    foreach (string? template in action.GetCustomAttributes().OfType<IRouteTemplateProvider>().Select(static a => a.Template))
                    {
                        Assert.False(
                            template is null || template.StartsWith('/') || template.StartsWith('~')
                                || template.StartsWith("api/", StringComparison.OrdinalIgnoreCase),
                            $"{controller.Name}.{action.Name} maps a route outside its version prefix.");
                    }

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
        public void An_authorized_call_is_counted_ok_with_its_version_and_endpoint_and_logged_with_its_client()
        {
            using Telemetry telemetry = new();
            ManagementApiCallFilter filter = new("v1", telemetry.Recorder);
            ActionContext action = Action(ManagementApiEndpoints.Invite, Principal(("client_id", "acme-svc")));

            // Nothing is recorded when the call arrives, only once it has been answered.
            filter.OnResourceExecuting(Executing(action));
            Assert.Empty(telemetry.Calls.GetMeasurementSnapshot());
            filter.OnResourceExecuted(Executed(action));

            CollectedMeasurement<long> measurement = Assert.Single(telemetry.Calls.GetMeasurementSnapshot());
            Assert.Equal(1, measurement.Value);
            Assert.Equal("v1", measurement.Tags[IdentityMetrics.ApiVersionTag]);
            Assert.Equal(ManagementApiEndpoints.Invite, measurement.Tags[IdentityMetrics.ApiEndpointTag]);
            Assert.Equal(ManagementApiOutcomes.Ok, measurement.Tags[IdentityMetrics.ApiOutcomeTag]);

            // The client is in the log, never in the metric's tags.
            Assert.DoesNotContain("acme-svc", measurement.Tags.Values.Select(static value => value as string));
            FakeLogRecord record = Assert.Single(telemetry.Logger.Collector.GetSnapshot());
            Assert.Equal(LogLevel.Information, record.Level);
            Assert.Contains("acme-svc", record.Message, StringComparison.Ordinal);
            Assert.Contains("v1", record.Message, StringComparison.Ordinal);
            Assert.Contains(ManagementApiEndpoints.Invite, record.Message, StringComparison.Ordinal);
            Assert.Contains(ManagementApiOutcomes.Ok, record.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_client_error_answer_is_still_ok()
        {
            using Telemetry telemetry = new();
            ActionContext action = Action(ManagementApiEndpoints.GetServiceAccount, Principal(("client_id", "acme-svc")));
            action.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;

            new ManagementApiCallFilter("v1", telemetry.Recorder).OnResourceExecuted(Executed(action));

            Assert.Equal(ManagementApiOutcomes.Ok, Assert.Single(telemetry.Calls.GetMeasurementSnapshot()).Tags[IdentityMetrics.ApiOutcomeTag]);
        }

        [Fact]
        public void An_unhandled_exception_or_a_server_error_is_counted_as_an_error()
        {
            using Telemetry telemetry = new();
            ManagementApiCallFilter filter = new("v1", telemetry.Recorder);

            ActionContext thrown = Action(ManagementApiEndpoints.Invite, Principal(("client_id", "acme-svc")));
            filter.OnResourceExecuted(Executed(thrown, new InvalidOperationException("boom")));

            ActionContext failed = Action(ManagementApiEndpoints.Invite, Principal(("client_id", "acme-svc")));
            failed.HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            filter.OnResourceExecuted(Executed(failed));

            Assert.All(
                telemetry.Calls.GetMeasurementSnapshot(),
                static measurement => Assert.Equal(ManagementApiOutcomes.Error, measurement.Tags[IdentityMetrics.ApiOutcomeTag]));
            Assert.Equal(2, telemetry.Calls.GetMeasurementSnapshot().Count);
        }

        [Fact]
        public void An_action_without_an_endpoint_name_is_refused_rather_than_counted()
        {
            using Telemetry telemetry = new();
            ManagementApiCallFilter filter = new("v1", telemetry.Recorder);
            ActionContext action = Action(endpoint: null, Principal(("client_id", "acme-svc")));

            Assert.Throws<InvalidOperationException>(() => filter.OnResourceExecuting(Executing(action)));
            Assert.Empty(telemetry.Calls.GetMeasurementSnapshot());
        }

        [Fact]
        public async Task A_forbidden_call_is_counted_and_logged_with_its_client_and_still_answered()
        {
            using Telemetry telemetry = new();
            RecordingResultHandler inner = new();
            ManagementApiAuthorizationResultHandler handler = new(inner, telemetry.Recorder);
            HttpContext http = Request(ManagementApiMetadata(ManagementApiEndpoints.DeliveryStatus), Principal(("client_id", "beta-svc")));

            await handler.HandleAsync(static _ => Task.CompletedTask, http, Policy(), PolicyAuthorizationResult.Forbid());

            CollectedMeasurement<long> measurement = Assert.Single(telemetry.Calls.GetMeasurementSnapshot());
            Assert.Equal(ManagementApiOutcomes.Forbidden, measurement.Tags[IdentityMetrics.ApiOutcomeTag]);
            Assert.Equal(ManagementApiEndpoints.DeliveryStatus, measurement.Tags[IdentityMetrics.ApiEndpointTag]);
            Assert.Equal("v1", measurement.Tags[IdentityMetrics.ApiVersionTag]);
            Assert.Contains("beta-svc", Assert.Single(telemetry.Logger.Collector.GetSnapshot()).Message, StringComparison.Ordinal);
            Assert.Equal(1, inner.Calls);
        }

        [Fact]
        public async Task A_challenged_call_is_counted_unauthorized_and_logged_without_a_client()
        {
            using Telemetry telemetry = new();
            RecordingResultHandler inner = new();
            ManagementApiAuthorizationResultHandler handler = new(inner, telemetry.Recorder);
            HttpContext http = Request(ManagementApiMetadata(ManagementApiEndpoints.Invite), new ClaimsPrincipal(new ClaimsIdentity()));

            await handler.HandleAsync(static _ => Task.CompletedTask, http, Policy(), PolicyAuthorizationResult.Challenge());

            Assert.Equal(
                ManagementApiOutcomes.Unauthorized,
                Assert.Single(telemetry.Calls.GetMeasurementSnapshot()).Tags[IdentityMetrics.ApiOutcomeTag]);
            Assert.Contains(
                "client " + ManagementApiCallRecorder.NoClient,
                Assert.Single(telemetry.Logger.Collector.GetSnapshot()).Message,
                StringComparison.Ordinal);
            Assert.Equal(1, inner.Calls);
        }

        [Fact]
        public async Task Other_endpoints_and_authorized_calls_pass_through_unrecorded()
        {
            using Telemetry telemetry = new();
            RecordingResultHandler inner = new();
            ManagementApiAuthorizationResultHandler handler = new(inner, telemetry.Recorder);

            // A refused call to an endpoint outside the management API, a refused call with no
            // endpoint at all, and an authorized management call, which its filter records.
            await handler.HandleAsync(static _ => Task.CompletedTask, Request([new object()], new ClaimsPrincipal()), Policy(), PolicyAuthorizationResult.Forbid());
            await handler.HandleAsync(static _ => Task.CompletedTask, Request(null, new ClaimsPrincipal()), Policy(), PolicyAuthorizationResult.Challenge());
            await handler.HandleAsync(
                static _ => Task.CompletedTask,
                Request(ManagementApiMetadata(ManagementApiEndpoints.Invite), Principal(("client_id", "acme-svc"))),
                Policy(),
                PolicyAuthorizationResult.Success());

            Assert.Empty(telemetry.Calls.GetMeasurementSnapshot());
            Assert.Equal(3, inner.Calls);
        }

        [Fact]
        public void The_engine_decorates_the_registered_result_handler()
        {
            // A handler the host registered before the engine is kept and wrapped, with its lifetime.
            ServiceCollection services = new();
            services.AddMetrics().AddLogging();
            services.AddSingleton<IdentityMetrics>().AddSingleton<ManagementApiCallRecorder>();
            RecordingResultHandler hostHandler = new();
            services.AddSingleton<IAuthorizationMiddlewareResultHandler>(hostHandler);

            ManagementApiAuthorizationResultHandler.Decorate(services);

            ServiceDescriptor descriptor = Assert.Single(services, static d => d.ServiceType == typeof(IAuthorizationMiddlewareResultHandler));
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.IsType<ManagementApiAuthorizationResultHandler>(provider.GetRequiredService<IAuthorizationMiddlewareResultHandler>());
        }

        [Fact]
        public void A_host_that_replaces_the_result_handler_after_the_engine_does_not_start()
        {
            ServiceCollection services = new();
            services.AddMetrics().AddLogging();
            services.AddSingleton<IdentityMetrics>().AddSingleton<ManagementApiCallRecorder>();
            ManagementApiAuthorizationResultHandler.Decorate(services);
            services.AddSingleton<IAuthorizationMiddlewareResultHandler, RecordingResultHandler>();

            using ServiceProvider provider = services.BuildServiceProvider();
            IStartupFilter check = Assert.Single(provider.GetServices<IStartupFilter>());
            ApplicationBuilder app = new(provider);

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => check.Configure(static _ => { })(app));
            Assert.Contains("AddTellmaIdentity", failure.Message, StringComparison.Ordinal);
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
                .Where(static method => method.GetCustomAttributes().OfType<IRouteTemplateProvider>().Any());
        }

        /// <summary>
        ///     Every route template a controller declares: each class template, each action
        ///     template, and each class and action template combined.
        /// </summary>
        private static IEnumerable<string> RoutesOf(Type controller)
        {
            string[] classTemplates = [.. controller.GetCustomAttributes().OfType<IRouteTemplateProvider>()
                .Select(static provider => provider.Template).OfType<string>()];
            string[] actionTemplates = [.. Actions(controller)
                .SelectMany(static action => action.GetCustomAttributes().OfType<IRouteTemplateProvider>())
                .Select(static provider => provider.Template).OfType<string>()];

            return classTemplates
                .Concat(actionTemplates)
                .Concat(classTemplates.SelectMany(prefix => actionTemplates.Select(
                    template => Microsoft.AspNetCore.Mvc.ApplicationModels.AttributeRouteModel.CombineTemplates(prefix, template) ?? string.Empty)));
        }

        /// <summary>The endpoint metadata MVC builds for a management action: class and action attributes.</summary>
        private static object[] ManagementApiMetadata(string endpoint)
        {
            return [new ManagementApiAttribute(ManagementApiVersions.V1), new ManagementApiEndpointAttribute(endpoint)];
        }

        /// <summary>A request routed to an endpoint with the given metadata, or to none.</summary>
        private static DefaultHttpContext Request(object[]? metadata, ClaimsPrincipal user)
        {
            DefaultHttpContext http = new() { User = user };
            if (metadata is not null)
            {
                http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "test"));
            }

            return http;
        }

        /// <summary>A policy for the result handler to receive; its content does not matter here.</summary>
        private static AuthorizationPolicy Policy()
        {
            return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
        }

        /// <summary>An action context for an action with the given endpoint name.</summary>
        private static ActionContext Action(string? endpoint, ClaimsPrincipal user)
        {
            ActionDescriptor descriptor = new()
            {
                DisplayName = "TestAction",
                EndpointMetadata = endpoint is null ? [] : [new ManagementApiEndpointAttribute(endpoint)],
            };

            return new ActionContext(new DefaultHttpContext { User = user }, new RouteData(), descriptor);
        }

        /// <summary>The resource filter's context before the action runs.</summary>
        private static ResourceExecutingContext Executing(ActionContext action)
        {
            return new ResourceExecutingContext(action, [], Array.Empty<IValueProviderFactory>());
        }

        /// <summary>The resource filter's context after the action and its result ran.</summary>
        private static ResourceExecutedContext Executed(ActionContext action, Exception? exception = null)
        {
            return new ResourceExecutedContext(action, []) { Exception = exception };
        }

        /// <summary>An authenticated principal with the given claims.</summary>
        private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(
                claims.Select(static claim => new Claim(claim.Type, claim.Value)), "test"));
        }

        /// <summary>A recorder over a fresh meter and a fake logger, with a collector on the counter.</summary>
        private sealed class Telemetry : IDisposable
        {
            private readonly ServiceProvider _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();

            public Telemetry()
            {
                IMeterFactory meterFactory = _provider.GetRequiredService<IMeterFactory>();
                Calls = new(meterFactory, IdentityMetrics.MeterName, IdentityMetrics.ApiCallsInstrumentName);
                Recorder = new(new IdentityMetrics(meterFactory), Logger);
            }

            /// <summary>The counter's measurements.</summary>
            public MetricCollector<long> Calls { get; }

            /// <summary>The call log.</summary>
            public FakeLogger<ManagementApiCallRecorder> Logger { get; } = new();

            /// <summary>The recorder under test.</summary>
            public ManagementApiCallRecorder Recorder { get; }

            /// <inheritdoc />
            public void Dispose()
            {
                Calls.Dispose();
                _provider.Dispose();
            }
        }

        /// <summary>A result handler that counts the results handed to it.</summary>
        private sealed class RecordingResultHandler : IAuthorizationMiddlewareResultHandler
        {
            /// <summary>How many results it received.</summary>
            public int Calls { get; private set; }

            /// <inheritdoc />
            public Task HandleAsync(
                RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
            {
                Calls++;
                return Task.CompletedTask;
            }
        }
    }
}
