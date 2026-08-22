// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Tellma.Connector.MarminAe.Tests.Infrastructure
{
    /// <summary>Builds the responses a scripted wire hands back.</summary>
    internal static class MarminAeResponses
    {
        /// <summary>A token response in the shape the vendor actually sends.</summary>
        /// <remarks>
        ///     A lifetime under <c>expires_in</c>, not the instant under <c>expires_at</c> the
        ///     vendor's reference describes. Every test that does not care about the encoding should
        ///     drive the encoding production sees; the ones that do care build their own bodies.
        /// </remarks>
        /// <param name="token">The token.</param>
        /// <param name="lifetime">How long the vendor says it lives.</param>
        /// <returns>The response.</returns>
        internal static HttpResponseMessage Token(string token, TimeSpan lifetime)
        {
            return Json(
                HttpStatusCode.OK,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $$"""{"expires_in":{{(long)lifetime.TotalSeconds}},"token":"{{token}}"}"""));
        }

        /// <summary>A JSON response.</summary>
        /// <param name="statusCode">The status.</param>
        /// <param name="body">The body.</param>
        /// <returns>The response.</returns>
        internal static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }

        /// <summary>A response carrying bytes rather than text.</summary>
        /// <param name="statusCode">The status.</param>
        /// <param name="content">The bytes.</param>
        /// <param name="contentType">The media type.</param>
        /// <param name="fileName">The name to suggest, when the vendor suggests one.</param>
        /// <param name="suggestedFileNameHeader">Whether to use the vendor's dedicated header
        ///     rather than the disposition header.</param>
        /// <returns>The response.</returns>
        internal static HttpResponseMessage Binary(
            HttpStatusCode statusCode,
            byte[] content,
            string contentType,
            string? fileName = null,
            bool suggestedFileNameHeader = false)
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new ByteArrayContent(content),
            };
            response.Content.Headers.ContentType =
                new MediaTypeHeaderValue(contentType);

            if (fileName is not null)
            {
                if (suggestedFileNameHeader)
                {
                    response.Headers.TryAddWithoutValidation(
                        MarminAeClient.SuggestedFileNameHeaderName, fileName);
                }
                else
                {
                    response.Content.Headers.TryAddWithoutValidation(
                        "Content-Disposition", $"attachment; filename=\"{fileName}\"");
                }
            }

            return response;
        }

        /// <summary>Adds the quota headers the vendor puts on every response.</summary>
        /// <param name="response">The response.</param>
        /// <param name="limit">How many calls the window allows.</param>
        /// <param name="remaining">How many are left.</param>
        /// <param name="resetsAt">When it starts over.</param>
        /// <param name="retryAfterSeconds">How long to wait, when the vendor says.</param>
        /// <returns>The same response.</returns>
        internal static HttpResponseMessage WithRateLimit(
            HttpResponseMessage response,
            int? limit = 60,
            int? remaining = 59,
            DateTimeOffset? resetsAt = null,
            int? retryAfterSeconds = null)
        {
            ArgumentNullException.ThrowIfNull(response);

            if (limit is int limitValue)
            {
                response.Headers.TryAddWithoutValidation(
                    MarminAeClient.RateLimitLimitHeaderName,
                    limitValue.ToString(CultureInfo.InvariantCulture));
            }

            if (remaining is int remainingValue)
            {
                response.Headers.TryAddWithoutValidation(
                    MarminAeClient.RateLimitRemainingHeaderName,
                    remainingValue.ToString(CultureInfo.InvariantCulture));
            }

            if (resetsAt is DateTimeOffset reset)
            {
                response.Headers.TryAddWithoutValidation(
                    MarminAeClient.RateLimitResetHeaderName,
                    reset.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
            }

            if (retryAfterSeconds is int retryAfter)
            {
                response.Headers.TryAddWithoutValidation(
                    "Retry-After", retryAfter.ToString(CultureInfo.InvariantCulture));
            }

            return response;
        }
    }
}
