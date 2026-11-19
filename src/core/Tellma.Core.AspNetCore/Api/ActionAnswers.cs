// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §2.3 and §3.9: an action's result serializes like any envelope under its declared
    // type; a job hand-off answers 202 with its polling target in the body; an Excel result streams.

    /// <summary>How an action's result is answered on the wire.</summary>
    internal static class ActionAnswers
    {
        /// <summary>The media type of a workbook.</summary>
        public const string WorkbookContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        /// <summary>The media type of a JSON answer.</summary>
        public const string JsonContentType = "application/json; charset=utf-8";

        /// <summary>The status an action's result answers with, by its declared type.</summary>
        /// <param name="declared">The action's declared result type.</param>
        /// <returns>202 for a job hand-off, 204 for an action without a result, else 200.</returns>
        public static int StatusOf(Type declared)
        {
            ArgumentNullException.ThrowIfNull(declared);
            return declared == typeof(JobAccepted)
                ? StatusCodes.Status202Accepted
                : declared == typeof(void) ? StatusCodes.Status204NoContent : StatusCodes.Status200OK;
        }

        /// <summary>The answer to an action's result.</summary>
        /// <param name="result">The result; null for an action without one.</param>
        /// <param name="declared">The action's declared result type.</param>
        /// <returns>The endpoint's result.</returns>
        public static IResult Of(object? result, Type declared)
        {
            ArgumentNullException.ThrowIfNull(declared);
            return result switch
            {
                _ when declared == typeof(void) => TypedResults.NoContent(),
                ExportOutcome workbook => new WorkbookResult(workbook),

                // A document the service serialized once and caches (the client settings): its
                // bytes are the body as they are; the tag travels in the version-tags header.
                ValueTuple<byte[], string> document when declared == typeof(ValueTuple<byte[], string>)
                    => new Utf8JsonResult(document.Item1 ?? throw new InvalidOperationException("A pre-serialized JSON result carries no bytes.")),
                _ => new ContractJsonResult(result, declared, StatusOf(declared)),
            };
        }

        /// <summary>Whether a declared result type is a pre-serialized JSON document, written as its bytes.</summary>
        /// <param name="declared">The action's declared result type.</param>
        /// <returns>True for <c>(byte[] Json, string WireTag)</c>.</returns>
        public static bool IsPreSerialized(Type declared)
        {
            return declared == typeof(ValueTuple<byte[], string>);
        }

        /// <summary>A JSON document already serialized as UTF-8, written as it is.</summary>
        /// <param name="utf8">The document's bytes.</param>
        private sealed class Utf8JsonResult(byte[] utf8) : IResult, IStatusCodeHttpResult, IContentTypeHttpResult
        {
            /// <inheritdoc />
            public int? StatusCode => StatusCodes.Status200OK;

            /// <inheritdoc />
            public string ContentType => JsonContentType;

            /// <inheritdoc />
            public Task ExecuteAsync(HttpContext httpContext)
            {
                ArgumentNullException.ThrowIfNull(httpContext);
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = JsonContentType;
                httpContext.Response.ContentLength = utf8.Length;
                return httpContext.Response.Body.WriteAsync(utf8, httpContext.RequestAborted).AsTask();
            }
        }

        /// <summary>A JSON answer written under its declared type with the host's HTTP JSON options.</summary>
        /// <param name="value">The value.</param>
        /// <param name="declared">The declared type, which the contract fingerprint describes.</param>
        /// <param name="status">The status code.</param>
        private sealed class ContractJsonResult(object? value, Type declared, int status) : IResult, IStatusCodeHttpResult, IContentTypeHttpResult
        {
            /// <inheritdoc />
            public int? StatusCode => status;

            /// <inheritdoc />
            public string ContentType => JsonContentType;

            /// <inheritdoc />
            public Task ExecuteAsync(HttpContext httpContext)
            {
                ArgumentNullException.ThrowIfNull(httpContext);
                JsonOptions json = httpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value;
                httpContext.Response.StatusCode = status;
                return httpContext.Response.WriteAsJsonAsync(value, declared, json.SerializerOptions, JsonContentType, httpContext.RequestAborted);
            }
        }

        /// <summary>
        ///     A workbook streamed as an attachment, chunked: no length, no ranges. The outcome is
        ///     disposed once the response completes.
        /// </summary>
        /// <param name="workbook">The workbook.</param>
        private sealed class WorkbookResult(ExportOutcome workbook) : IResult, IStatusCodeHttpResult, IContentTypeHttpResult
        {
            /// <inheritdoc />
            public int? StatusCode => StatusCodes.Status200OK;

            /// <inheritdoc />
            public string ContentType => WorkbookContentType;

            /// <inheritdoc />
            public async Task ExecuteAsync(HttpContext httpContext)
            {
                ArgumentNullException.ThrowIfNull(httpContext);
                httpContext.Response.RegisterForDisposeAsync(workbook);

                // --- The headers: an attachment named in ASCII and in UTF-8 ---
                ContentDispositionHeaderValue disposition = new("attachment");
                disposition.SetHttpFileName(workbook.FileName);
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                httpContext.Response.ContentType = WorkbookContentType;
                httpContext.Response.Headers.ContentDisposition = disposition.ToString();

                // --- The body: copied as it is produced ---
                await workbook.Workbook.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted).ConfigureAwait(false);
            }
        }
    }
}
