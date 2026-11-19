// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Queryex;

namespace Tellma.Core.AspNetCore.Tests.Problems
{
    /// <summary>
    ///     Every exception a handler throws answers its problem body: its status, code, type, title
    ///     and trace, the members its row adds, the headers that tell a client when to retry or how
    ///     to step up, and nothing of the server's internals.
    /// </summary>
    public sealed class ProblemMappingTests
    {
        /// <summary>The members a problem body may carry; anything else would be a leak.</summary>
        private static readonly string[] BodyMembers = ["type", "title", "status", "detail", "instance", "code", "errors", "errorDetails", "arguments"];

        /// <summary>The exceptions the throwing endpoint raises, by name.</summary>
        private static readonly Dictionary<string, Func<Exception>> Exceptions = new(StringComparer.Ordinal)
        {
            ["bad-request"] = static () => new BadRequestException("The ids are not distinct."),
            ["query-invalid"] = static () => new InvalidQueryException([new QueryexDiagnostic("QX3001", new QueryexSpan(0, 4), "Select[0]", [new("property", "Nme")])]),
            ["step-up"] = static () => new StepUpRequiredException(ProbeEndpoints.StepUpAcr, TimeSpan.FromMinutes(15)),
            ["forbidden"] = static () => new ForbiddenException("forbidden", "gl.Center", "Save", ForbiddenReason.OutsideFilter),
            ["forbidden-path"] = static () => new ForbiddenException("forbidden", "gl.Center", "Read", ForbiddenReason.RestrictedPath, "Manager.Salary"),
            ["individual"] = static () => new IndividualRequiredException(),
            ["tenant-suspended"] = static () => new TenantUnavailableException(1, TenantState.Suspended, TenantProblemCodes.Suspended, retryAfter: null),
            ["tenant-schema-behind"] = static () => new TenantUnavailableException(1, TenantState.Active, TenantProblemCodes.SchemaBehind, retryAfter: null),
            ["tenant-read-only"] = static () => new TenantUnavailableException(1, TenantState.ReadOnly, TenantProblemCodes.ReadOnly, retryAfter: null),
            ["tenant-frozen"] = static () => new TenantUnavailableException(1, TenantState.Frozen, TenantProblemCodes.Frozen, retryAfter: null),
            ["catalog-unavailable"] = static () => new TenantUnavailableException(1, state: null, TenantProblemCodes.CatalogUnavailable, TimeSpan.FromSeconds(10)),
            ["tenant-unavailable-gone"] = static () => new TenantUnavailableException(1, TenantState.Retired, TenantProblemCodes.NotFound, retryAfter: null),
            ["tenant-not-found"] = static () => new TenantNotFoundException(1),
            ["tenant-state"] = static () => new TenantStateException(TenantProblemCodes.IllegalTransition, "The tenant cannot move from Active to Provisioning."),
            ["not-found"] = static () => new NotFoundException("gl.Center", ["3"]),
            ["concurrency"] = static () => new ConcurrencyException("concurrency-conflict", [new ConcurrencyConflict(3, "2026-11-19T08:30:00.0000000Z", 7, "Jane Doe", IsMissing: false)]),
            ["confirmation"] = static () => new ConfirmationRequiredException([new ConfirmationRequest("Gl.Backdated", [1, 2], new Dictionary<string, object?> { ["days"] = 3 })]),
            ["count-mismatch"] = static () => new CountMismatchException(2, 3),
            ["limit"] = static () => new LimitExceededException(LimitNames.MaxIds, 5, 3),
            ["validation"] = static () => new ValidationException([new ValidationError(ValidationPath.Root.Index(1).Property("Code"), "Validation.Required", new Dictionary<string, object?> { ["property"] = "Code" })]),
            ["import"] = static () => new ImportException([new ImportError("Centers", 4, "C", "Code", "Code", "Validation.Required", new Dictionary<string, object?>())], 7, [new RowRange(1, 3)]),
            ["blob-too-large"] = static () => new BlobRejectedException(BlobRejectedException.TooLarge, new Dictionary<string, object?> { [BlobRejectedException.MaxBytesArgument] = 1024 }),
            ["partial"] = static () => new PartialFailureException("partial-failure", new[] { "sent" }, ["email"]),
            ["stale"] = static () => new StaleContextException([new VersionTagDependency(VersionTagNames.Settings, VersionTag.New(), VersionTagMismatchPolicy.Rerun)]),
            ["dependency"] = static () => new DependencyUnavailableException("database", TimeSpan.FromSeconds(5), inner: null),
            ["imaging"] = static () => new ImageProcessingBusyException(TimeSpan.FromSeconds(3)),
            ["body-too-large"] = static () => new BadHttpRequestException("Request body too large. The max request body size is 64 bytes.", StatusCodes.Status413PayloadTooLarge),
            ["internal"] = static () => new InvalidOperationException("SELECT * FROM [Users] WHERE [Password] = 'hunter2' failed at Server=sql01;Password=s3cret"),
        };

