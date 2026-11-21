// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Globalization;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.Blobs;

namespace Tellma.Core.AspNetCore.Blobs
{
    // Spec 0017 §5.3 and §8.1: the two hand-mapped blob endpoints on the Blobs group, which brings
    // the cookie policy, the tenant guard, the telemetry stage and the witness. Neither evaluates a
    // securable itself: the blob service decides what the caller may stage and read, and every
    // storage key it hands back is under the scope's own tenant.

    /// <summary>The blob endpoints: the single-file upload and the download.</summary>
    internal static class BlobEndpoints
    {
        /// <summary>The upload's route, under the blob group.</summary>
        public const string UploadRoute = "/{kind}";

        /// <summary>The download's route, under the blob group.</summary>
        public const string DownloadRoute = "/{kind}/{id:int}";

        /// <summary>The longest file name an upload may name, in characters.</summary>
        public const int MaxFileNameLength = 255;

        /// <summary>
        ///     The slowest rate, in bytes per second, a download's deadline allows for its object on
        ///     top of the web timeout: 32 KiB/s, so a 100 MiB file has about 53 minutes more.
        /// </summary>
        public const int MinimumTransferRate = 32 * 1024;

        /// <summary>
        ///     The longest deadline any download is given, however large its object: a day, well
        ///     inside what a timer can wait.
        /// </summary>
        public static readonly TimeSpan MaxTransferDeadline = TimeSpan.FromDays(1);

        /// <summary>
        ///     Maps the upload and the download on the blob group: any connected active member may
        ///     call either, and the service's checks are the finer ones.
        /// </summary>
        /// <param name="blobs">The blob group.</param>
        /// <returns>The upload's and the download's builders.</returns>
        public static (RouteHandlerBuilder Upload, RouteHandlerBuilder Download) MapTellmaBlobs(this RouteGroupBuilder blobs)
        {
            ArgumentNullException.ThrowIfNull(blobs);
            IEndpointRouteBuilder routes = blobs;
            long maxUploadSize = routes.ServiceProvider.GetRequiredService<IOptions<BlobOptions>>().Value.MaxUploadSize;

            // --- The upload: a raw body of any declared type, a write, under the long timeout ---
            // The binary metadata raises the server's body limit to the global ceiling and waives
            // only the JSON content-type rule of the CSRF control, so Tellma-Client stays required.
            // A large file over a slow link outlasts the web timeout, so it takes the long one.
            RouteHandlerBuilder upload = blobs.MapPost(UploadRoute, UploadAsync)
                .AllowMember()
                .WithMutation(true)
                .AcceptsBinary(maxUploadSize);
            _ = upload.WithRequestTimeout(TellmaRequestTimeouts.Long);

            // --- The download: a read under the per-tenant blob concurrency, which replaces the group's rates ---
            // A browser cannot add Tellma-Client to an <img> load; a GET is a safe method, which the
            // CSRF control holds to the provenance rules alone.
            RouteHandlerBuilder download = blobs.MapGet(DownloadRoute, DownloadAsync)
                .AllowMember()
                .WithMutation(false)
                .RequireRatePolicy(TellmaRatePolicies.Blobs);

            return (upload, download);
        }

        /// <summary>
        ///     Stages one uploaded file of a kind: <c>201</c> with the staged blob and its address, or
        ///     the service's refusal (an unknown kind, a missing length, too large, an unsupported
        ///     type, the staging quota, a refused image).
        /// </summary>
        /// <param name="tenantId">The route's tenant, which the guard admitted the caller to.</param>
        /// <param name="kind">The kind the file is uploaded as.</param>
        /// <param name="fileName">The client's file name, kept for display; at most 255 characters.</param>
        /// <param name="request">The request, whose body is the file.</param>
        /// <param name="blobs">The blob service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The <c>201 Created</c> result.</returns>
        /// <exception cref="BadRequestException">The file name is longer than 255 characters.</exception>
        private static async Task<IResult> UploadAsync(
            int tenantId,
            string kind,
            [FromQuery] string? fileName,
            HttpRequest request,
            [FromServices] IBlobService blobs,
            CancellationToken cancellationToken)
        {
            if (fileName is { Length: > MaxFileNameLength })
            {
                throw new BadRequestException(
                    $"The file name is longer than {MaxFileNameLength.ToString(CultureInfo.InvariantCulture)} characters.",
                    new Dictionary<string, object?> { ["parameter"] = "fileName" });
            }

            // --- The body as the client declared it: no Content-Length is a length of -1, which the service refuses with 411 ---
            BlobStageRequest stage = new(kind, request.Body, request.ContentLength ?? -1, request.ContentType, fileName);
            BlobDescriptor staged = await blobs.StageAsync(stage, cancellationToken).ConfigureAwait(false);

            // The service's kind is a declared one, so the address it builds is the download's route.
            string location = string.Create(CultureInfo.InvariantCulture, $"/{tenantId}/blobs/{staged.Kind}/{staged.Id}");
            return Results.Created(location, staged);
        }

