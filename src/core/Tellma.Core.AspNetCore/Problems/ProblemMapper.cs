// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;

namespace Tellma.Core.AspNetCore.Problems
{
    /// <summary>What the mapping needs to know of the caller beyond the exception.</summary>
    /// <param name="Root">What a validation path's leading index names on the operation that raised it.</param>
    /// <param name="RowType">The type of the operation's payload rows, which validation messages label properties from; null when it has none.</param>
    /// <param name="Development">Whether the backstop may name the exception's type and message.</param>
    internal sealed record ProblemMappingContext(ValidationRoot Root, Type? RowType, bool Development)
    {
        /// <summary>A caller with no payload rows, outside Development.</summary>
        public static ProblemMappingContext Default { get; } = new(ValidationRoot.Body, RowType: null, Development: false);
    }

    // Spec 0016 §7.1 and §7.5. The mapping is by exact type: every member of the closed set is
    // sealed, so no exception reaches a row through a base type, and a type the mapping does not
    // name is the 500 backstop.
    /// <summary>
    ///     Maps every outcome that reaches an edge to its problem: the closed set of exceptions by
    ///     type, the framework's request refusals by status, and everything else to the
    ///     <c>500 internal</c> backstop. The mapping reads no request, so the agent tools apply it
    ///     to a tool's failure as the web edge applies it to a response.
    /// </summary>
    internal static class ProblemMapper
    {
        /// <summary>The problem code of an exception outside the mapped set.</summary>
        public const string InternalCode = "internal";

        /// <summary>The problem code of a malformed request.</summary>
        public const string BadRequestCode = "bad-request";

        /// <summary>The problem code of a refused permission or policy.</summary>
        public const string ForbiddenCode = "forbidden";

        /// <summary>The problem code of a request over a limit.</summary>
        public const string LimitExceededCode = "limit-exceeded";

        /// <summary>The problem code of an unsupported media type.</summary>
        public const string UnsupportedMediaCode = "unsupported-media";

        /// <summary>The problem code of a dependency the operation could not reach.</summary>
        public const string DependencyUnavailableCode = "dependency-unavailable";

        /// <summary>The problem code of a refused upload, whatever its blob code.</summary>
        public const string BlobRejectedCode = "blob-rejected";

        /// <summary>The problem code of every validation failure, an import's included.</summary>
        public const string ValidationCode = "validation";

        /// <summary>The <c>arguments.reason</c> of a body that is not well-formed JSON.</summary>
        public const string MalformedJsonReason = "malformed-json";

        /// <summary>The <c>arguments.dependency</c> of a busy image processor.</summary>
        public const string ImagingDependency = "imaging";

        /// <summary>
        ///     The detail of a refusal that names no securable (a session that fails its surface's
        ///     policy, a component's bare 403): the general sentence, whose reason the refusal's
        ///     detail resource selects on.
        /// </summary>
        private static readonly ProblemDetail GeneralRefusal = new ProblemDetail.Resource(
            ProblemRenderer.TitleKey(ForbiddenCode) + ProblemRenderer.DetailSuffix,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["reason"] = "other" });

        /// <summary>The wait of an unavailable tenant that names none.</summary>
        public static readonly TimeSpan DefaultTenantRetryAfter = TimeSpan.FromSeconds(30);

        /// <summary>The wait of an unavailable dependency or a stale context that names none.</summary>
        public static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(1);

        /// <summary>The prefix of a query compiler code's message resource.</summary>
        private const string QueryexKeyPrefix = "Queryex_";

        /// <summary>The prefix every query compiler code starts with.</summary>
        private const string QueryexCodePrefix = "QX";