        /// <summary>Each thrown exception's status and code.</summary>
        /// <returns>The exception's name, its status and its code.</returns>
        public static TheoryData<string, int, string> Table()
        {
            return new TheoryData<string, int, string>
            {
                { "bad-request", 400, "bad-request" },
                { "query-invalid", 400, "query-invalid" },
                { "step-up", 401, "step-up-required" },
                { "forbidden", 403, "forbidden" },
                { "forbidden-path", 403, "forbidden" },
                { "individual", 403, "individual-required" },
                { "tenant-suspended", 403, "tenant-suspended" },
                { "tenant-schema-behind", 503, "tenant-schema-behind" },
                { "tenant-read-only", 403, "tenant-read-only" },
                { "tenant-frozen", 403, "tenant-frozen" },
                { "catalog-unavailable", 503, "catalog-unavailable" },
                { "tenant-unavailable-gone", 404, "tenant-not-found" },
                { "tenant-not-found", 404, "tenant-not-found" },
                { "tenant-state", 409, "tenant-illegal-transition" },
                { "not-found", 404, "not-found" },
                { "concurrency", 409, "concurrency-conflict" },
                { "confirmation", 409, "confirmation-required" },
                { "count-mismatch", 409, "count-mismatch" },
                { "limit", 413, "limit-exceeded" },
                { "validation", 422, "validation" },
                { "import", 422, "validation" },
                { "blob-too-large", 413, "blob-rejected" },
                { "partial", 502, "partial-failure" },
                { "stale", 503, "stale-context" },
                { "dependency", 503, "dependency-unavailable" },
                { "imaging", 503, "dependency-unavailable" },
                { "body-too-large", 413, "limit-exceeded" },
                { "internal", 500, "internal" },
            };
        }

        [Theory]
        [MemberData(nameof(Table))]
        public async Task Each_exception_answers_its_problem_body(string name, int status, string code)
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpResponseMessage response = await ThrowAsync(host, name);

            // --- The status, the members every problem carries, the trace it names ---
            Assert.Equal(status, (int)response.StatusCode);
            Problem problem = await Problem.ReadAsync(response);
            JsonElement body = problem.Document;
            Assert.Equal(code, problem.Code);
            Assert.Equal("https://tellma.com/problems/" + code, problem.Type);
            Assert.Equal(status, body.GetProperty("status").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
            Assert.NotEqual(code, body.GetProperty("title").GetString());
            Assert.Equal(Assert.Single(response.Headers.GetValues(TellmaHeaders.TraceId)), problem.Instance);
            Assert.Equal(JsonValueKind.Array, body.GetProperty("errors").ValueKind);
            Assert.True(body.TryGetProperty("errorDetails", out _));
            Assert.All(body.EnumerateObject(), static member => Assert.Contains(member.Name, BodyMembers));
        }