        /// <summary>
        ///     Serves one blob the caller may read: <c>304</c> when the caller already holds it, else
        ///     the bytes with range support (<c>200</c>, <c>206</c> or <c>416</c>); <c>404</c> for
        ///     anything the caller may not read or that does not exist, alike.
        /// </summary>
        /// <param name="kind">The kind the request names.</param>
        /// <param name="id">The blob's id.</param>
        /// <param name="variant">A stored variant, such as <c>thumb</c>; absent or empty for the primary.</param>
        /// <param name="download">Whether to download rather than open in the browser; absent is false.</param>
        /// <param name="http">The request.</param>
        /// <param name="blobs">The blob service, which authorizes the read.</param>
        /// <param name="store">The blob store.</param>
        /// <param name="kinds">The declared kinds, which bound the download counter's kind tag.</param>
        /// <param name="metrics">The download counter.</param>
        /// <param name="requests">The request context, whose tenant owns the storage read.</param>
        /// <param name="api">The API settings, whose web timeout a transfer's deadline starts from.</param>
        /// <param name="time">The clock the transfer's deadline runs on.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The result.</returns>
        /// <exception cref="BlobStoreException">The stored primary's length is not the length its row records.</exception>
        private static async Task<IResult> DownloadAsync(
            string kind,
            int id,
            [FromQuery] string? variant,
            [FromQuery] bool? download,
            HttpContext http,
            [FromServices] IBlobService blobs,
            [FromServices] IBlobStore store,
            [FromServices] IBlobKindRegistry kinds,
            [FromServices] BlobDownloadMetrics metrics,
            [FromServices] IRequestContextAccessor requests,
            [FromServices] IOptions<ApiOptions> api,
            [FromServices] TimeProvider time,
            CancellationToken cancellationToken)
        {
            // The framing and sniffing guards are the host's header stamp's, on every blob answer,
            // a refusal before this handler included.
            // A kind no reference declares is never counted: the tag would take whatever a client sends.
            string? counted = kinds.Find(kind)?.Kind;
            string? requested = string.IsNullOrEmpty(variant) ? null : variant;

            // --- The authorization, in one round trip: anything the caller may not read is a plain 404 ---
            BlobDownload? resolved = await blobs.ResolveAsync(kind, id, requested, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                Count(metrics, counted, BlobTelemetryTagValues.DownloadNotFound);
                return NotFound();
            }

            // --- A held copy is answered from the row, with its tag and caching, and without the store ---
            if (BlobResponseHeaders.IsNotModified(http.Request.Headers.IfNoneMatch, resolved.ETag))
            {
                BlobResponseHeaders.ApplyCaching(http.Response.Headers, resolved);
                Count(metrics, counted, BlobTelemetryTagValues.DownloadNotModified);
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }

            // --- The object, under the scope's own tenant, never one the route names ---
            int tenantId = requests.Current.RequireTenant().Id;
            BlobContent? content = await store.OpenReadAsync(tenantId, resolved.StorageName, cancellationToken).ConfigureAwait(false);
            if (content is null)
            {
                Count(metrics, counted, BlobTelemetryTagValues.DownloadNotFound);
                return NotFound();
            }

            // The response disposes the content whatever happens to it, a range refusal included.
            http.Response.RegisterForDisposeAsync(content);

            // The primary's length is its row's; a stored object of another length is corrupt, not servable.
            if (requested is null && content.Length != resolved.Size)
            {
                throw new BlobStoreException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The stored object of blob {resolved.Id} is {content.Length} bytes long, but its row records {resolved.Size}."),
                    inner: null);
            }

