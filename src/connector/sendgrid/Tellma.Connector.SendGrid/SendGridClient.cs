// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tellma.Connector.SendGrid
{
    /// <summary>
    ///     A typed client over the one SendGrid endpoint the platform sends through:
    ///     <c>POST /v3/mail/send</c>.
    /// </summary>
    /// <remarks>
    ///     Deliberately retry-free. Durable retry policy belongs to the caller, which owns the
    ///     durability; a retry here would silently duplicate mail the caller believes failed.
    /// </remarks>
    public sealed class SendGridClient
    {
        /// <summary>The path of the mail-send endpoint, relative to the API root.</summary>
        public const string MailSendPath = "/v3/mail/send";

        /// <summary>The response header carrying SendGrid's own id for an accepted message.</summary>
        public const string MessageIdHeaderName = "X-Message-Id";

        private readonly HttpClient _httpClient;
        private readonly SendGridClientOptions _options;

        /// <summary>Creates a client over an <see cref="HttpClient" /> the caller owns.</summary>
        /// <param name="httpClient">The transport; hosts wire it through a factory, tests through a
        ///     scripted handler.</param>
        /// <param name="options">The API key, timeout, and root.</param>
        public SendGridClient(HttpClient httpClient, SendGridClientOptions options)
        {
            ArgumentNullException.ThrowIfNull(httpClient);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiKey);

            _httpClient = httpClient;
            _options = options;
        }

        /// <summary>Sends one message.</summary>
        /// <param name="request">The mail-send payload.</param>
        /// <param name="cancellationToken">Abandons the request.</param>
        /// <returns>The status, the message id when accepted, and the parsed errors when not.</returns>
        /// <exception cref="TimeoutException">The request exceeded the configured timeout while the
        ///     caller's own token was still live.</exception>
        public async Task<SendGridSendResult> SendAsync(
            SendGridMailRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            using HttpRequestMessage httpRequest = new(
                HttpMethod.Post, new Uri(_options.EffectiveBaseAddress, MailSendPath));

            // Per request rather than on DefaultRequestHeaders: the HttpClient is injected and may be
            // shared, and this is what lets an API-key rotation take effect on the next batch.
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            httpRequest.Content = JsonContent.Create(request, SendGridJsonContext.Default.SendGridMailRequest);

            // The client owns the timeout so that "this request timed out" (a transient failure) can
            // be told apart from "the caller abandoned the batch" (which must propagate).
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(_options.Timeout);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(httpRequest, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The SendGrid mail-send request did not complete within {_options.Timeout}.");
            }

            using (response)
            {
                int statusCode = (int)response.StatusCode;
                string? messageId = response.Headers.TryGetValues(MessageIdHeaderName, out IEnumerable<string>? values)
                    ? values.FirstOrDefault()
                    : null;

                if (response.IsSuccessStatusCode)
                {
                    return new SendGridSendResult(statusCode, messageId, [], null);
                }

                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                return new SendGridSendResult(statusCode, messageId, ParseErrors(body), body);
            }
        }

        private static IReadOnlyList<SendGridError> ParseErrors(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return [];
            }

            try
            {
                SendGridErrorResponse? parsed = JsonSerializer.Deserialize(
                    body, SendGridJsonContext.Default.SendGridErrorResponse);
                return parsed?.Errors ?? [];
            }
            catch (JsonException)
            {
                // A non-JSON failure body (a gateway error page, a truncated response) is kept
                // verbatim on the result instead; parsing it is not worth failing over.
                return [];
            }
        }
    }
}