        /// <summary>The members each row adds to the body, by the exception that raises it.</summary>
        /// <returns>The exception's name, the member's path in the body and its value as JSON, so its kind is pinned too.</returns>
        public static TheoryData<string, string, string> Members()
        {
            return new TheoryData<string, string, string>
            {
                { "count-mismatch", "arguments.expected", "2" },
                { "count-mismatch", "arguments.actual", "3" },
                { "limit", "arguments.limit", "\"MaxIds\"" },
                { "limit", "arguments.actual", "5" },
                { "limit", "arguments.maximum", "3" },
                { "not-found", "arguments.resource", "\"gl.Center\"" },
                { "not-found", "arguments.ids[0]", "\"3\"" },
                { "concurrency", "errorDetails.conflicts[0].id", "3" },
                { "concurrency", "errorDetails.conflicts[0].modifiedAt", "\"2026-11-19T08:30:00.0000000Z\"" },
                { "concurrency", "errorDetails.conflicts[0].modifiedById", "7" },
                { "concurrency", "errorDetails.conflicts[0].modifiedByName", "\"Jane Doe\"" },
                { "concurrency", "errorDetails.conflicts[0].isMissing", "false" },
                { "partial", "errorDetails.results[0]", "\"sent\"" },
                { "partial", "errorDetails.failed[0]", "\"email\"" },
                { "tenant-state", "detail", "\"The tenant cannot move from Active to Provisioning.\"" },
                { "confirmation", "errorDetails.confirmations[0].key", "\"Gl.Backdated\"" },
                { "confirmation", "errorDetails.confirmations[0].ids[1]", "2" },
                { "confirmation", "errorDetails.confirmations[0].arguments.days", "3" },
                { "stale", "errorDetails.dependencies[0]", "\"settings\"" },
                { "dependency", "arguments.dependency", "\"database\"" },
                { "blob-too-large", "errors[0].code", "\"Blob.TooLarge\"" },
                { "blob-too-large", "errors[0].arguments.maxBytes", "1024" },
                { "bad-request", "detail", "\"The ids are not distinct.\"" },
            };
        }

        [Theory]
        [MemberData(nameof(Members))]
        public async Task Each_row_writes_the_members_it_adds(string name, string path, string value)
        {
            await using TellmaTestHost host = await StartAsync();

            using JsonDocument body = await BodyAsync(host, name);

            Assert.Equal(value, At(body.RootElement, path).GetRawText());
        }

        [Fact]
        public async Task A_problem_without_extras_writes_null_error_details()
        {
            await using TellmaTestHost host = await StartAsync();

            using JsonDocument body = await BodyAsync(host, "not-found");

            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("errorDetails").ValueKind);
            Assert.Equal("gl.Center", body.RootElement.GetProperty("arguments").GetProperty("resource").GetString());
        }

        [Theory]
        [InlineData("stale", 1)]
        [InlineData("dependency", 5)]
        [InlineData("imaging", 3)]
        [InlineData("tenant-schema-behind", 30)]
        public async Task A_transient_problem_says_when_to_retry(string name, int seconds)
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpResponseMessage response = await ThrowAsync(host, name);