        /// <summary>Maps an exception to its problem.</summary>
        /// <param name="exception">The exception.</param>
        /// <param name="context">What the mapping needs to know of the caller.</param>
        /// <returns>The problem.</returns>
        public static ProblemDescription Map(Exception exception, ProblemMappingContext context)
        {
            ArgumentNullException.ThrowIfNull(exception);
            ArgumentNullException.ThrowIfNull(context);
            return exception switch
            {
                // --- 400: a request the operation cannot read ---
                BadRequestException bad => new(StatusCodes.Status400BadRequest, bad.Code)
                {
                    Arguments = Present(bad.Arguments),
                    Detail = bad.Detail is { } detail ? new ProblemDetail.Verbatim(detail) : null,
                },
                InvalidQueryException query => Query(query),
                BadHttpRequestException refused => Framework(refused),

                // --- 401 and 403: the caller, its credential, the tenant's state ---
                StepUpRequiredException stepUp => new(StatusCodes.Status401Unauthorized, stepUp.Code)
                {
                    StepUp = new StepUpDemand(stepUp.Acr, stepUp.MaxAge is { } age ? (int)Math.Ceiling(age.TotalSeconds) : null),
                },
                ForbiddenException forbidden => new(StatusCodes.Status403Forbidden, forbidden.Code)
                {
                    Arguments = Present(forbidden.Arguments),
                    Detail = new ProblemDetail.Securable(Reason(forbidden), forbidden.Resource, forbidden.Action, forbidden.Path),
                },
                IndividualRequiredException individual => new(StatusCodes.Status403Forbidden, individual.Code),

                // The tenancy rows carry no argument: a tenant's id or state never tells a caller
                // whether a tenant exists.
                TenantUnavailableException unavailable => Unavailable(unavailable),
                TenantNotFoundException => new(StatusCodes.Status404NotFound, TenantProblemCodes.NotFound),

                // --- 404 and 409: the rows the operation names ---
                NotFoundException notFound => new(StatusCodes.Status404NotFound, notFound.Code) { Arguments = Present(notFound.Arguments) },
                ConcurrencyException concurrency => new(StatusCodes.Status409Conflict, concurrency.Code)
                {
                    ErrorDetails = new ConflictDetails(concurrency.Conflicts),
                },
                ConfirmationRequiredException confirmation => new(StatusCodes.Status409Conflict, confirmation.Code)
                {
                    ErrorDetails = new ConfirmationDetails(confirmation.Requests),
                    // A confirmation's message is declared under its key as it is, which the composition requires.
                    Detail = new ProblemDetail.Resource(confirmation.Requests[0].Key, confirmation.Requests[0].Arguments),
                },
                TenantStateException state => new(StatusCodes.Status409Conflict, state.Code) { Detail = new ProblemDetail.Verbatim(state.Detail) },
                CountMismatchException mismatch => new(StatusCodes.Status409Conflict, mismatch.Code) { Arguments = Present(mismatch.Arguments) },

                // --- 413 and 422: the payload ---
                LimitExceededException exceeded => new(StatusCodes.Status413PayloadTooLarge, exceeded.Code) { Arguments = Present(exceeded.Arguments) },
                ValidationException validation => new(StatusCodes.Status422UnprocessableEntity, validation.Code)
                {
                    Errors = [.. validation.Errors.Select(error => Validation(error, context))],
                },
                ImportException import => Import(import),
                BlobRejectedException blob => Blob(blob),

                // --- 502 and 503: what the operation depends on ---
                PartialFailureException partial => new(StatusCodes.Status502BadGateway, partial.Code)
                {
                    ErrorDetails = new PartialFailureDetails(partial.Results, partial.Failed),
                },
                StaleContextException stale => new(StatusCodes.Status503ServiceUnavailable, stale.Code)
                {
                    RetryAfter = DefaultRetryAfter,
                    ErrorDetails = new StaleContextDetails([.. stale.Dependencies.Select(static dependency => dependency.Name)]),
                },
                DependencyUnavailableException dependency => new(StatusCodes.Status503ServiceUnavailable, dependency.Code)
                {
                    Arguments = Present(dependency.Arguments),
                    RetryAfter = dependency.RetryAfter ?? DefaultRetryAfter,
                },
                ImageProcessingBusyException busy => new(StatusCodes.Status503ServiceUnavailable, DependencyUnavailableCode)
                {
                    Arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["dependency"] = ImagingDependency },
                    RetryAfter = busy.RetryAfter,
                },

                // --- The backstop: the trace id alone, and in Development the exception's type and message ---
                _ => Internal(exception, context.Development),
            };
        }

        /// <summary>
        ///     The problem the framework's refusal of a request without a body is answered with, by
        ///     its status: a route miss under a tenant prefix, a verb the route does not accept, a body
        ///     over its ceiling, a rate limit, a timeout.
        /// </summary>
        /// <param name="status">The status the framework set.</param>
        /// <param name="routeMiss">Whether no endpoint matched a path under a tenant id, so a 404 is a route miss rather than an entity miss.</param>
        /// <returns>The problem, or null for a status no problem code names.</returns>
        public static ProblemDescription? ForStatus(int status, bool routeMiss)
        {
            string? code = status switch
            {
                StatusCodes.Status400BadRequest => BadRequestCode,
                StatusCodes.Status401Unauthorized => "unauthenticated",
                StatusCodes.Status403Forbidden => ForbiddenCode,
                StatusCodes.Status404NotFound => routeMiss ? "route-not-found" : "not-found",
                StatusCodes.Status405MethodNotAllowed => "method-not-allowed",
                StatusCodes.Status413PayloadTooLarge => LimitExceededCode,
                StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaCode,
                StatusCodes.Status429TooManyRequests => "rate-limited",
                StatusCodes.Status500InternalServerError => InternalCode,
                StatusCodes.Status504GatewayTimeout => "timeout",
                _ => null,
            };

            return code switch
            {
                null => null,

                // A body over its ceiling names the limit; its size is unknown, so no detail renders it.
                LimitExceededCode => new(status, code) { Arguments = BodyLimit(), Detail = ProblemDetail.None },
                _ => Named(status, code),
            };
        }

        /// <summary>
        ///     The problem a component answers by its status and code alone: its code's own detail,
        ///     except a refusal naming no securable, whose detail is the general sentence.
        /// </summary>
        /// <param name="status">The status.</param>
        /// <param name="code">The kebab-case problem code.</param>
        /// <returns>The problem.</returns>
        public static ProblemDescription Named(int status, string code)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            return string.Equals(code, ForbiddenCode, StringComparison.Ordinal)
                ? new(status, code) { Detail = GeneralRefusal }
                : new(status, code);
        }

        /// <summary>The resource key of a dotted code's message: each <c>.</c> written <c>_</c>.</summary>
        /// <param name="code">The code, such as <c>Tree.Cycle</c>.</param>
        /// <returns>The key, such as <c>Tree_Cycle</c>.</returns>
        public static string MessageKey(string code)
        {
            ArgumentNullException.ThrowIfNull(code);
            return code.Replace('.', '_');
        }

        /// <summary>The wire form of a refusal's reason, the other branch when it names no action to render.</summary>
        /// <param name="forbidden">The refusal.</param>
        /// <returns>The reason the detail is rendered for.</returns>
        private static string Reason(ForbiddenException forbidden)
        {
            // The grant-shaped sentences name the action; one without an action reads as the general one.
            return forbidden.Reason == ForbiddenReason.RestrictedPath
                ? "restricted-path"
                : forbidden.Action is null
                    ? "other"
                    : forbidden.Reason == ForbiddenReason.OutsideFilter ? "outside-filter" : "no-grant";
        }

        /// <summary>A query's diagnostics: one item per diagnostic, keyed by its clause, and the diagnostics as discovery answers them, for editors.</summary>
        /// <param name="query">The exception.</param>
        /// <returns>The problem.</returns>
        private static ProblemDescription Query(InvalidQueryException query)
        {
            List<ProblemError> errors = [];
            List<QueryDiagnostic> diagnostics = [];
            foreach (QueryexDiagnostic diagnostic in query.Diagnostics)
            {
                // One mapping to the wire, the discovery endpoint's: the clause, the item within it, the arguments.
                QueryDiagnostic wire = QueryWireDiagnostics.ToWire(diagnostic);
                string key = diagnostic.Code.StartsWith(QueryexCodePrefix, StringComparison.Ordinal)
                    ? QueryexKeyPrefix + diagnostic.Code
                    : MessageKey(diagnostic.Code);
                errors.Add(new ProblemError(diagnostic.Code, key, wire.Arguments) { Path = wire.Clause });
                diagnostics.Add(wire);
            }

            return new ProblemDescription(StatusCodes.Status400BadRequest, query.Code)
            {
                Errors = errors,
                ErrorDetails = new QueryDetails(diagnostics),
            };
        }

        /// <summary>A refused request the framework raised: a malformed body, a body over its ceiling, an unsupported media type.</summary>
        /// <param name="refused">The exception.</param>
        /// <returns>The problem.</returns>
        private static ProblemDescription Framework(BadHttpRequestException refused)
        {
            // The framework's own message names parameters and readers: developer text, never on the wire.
            return refused.StatusCode switch
            {
                StatusCodes.Status413PayloadTooLarge => new(StatusCodes.Status413PayloadTooLarge, LimitExceededCode)
                {
                    Arguments = BodyLimit(),
                    Detail = ProblemDetail.None,
                },
                StatusCodes.Status415UnsupportedMediaType => new(StatusCodes.Status415UnsupportedMediaType, UnsupportedMediaCode),
                StatusCodes.Status400BadRequest when IsMalformedJson(refused) => new(StatusCodes.Status400BadRequest, BadRequestCode)
                {
                    Arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["reason"] = MalformedJsonReason },
                },
                // Another refusal of the request (a missing body, a read that timed out) keeps its own status.
                int status when status is >= 400 and < 500 => ForStatus(status, routeMiss: false) ?? new ProblemDescription(status, BadRequestCode),
                _ => new ProblemDescription(StatusCodes.Status400BadRequest, BadRequestCode),
            };
        }

        /// <summary>Whether a refusal was caused by a body that is not well-formed JSON or nests too deep.</summary>
        /// <param name="refused">The refusal.</param>
        /// <returns>True when a JSON reader's failure is among its causes.</returns>
        private static bool IsMalformedJson(BadHttpRequestException refused)
        {
            for (Exception? cause = refused.InnerException; cause is not null; cause = cause.InnerException)
            {
                if (cause is JsonException)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>An unavailable tenant, whose status depends on its code.</summary>
        /// <param name="unavailable">The exception.</param>
        /// <returns>The problem.</returns>
        private static ProblemDescription Unavailable(TenantUnavailableException unavailable)
        {
            return unavailable.Code switch
            {
                // A background scope's refusal of a provisioning or retired tenant: the same answer as an absent tenant.
                TenantProblemCodes.NotFound => new(StatusCodes.Status404NotFound, TenantProblemCodes.NotFound),

                // Waiting changes these: the catalog comes back, the upgrade finishes.
                TenantProblemCodes.CatalogUnavailable or TenantProblemCodes.SchemaBehind => new(StatusCodes.Status503ServiceUnavailable, unavailable.Code)
                {
                    RetryAfter = unavailable.RetryAfter ?? DefaultTenantRetryAfter,
                },

                // Suspended, read-only and frozen: an operator's state, which waiting does not change.
                _ => new(StatusCodes.Status403Forbidden, unavailable.Code),
            };
        }

        /// <summary>One validation error as a body item: its path rendered, its property's owner found for the message's label.</summary>
        /// <param name="error">The error.</param>
        /// <param name="context">What a leading index names, and the payload's row type.</param>
        /// <returns>The item.</returns>
        private static ProblemError Validation(ValidationError error, ProblemMappingContext context)
        {
            return new ProblemError(error.Code, MessageKey(error.Code), error.Arguments)
            {
                Path = ValidationPaths.Render(error.Path, context.Root),
                Entity = ValidationPaths.OwnerOfLastProperty(error.Path, context.Root, context.RowType),
            };
        }

        /// <summary>An import's errors at their cells, with how many exist and which rows were committed.</summary>
        /// <param name="import">The exception.</param>
        /// <returns>The problem.</returns>
        private static ProblemDescription Import(ImportException import)
        {
            return new ProblemDescription(StatusCodes.Status422UnprocessableEntity, ValidationCode)
            {
                Errors =
                [
                    .. import.Errors.Select(static error => new ProblemError(error.Code, MessageKey(error.Code), error.Arguments)
                    {
                        Sheet = error.Sheet,
                        Row = error.Row,
                        Column = error.Column,
                        Header = error.Header,
                        Property = error.Property,
                    }),
                ],
                ErrorDetails = new ImportDetails(import.TotalErrors, import.CommittedRowRanges),
            };
        }

        /// <summary>A refused upload, whose status depends on its blob code.</summary>
        /// <param name="blob">The exception.</param>
        /// <returns>The problem.</returns>
        private static ProblemDescription Blob(BlobRejectedException blob)
        {
            int status = blob.Code switch
            {
                BlobRejectedException.TooLarge => StatusCodes.Status413PayloadTooLarge,
                BlobRejectedException.UnsupportedType => StatusCodes.Status415UnsupportedMediaType,
                BlobRejectedException.UnknownKind => StatusCodes.Status404NotFound,
                BlobRejectedException.LengthRequired => StatusCodes.Status411LengthRequired,
                _ => StatusCodes.Status422UnprocessableEntity,
            };
            return new ProblemDescription(status, BlobRejectedCode)
            {
                Errors = [new ProblemError(blob.Code, MessageKey(blob.Code), blob.Arguments) { Path = ValidationPaths.BodyPath }],
            };
        }

        /// <summary>The backstop of an exception outside the mapped set.</summary>
        /// <param name="exception">The exception.</param>
        /// <param name="development">Whether the body may name the exception's type and message.</param>
        /// <returns>The problem.</returns>
        private static ProblemDescription Internal(Exception exception, bool development)
        {
            // Type and message only, never a stack: enough for a developer, and only in Development.
            return new ProblemDescription(StatusCodes.Status500InternalServerError, InternalCode)
            {
                IsUnhandled = true,
                Arguments = development
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["exception"] = string.Create(CultureInfo.InvariantCulture, $"{exception.GetType().FullName}: {exception.Message}"),
                    }
                    : null,
            };
        }

        /// <summary>The arguments of a body over its ceiling.</summary>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> BodyLimit()
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal) { ["limit"] = LimitNames.Body };
        }

        /// <summary>An exception's arguments, or null when it carries none.</summary>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The arguments, or null.</returns>
        private static IReadOnlyDictionary<string, object?>? Present(IReadOnlyDictionary<string, object?> arguments)
        {
            return arguments.Count > 0 ? arguments : null;
        }

        /// <summary>The extras of a concurrency conflict.</summary>
        /// <param name="Conflicts">The rows changed since the caller loaded them.</param>
        private sealed record ConflictDetails(IReadOnlyList<ConcurrencyConflict> Conflicts);

        /// <summary>The extras of a confirmation request.</summary>
        /// <param name="Confirmations">One item per unconfirmed warning.</param>
        private sealed record ConfirmationDetails(IReadOnlyList<ConfirmationRequest> Confirmations);

        /// <summary>The extras of an invalid query.</summary>
        /// <param name="Diagnostics">The diagnostics, for editors.</param>
        private sealed record QueryDetails(IReadOnlyList<QueryDiagnostic> Diagnostics);

        /// <summary>The extras of an import that failed.</summary>
        /// <param name="TotalErrors">How many errors exist, reported or not.</param>
        /// <param name="CommittedRowRanges">The sheet rows already committed.</param>
        private sealed record ImportDetails(int TotalErrors, IReadOnlyList<RowRange> CommittedRowRanges);

        /// <summary>The extras of a partial failure.</summary>
        /// <param name="Results">The partial results.</param>
        /// <param name="Failed">What failed.</param>
        private sealed record PartialFailureDetails(object Results, IReadOnlyList<string> Failed);

        /// <summary>The extras of a stale context.</summary>
        /// <param name="Dependencies">The names of the inputs that moved.</param>
        private sealed record StaleContextDetails(IReadOnlyList<string> Dependencies);
    }
}
