// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Problems;

namespace Tellma.Core.AspNetCore.Tests.Problems
{
    /// <summary>
    ///     The mapping from an exception, or a framework's bare status, to its problem: the status,
    ///     the code, the arguments a client reads, and the hint of when to retry.
    /// </summary>
    public sealed class ProblemMapperTests
    {
        [Theory]
        [InlineData(StatusCodes.Status400BadRequest, false, "bad-request")]
        [InlineData(StatusCodes.Status401Unauthorized, false, "unauthenticated")]
        [InlineData(StatusCodes.Status403Forbidden, false, "forbidden")]
        [InlineData(StatusCodes.Status404NotFound, true, "route-not-found")]
        [InlineData(StatusCodes.Status404NotFound, false, "not-found")]
        [InlineData(StatusCodes.Status405MethodNotAllowed, true, "method-not-allowed")]
        [InlineData(StatusCodes.Status413PayloadTooLarge, true, "limit-exceeded")]
        [InlineData(StatusCodes.Status415UnsupportedMediaType, true, "unsupported-media")]
        [InlineData(StatusCodes.Status429TooManyRequests, true, "rate-limited")]
        [InlineData(StatusCodes.Status500InternalServerError, true, "internal")]
        [InlineData(StatusCodes.Status504GatewayTimeout, true, "timeout")]
        public void A_bare_framework_status_maps_to_its_problem_code(int status, bool routeMiss, string code)
        {
            ProblemDescription problem = Assert.IsType<ProblemDescription>(ProblemMapper.ForStatus(status, routeMiss));

            Assert.Equal(status, problem.Status);
            Assert.Equal(code, problem.Code);
        }

        [Theory]
        [InlineData(StatusCodes.Status409Conflict)]
        [InlineData(StatusCodes.Status418ImATeapot)]
        [InlineData(StatusCodes.Status502BadGateway)]
        public void A_status_no_problem_code_names_is_left_alone(int status)
        {
            Assert.Null(ProblemMapper.ForStatus(status, routeMiss: true));
        }

        [Fact]
        public void A_body_over_its_ceiling_names_the_body_limit_and_renders_no_detail()
        {
            ProblemDescription bare = ProblemMapper.ForStatus(StatusCodes.Status413PayloadTooLarge, routeMiss: true)!;
            ProblemDescription thrown = Map(new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));

            Assert.All([bare, thrown], static problem =>
            {
                Assert.Equal(ProblemMapper.LimitExceededCode, problem.Code);
                Assert.Equal(LimitNames.Body, problem.Arguments!["limit"]);
                Assert.Same(ProblemDetail.None, problem.Detail);
            });
        }

        [Fact]
        public void A_body_that_is_not_JSON_is_a_bad_request_for_malformed_JSON()
        {
            BadHttpRequestException refused = new("Failed to read parameter \"Body body\" from the request body as JSON.", StatusCodes.Status400BadRequest, new JsonException("'x' is an invalid start of a value."));

            ProblemDescription problem = Map(refused);

            Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
            Assert.Equal(ProblemMapper.BadRequestCode, problem.Code);
            Assert.Equal(ProblemMapper.MalformedJsonReason, problem.Arguments!["reason"]);
            Assert.Null(problem.Detail);
        }

        [Fact]
        public void Another_framework_refusal_keeps_its_status_and_never_its_message()
        {
            ProblemDescription unsupported = Map(new BadHttpRequestException("Expected a supported JSON media type but got \"text/plain\".", StatusCodes.Status415UnsupportedMediaType));
            ProblemDescription missing = Map(new BadHttpRequestException("Required parameter \"Body body\" was not provided from body.", StatusCodes.Status400BadRequest));

            Assert.Equal((StatusCodes.Status415UnsupportedMediaType, ProblemMapper.UnsupportedMediaCode), (unsupported.Status, unsupported.Code));
            Assert.Equal((StatusCodes.Status400BadRequest, ProblemMapper.BadRequestCode), (missing.Status, missing.Code));
            Assert.All([unsupported, missing], static problem =>
            {
                Assert.Null(problem.Arguments);
                Assert.Null(problem.Detail);
            });
        }

        [Fact]
        public void A_busy_image_processor_is_the_imaging_dependency_with_its_wait()
        {
            ProblemDescription problem = Map(new ImageProcessingBusyException(TimeSpan.FromSeconds(4)));

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
            Assert.Equal(ProblemMapper.DependencyUnavailableCode, problem.Code);
            Assert.Equal(ProblemMapper.ImagingDependency, problem.Arguments!["dependency"]);
            Assert.Equal(TimeSpan.FromSeconds(4), problem.RetryAfter);
        }