            Assert.Equal(TimeSpan.FromSeconds(seconds), response.Headers.RetryAfter?.Delta);
        }

        [Fact]
        public async Task A_busy_image_processor_names_the_imaging_dependency()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpResponseMessage response = await ThrowAsync(host, "imaging");

            Assert.Equal("imaging", (await Problem.ReadAsync(response)).Argument("dependency"));
        }

        [Fact]
        public async Task A_step_up_problem_carries_its_challenge()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpResponseMessage response = await ThrowAsync(host, "step-up");

            string challenge = Assert.Single(response.Headers.WwwAuthenticate).ToString();
            Assert.Contains("insufficient_user_authentication", challenge, StringComparison.Ordinal);
            Assert.Contains(ProbeEndpoints.StepUpAcr, challenge, StringComparison.Ordinal);
            Assert.Contains("max_age=900", challenge, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("forbidden", "outside-filter")]
        [InlineData("forbidden-path", "restricted-path")]
        public async Task A_refusal_names_the_securable_and_its_reason_and_nothing_else(string name, string reason)
        {
            await using TellmaTestHost host = await StartAsync();

            using JsonDocument body = await BodyAsync(host, name);

            // Only the securable, the reason and a restricted path: never a role, a filter or a user.
            JsonElement arguments = body.RootElement.GetProperty("arguments");
            Assert.All(arguments.EnumerateObject(), static member => Assert.Contains(member.Name, (string[])["resource", "action", "reason", "path"]));
            Assert.Equal(reason, arguments.GetProperty("reason").GetString());
            string detail = body.RootElement.GetProperty("detail").GetString()!;
            Assert.False(string.IsNullOrWhiteSpace(detail));
            Assert.DoesNotContain("{", detail, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_query_problem_keys_each_diagnostic_by_its_clause()
        {
            await using TellmaTestHost host = await StartAsync();

            using JsonDocument body = await BodyAsync(host, "query-invalid");

            JsonElement error = Assert.Single(body.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal("select", error.GetProperty("path").GetString());
            Assert.Equal("QX3001", error.GetProperty("code").GetString());
            Assert.Equal("Nme", error.GetProperty("arguments").GetProperty("property").GetString());
            JsonElement diagnostic = Assert.Single(body.RootElement.GetProperty("errorDetails").GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("Select[0]", diagnostic.GetProperty("location").GetString());
        }

        [Fact]
        public async Task A_validation_problem_renders_each_path_and_message()
        {
            await using TellmaTestHost host = await StartAsync();

            using JsonDocument body = await BodyAsync(host, "validation");

            JsonElement error = Assert.Single(body.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal("body[1].code", error.GetProperty("path").GetString());
            Assert.Equal("Validation.Required", error.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
            Assert.Equal("Code", error.GetProperty("arguments").GetProperty("property").GetString());
        }

        [Fact]
        public async Task An_import_problem_names_each_cell_and_the_committed_rows()
        {
            await using TellmaTestHost host = await StartAsync();

            using JsonDocument body = await BodyAsync(host, "import");

            JsonElement error = Assert.Single(body.RootElement.GetProperty("errors").EnumerateArray());
            Assert.Equal("Centers", error.GetProperty("sheet").GetString());
            Assert.Equal(4, error.GetProperty("row").GetInt32());
            Assert.Equal("C", error.GetProperty("column").GetString());
            Assert.Equal("Code", error.GetProperty("header").GetString());
            Assert.Equal("Code", error.GetProperty("property").GetString());
            Assert.False(error.TryGetProperty("path", out _));
            JsonElement details = body.RootElement.GetProperty("errorDetails");
            Assert.Equal(7, details.GetProperty("totalErrors").GetInt32());
            JsonElement range = Assert.Single(details.GetProperty("committedRowRanges").EnumerateArray());
            Assert.Equal((1, 3), (range.GetProperty("from").GetInt32(), range.GetProperty("to").GetInt32()));
        }

        [Fact]
        public async Task A_confirmation_without_its_own_message_falls_back_to_the_general_detail()
        {
            await using TellmaTestHost host = await StartAsync();

            using JsonDocument body = await BodyAsync(host, "confirmation");

            Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("detail").GetString()));
            JsonElement confirmation = Assert.Single(body.RootElement.GetProperty("errorDetails").GetProperty("confirmations").EnumerateArray());
            Assert.Equal("Gl.Backdated", confirmation.GetProperty("key").GetString());
        }

        [Fact]
        public async Task An_internal_problem_never_names_the_failure_in_Production()
        {
            await using TellmaTestHost host = await StartAsync();

            using HttpResponseMessage response = await ThrowAsync(host, "internal");
            string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain("SELECT", text, StringComparison.Ordinal);
            Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
            Assert.DoesNotContain("s3cret", text, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(InvalidOperationException), text, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
            Assert.False(JsonDocument.Parse(text).RootElement.TryGetProperty("arguments", out _));
        }

        [Fact]
        public async Task An_internal_problem_names_the_failure_type_and_message_in_Development_without_a_stack()
        {
            await using TellmaTestHost host = await StartAsync(static options => options.Environment = "Development");

            using HttpResponseMessage response = await ThrowAsync(host, "internal");
            string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            using var body = JsonDocument.Parse(text);
            string exception = body.RootElement.GetProperty("arguments").GetProperty("exception").GetString()!;
            Assert.StartsWith(typeof(InvalidOperationException).FullName + ": ", exception, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_unhandled_exception_is_logged_once_with_its_trace_and_counted_as_a_problem()
        {
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await StartAsync(options => options.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(logs));
            using MetricCollector<long> problems = new(host.Services.GetRequiredService<IMeterFactory>(), ApiTelemetryNames.MeterName, ApiTelemetryNames.Problems);

            using HttpResponseMessage response = await ThrowAsync(host, "internal");
            string trace = Assert.Single(response.Headers.GetValues(TellmaHeaders.TraceId));

            // --- One error naming the trace (the request log's own error line aside), and the problem line at Warning ---
            CapturedEvent unhandled = Assert.Single(logs.Events, static captured => captured.Level == LogLevel.Error && captured.Message.Contains("unhandled exception", StringComparison.Ordinal));
            Assert.Contains(trace, unhandled.Message, StringComparison.Ordinal);
            Assert.Single(logs.Events, static captured => captured.Level == LogLevel.Error && !captured.Message.Contains(" responded 500", StringComparison.Ordinal));
            CapturedEvent answered = Assert.Single(logs.Events, static captured => Answered(captured, "internal"));
            Assert.Equal(LogLevel.Warning, answered.Level);

            // --- The problems instrument: its status and code alone ---
            CollectedMeasurement<long> measurement = Assert.Single(problems.GetMeasurementSnapshot());
            Assert.Equal(500, measurement.Tags[ApiMetrics.StatusTag]);
            Assert.Equal("internal", measurement.Tags[ApiMetrics.CodeTag]);
        }

        [Fact]
        public async Task A_client_problem_is_logged_at_information_under_the_request_scope()
        {
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await StartAsync(options => options.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(logs));

            using HttpResponseMessage response = await ThrowAsync(host, "not-found");

            CapturedEvent answered = Assert.Single(logs.Events, static captured => Answered(captured, "not-found"));
            Assert.Equal(LogLevel.Information, answered.Level);
            Assert.Equal("web", answered.Members["Client"]);
            Assert.Equal("1", answered.Members["TenantId"]);
            Assert.DoesNotContain(logs.Events, static captured => captured.Level >= LogLevel.Error);
        }

        /// <summary>Whether an event is the line a problem's answer is logged with.</summary>
        /// <param name="captured">The event.</param>
        /// <param name="code">The problem's code.</param>
        /// <returns>True when it is that problem's line.</returns>
        internal static bool Answered(CapturedEvent captured, string code)
        {
            return captured.Message.Contains("was answered with the problem", StringComparison.Ordinal)
                && captured.Members.GetValueOrDefault("Code") == code;
        }

        /// <summary>A member of a body by its dotted path, each list item by its bracketed index.</summary>
        /// <param name="body">The body.</param>
        /// <param name="path">The path, such as <c>errorDetails.conflicts[0].id</c>.</param>
        /// <returns>The member.</returns>
        private static JsonElement At(JsonElement body, string path)
        {
            JsonElement current = body;
            foreach (string part in path.Split('.'))
            {
                // --- The member's name, then each index that follows it ---
                int bracket = part.IndexOf('[', StringComparison.Ordinal);
                current = current.GetProperty(bracket < 0 ? part : part[..bracket]);
                for (int open = bracket; open >= 0; open = part.IndexOf('[', open + 1))
                {
                    int close = part.IndexOf(']', open);
                    current = current[int.Parse(part[(open + 1)..close], System.Globalization.CultureInfo.InvariantCulture)];
                }
            }

            return current;
        }

        /// <summary>Starts a host whose web surface maps the throwing endpoint.</summary>
        /// <param name="configure">More changes to the host.</param>
        /// <returns>The started host.</returns>
        internal static Task<TellmaTestHost> StartAsync(Action<TestHostOptions>? configure = null)
        {
            return TellmaTestHost.StartAsync(options =>
            {
                options.Map = static (_, endpoints) => endpoints.Web.MapGet("/throw/{name}", Throw).WithMutation(false).AllowMember();
                configure?.Invoke(options);
            });
        }

        /// <summary>Calls the throwing endpoint as the SPA does.</summary>
        /// <param name="host">The host.</param>
        /// <param name="name">The exception's name.</param>
        /// <returns>The response.</returns>
        internal static async Task<HttpResponseMessage> ThrowAsync(TellmaTestHost host, string name)
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/throw/" + name, host.Cookie());
            return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        /// <summary>Calls the throwing endpoint and reads its problem body.</summary>
        /// <param name="host">The host.</param>
        /// <param name="name">The exception's name.</param>
        /// <returns>The body.</returns>
        private static async Task<JsonDocument> BodyAsync(TellmaTestHost host, string name)
        {
            using HttpResponseMessage response = await ThrowAsync(host, name);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        /// <summary>Throws the named exception.</summary>
        /// <param name="name">The exception's name.</param>
        /// <returns>Never.</returns>
        private static IResult Throw(string name)
        {
            throw Exceptions[name]();
        }
    }
}