            // --- The tag and the caching only now, on an object that opened whole: a 404 or a 500 is never cached ---
            BlobResponseHeaders.ApplyCaching(http.Response.Headers, resolved);

            // --- The bytes: open inline or download, with ranges, conditionals and If-Range against the strong tag ---
            http.Response.Headers[HeaderNames.ContentDisposition] = BlobResponseHeaders.ContentDisposition(resolved.ContentType, resolved.FileName, download == true);
            CountServedAsItStarts(http.Response, metrics, counted);
            BoundTransfer(http, TransferDeadline(api.Value.RequestTimeout, content.Length), time);
            return Results.Stream(
                content.Content,
                resolved.ContentType,
                fileDownloadName: null,
                lastModified: null,
                entityTag: EntityTagHeaderValue.Parse(resolved.ETag),
                enableRangeProcessing: true);
        }

        /// <summary>
        ///     How long a download of an object may take from its open: the web timeout, the budget
        ///     every answer has for its first byte, plus the object's length at
        ///     <see cref="MinimumTransferRate" />, and never more than <see cref="MaxTransferDeadline" />.
        ///     A small object's hold is thus about the web timeout, however slowly it is read.
        /// </summary>
        /// <param name="webTimeout">The web request timeout.</param>
        /// <param name="length">The object's length in bytes.</param>
        /// <returns>The deadline.</returns>
        internal static TimeSpan TransferDeadline(TimeSpan webTimeout, long length)
        {
            double seconds = webTimeout.TotalSeconds + ((double)Math.Max(0, length) / MinimumTransferRate);
            return seconds >= MaxTransferDeadline.TotalSeconds ? MaxTransferDeadline : TimeSpan.FromSeconds(seconds);
        }

        /// <summary>
        ///     Replaces the web timeout, which covered the authorization and the open, with the
        ///     transfer's own deadline, past which the connection is aborted. The blob concurrency
        ///     permit is held until the endpoint returns, so a client that reads slowly or not at all
        ///     holds it for no longer than the deadline.
        /// </summary>
        /// <param name="http">The request.</param>
        /// <param name="deadline">The transfer's deadline, from now.</param>
        /// <param name="time">The clock the deadline runs on.</param>
        private static void BoundTransfer(HttpContext http, TimeSpan deadline, TimeProvider time)
        {
            http.Features.Get<IHttpRequestTimeoutFeature>()?.DisableTimeout();

            // The registration is disposed before the source (disposal runs last-registered first),
            // and its disposal waits out a running abort, so a completed request is never aborted.
            CancellationTokenSource expiry = new(deadline, time);
            CancellationTokenRegistration abort = expiry.Token.Register(static state => ((HttpContext)state!).Abort(), http);
            http.Response.RegisterForDispose(expiry);
            http.Response.RegisterForDispose(abort);
        }

        /// <summary>
        ///     Counts a download as served when its response starts as <c>200</c> or <c>206</c>; a
        ///     range refusal or a failed precondition the stream answers sends no object, and is not
        ///     counted.
        /// </summary>
        /// <param name="response">The response.</param>
        /// <param name="metrics">The counter.</param>
        /// <param name="kind">The declared kind; null when the request named none.</param>
        private static void CountServedAsItStarts(HttpResponse response, BlobDownloadMetrics metrics, string? kind)
        {
            if (kind is null)
            {
                return;
            }

            response.OnStarting(() =>
            {
                if (response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status206PartialContent)
                {
                    metrics.Count(kind, BlobTelemetryTagValues.DownloadServed);
                }

                return Task.CompletedTask;
            });
        }

        /// <summary>The download's answer when there is nothing the caller may read.</summary>
        /// <returns>The <c>404 not-found</c> problem.</returns>
        private static ProblemResult NotFound()
        {
            return new ProblemResult(StatusCodes.Status404NotFound, "not-found");
        }

        /// <summary>Counts a download's outcome under a declared kind, or not at all.</summary>
        /// <param name="metrics">The counter.</param>
        /// <param name="kind">The declared kind; null when the request named none.</param>
        /// <param name="outcome">The outcome.</param>
        private static void Count(BlobDownloadMetrics metrics, string? kind, string outcome)
        {
            if (kind is not null)
            {
                metrics.Count(kind, outcome);
            }
        }
    }
}