        [Fact]
        public void An_unavailable_dependency_without_a_hint_is_retried_after_a_second()
        {
            ProblemDescription silent = Map(new DependencyUnavailableException("database", retryAfter: null, inner: null));
            ProblemDescription hinted = Map(new DependencyUnavailableException("email", TimeSpan.FromSeconds(20), inner: null));

            Assert.Equal(ProblemMapper.DefaultRetryAfter, silent.RetryAfter);
            Assert.Equal(TimeSpan.FromSeconds(20), hinted.RetryAfter);
            Assert.Equal("database", silent.Arguments!["dependency"]);
        }

        [Fact]
        public void A_stale_context_is_retried_after_a_second_and_names_its_dependencies()
        {
            ProblemDescription problem = Map(new StaleContextException([new VersionTagDependency("settings", VersionTag.New(), VersionTagMismatchPolicy.Rerun)]));

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);
            Assert.Equal(TimeSpan.FromSeconds(1), problem.RetryAfter);
            Assert.NotNull(problem.ErrorDetails);
        }

        [Theory]
        [InlineData(TenantProblemCodes.CatalogUnavailable, StatusCodes.Status503ServiceUnavailable)]
        [InlineData(TenantProblemCodes.SchemaBehind, StatusCodes.Status503ServiceUnavailable)]
        [InlineData(TenantProblemCodes.Suspended, StatusCodes.Status403Forbidden)]
        [InlineData(TenantProblemCodes.ReadOnly, StatusCodes.Status403Forbidden)]
        [InlineData(TenantProblemCodes.Frozen, StatusCodes.Status403Forbidden)]
        [InlineData(TenantProblemCodes.NotFound, StatusCodes.Status404NotFound)]
        public void An_unavailable_tenant_maps_by_its_code_and_names_nothing_of_the_tenant(string code, int status)
        {
            ProblemDescription problem = Map(new TenantUnavailableException(7, TenantState.Active, code, retryAfter: null));

            Assert.Equal(status, problem.Status);
            Assert.Equal(code, problem.Code);
            Assert.Null(problem.Arguments);
            Assert.Null(problem.Detail);
            Assert.Equal(status == StatusCodes.Status503ServiceUnavailable ? ProblemMapper.DefaultTenantRetryAfter : null, problem.RetryAfter);
        }

        [Theory]
        [InlineData(BlobRejectedException.TooLarge, StatusCodes.Status413PayloadTooLarge)]
        [InlineData(BlobRejectedException.UnsupportedType, StatusCodes.Status415UnsupportedMediaType)]
        [InlineData(BlobRejectedException.UnknownKind, StatusCodes.Status404NotFound)]
        [InlineData(BlobRejectedException.LengthRequired, StatusCodes.Status411LengthRequired)]
        [InlineData(BlobRejectedException.StagingQuotaExceeded, StatusCodes.Status422UnprocessableEntity)]
        public void A_refused_upload_maps_its_status_by_its_blob_code(string code, int status)
        {
            ProblemDescription problem = Map(new BlobRejectedException(code, new Dictionary<string, object?>()));

            Assert.Equal(status, problem.Status);
            Assert.Equal(ProblemMapper.BlobRejectedCode, problem.Code);
            ProblemError error = Assert.Single(problem.Errors!);
            Assert.Equal((code, ValidationPaths.BodyPath), (error.Code, error.Path));
        }

        [Fact]
        public void An_unmapped_exception_is_internal_and_names_itself_only_in_Development()
        {
            InvalidOperationException failure = new("SELECT * FROM [Secrets] failed for password=hunter2");

            ProblemDescription production = ProblemMapper.Map(failure, ProblemMappingContext.Default);
            ProblemDescription development = ProblemMapper.Map(failure, ProblemMappingContext.Default with { Development = true });

            Assert.All([production, development], static problem =>
            {
                Assert.Equal(StatusCodes.Status500InternalServerError, problem.Status);
                Assert.Equal(ProblemMapper.InternalCode, problem.Code);
                Assert.True(problem.IsUnhandled);
            });
            Assert.Null(production.Arguments);
            Assert.Equal($"{typeof(InvalidOperationException).FullName}: {failure.Message}", development.Arguments!["exception"]);
        }

        [Fact]
        public void The_server_timing_value_names_the_database_time_and_its_calls()
        {
            Assert.Equal("db;dur=12.3;desc=\"4 calls\"", ApiTelemetryFilter.ServerTiming(TimeSpan.FromMilliseconds(12.34), 4));
            Assert.Equal("db;dur=0;desc=\"0 calls\"", ApiTelemetryFilter.ServerTiming(TimeSpan.Zero, 0));
        }

        /// <summary>Maps an exception for a caller without rows.</summary>
        /// <param name="exception">The exception.</param>
        /// <returns>The problem.</returns>
        private static ProblemDescription Map(Exception exception)
        {
            return ProblemMapper.Map(exception, ProblemMappingContext.Default);
        }
    }
}
