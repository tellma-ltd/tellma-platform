// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>Records every exchange the live suite has, redacted, for the failure report.</summary>
    /// <remarks>
    ///     <para>
    ///         The client is dependency-free and therefore logs nothing of its own, so this is where
    ///         a nightly failure gets its detail. Whoever reads one is hours late, cannot reproduce
    ///         it, and has only what the run wrote down.
    ///     </para>
    ///     <para>
    ///         The bearer, the signature and the secret never reach the output, and every body is
    ///         scrubbed of the account's own identity before it is written or attached. These logs
    ///         are published by a public repository's build runs.
    ///     </para>
    ///     <para>
    ///         Response bodies are also attached to the test result, named for the offline vector
    ///         each would become. That is the mechanism by which the offline parser suite gets real
    ///         captures instead of stand-ins.
    ///     </para>
    ///     <para>
    ///         Only JSON is recorded. A legal artifact runs to megabytes and is streamed to whoever
    ///         asked for it; buffering one here to write it into a log would defeat the streaming
    ///         and publish the rendered document. JSON is buffered because the client is about to
    ///         buffer it anyway.
    ///     </para>
    /// </remarks>
    /// <param name="innerHandler">The transport underneath.</param>
    /// <param name="accountValues">
    ///     Values that identify the account before any body is read, scrubbed from everything this
    ///     records even when the body is not JSON.
    /// </param>
    public sealed class MarminAeLiveTranscript(
        HttpMessageHandler innerHandler, IEnumerable<string> accountValues)
        : DelegatingHandler(innerHandler)
    {
        private const int MaxRecordedBodyLength = 64 * 1024;

        // The client's own ceiling on a submission, and past anything the vendor answers with.
        private const int MaxBufferedBodyLength = 8 * 1024 * 1024;

        // The token route, which the client keeps to itself; naming it here avoids widening the
        // library surface for the sake of a test transcript.
        private const string TokenPath = "auth/token";

        private readonly Lock _gate = new();
        private readonly StringBuilder _log = new();
        private readonly Dictionary<string, string> _attachments = new(StringComparer.Ordinal);
        private readonly string[] _accountValues = [.. accountValues];

        /// <summary>The name every token response is captured under.</summary>
        public const string TokenAttachmentName = "token-response.recorded.json";

        /// <summary>Names the next data response, so it lands as a usable capture.</summary>
        /// <remarks>
        ///     Set immediately before the call whose answer is worth keeping; cleared once used.
        ///     Token requests are captured under their own fixed name and never consume this,
        ///     because a data call may have to obtain a token first and would otherwise take the
        ///     name meant for its own answer.
        /// </remarks>
        public string? NextAttachmentName { get; set; }

        /// <summary>Writes the transcript to the test output and attaches what it captured.</summary>
        public void Publish()
        {
            lock (_gate)
            {
                if (_log.Length > 0)
                {
                    TestContext.Current.TestOutputHelper?.WriteLine(_log.ToString());
                }

                foreach (KeyValuePair<string, string> attachment in _attachments)
                {
                    TestContext.Current.AddAttachment(attachment.Key, attachment.Value);
                }

                _log.Clear();
                _attachments.Clear();
            }
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            bool isTokenRequest = request.RequestUri?.AbsolutePath.EndsWith(
                '/' + TokenPath, StringComparison.Ordinal) ?? false;

            string? attachmentName;
            if (isTokenRequest)
            {
                attachmentName = TokenAttachmentName;
            }
            else
            {
                attachmentName = NextAttachmentName;
                NextAttachmentName = null;
            }

            string requestBody = request.Content is null
                ? string.Empty
                : Scrub(await request.Content.ReadAsStringAsync(cancellationToken));

            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

            string responseBody = Scrub(await ReadBodyAsync(response, cancellationToken));

            lock (_gate)
            {
                _log.Append(CultureInfo.InvariantCulture, $"→ {request.Method} {Redact(request.RequestUri)}");
                _log.AppendLine();
                _log.AppendLine(CultureInfo.InvariantCulture, $"  {DescribeHeaders(request)}");

                if (requestBody.Length > 0)
                {
                    _log.AppendLine(CultureInfo.InvariantCulture, $"  body: {Truncate(requestBody)}");
                }

                _log.AppendLine(CultureInfo.InvariantCulture, $"← {(int)response.StatusCode} {response.ReasonPhrase}");
                _log.AppendLine(CultureInfo.InvariantCulture, $"  quota: {DescribeQuota(response)}");

                if (responseBody.Length > 0)
                {
                    _log.AppendLine(CultureInfo.InvariantCulture, $"  body: {Truncate(responseBody)}");
                }

                _log.AppendLine();

                if (attachmentName is not null && responseBody.Length > 0)
                {
                    _attachments[attachmentName] = responseBody;
                }
            }

            return response;
        }

        private static async Task<string> ReadBodyAsync(
            HttpResponseMessage response, CancellationToken cancellationToken)
        {
            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            // Buffering JSON costs nothing the caller was not about to pay anyway — it deserializes
            // the whole body — but a declared length past what any answer here plausibly is means
            // this is not that, and is left on the wire.
            if (response.Content.Headers.ContentLength > MaxBufferedBodyLength)
            {
                return string.Empty;
            }

            // Buffered so reading here does not consume the stream the caller is about to read.
            await response.Content.LoadIntoBufferAsync(cancellationToken);

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        private static string DescribeHeaders(HttpRequestMessage request)
        {
            IEnumerable<string> names = request.Headers.Select(static header =>
                IsSensitive(header.Key) ? header.Key + ": <redacted>" : header.Key + ": " + string.Join(',', header.Value));

            return string.Join("; ", names);
        }

        private static bool IsSensitive(string headerName)
        {
            return string.Equals(headerName, "Authorization", StringComparison.OrdinalIgnoreCase)
                || string.Equals(headerName, MarminAeTokenProvider.SignatureHeaderName, StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeQuota(HttpResponseMessage response)
        {
            string Read(params string[] names)
            {
                foreach (string name in names)
                {
                    if (response.Headers.TryGetValues(name, out IEnumerable<string>? values))
                    {
                        return string.Join(',', values);
                    }
                }

                return "-";
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"limit {Read(MarminAeClient.RateLimitLimitHeaderName, MarminAeClient.StandardRateLimitLimitHeaderName, MarminAeClient.RateLimitLimitPerMinuteHeaderName)}, remaining {Read(MarminAeClient.RateLimitRemainingHeaderName, MarminAeClient.StandardRateLimitRemainingHeaderName, MarminAeClient.RateLimitRemainingPerMinuteHeaderName)}, resets {Read(MarminAeClient.RateLimitResetHeaderName, MarminAeClient.StandardRateLimitResetHeaderName)}");
        }

        private string Redact(Uri? requestUri)
        {
            if (requestUri is null)
            {
                return "(no uri)";
            }

            // The client id rides in the token request's query string, and the business profile
            // rides in the path of every mutation. Neither is a secret, both name the account, and
            // the report already discloses as much of either as is useful.
            string described = requestUri.GetLeftPart(UriPartial.Path)
                + (requestUri.Query.Contains("client_id", StringComparison.Ordinal)
                    ? "?client_id=<redacted>"
                    : requestUri.Query);

            return Scrub(described);
        }

        private static string Truncate(string value)
        {
            return value.Length <= MaxRecordedBodyLength
                ? value
                : value[..MaxRecordedBodyLength] + "… [truncated]";
        }

        private string Scrub(string body)
        {
            return MarminAeLiveRedaction.Scrub(body, _accountValues);
        }
    }
}
