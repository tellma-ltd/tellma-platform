// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Connector.SendGrid
{
    /// <summary>The outcome of one mail-send request.</summary>
    /// <param name="StatusCode">The HTTP status SendGrid answered with.</param>
    /// <param name="MessageId">The <c>X-Message-Id</c> response header, when present.</param>
    /// <param name="Errors">The parsed <c>errors[]</c> array on a failure; empty on success and on a
    ///     failure whose body was not JSON.</param>
    /// <param name="RawErrorBody">The failure body verbatim, kept because a body this client could
    ///     not parse is exactly the one an operator needs to see.</param>
    public sealed record SendGridSendResult(
        int StatusCode,
        string? MessageId,
        IReadOnlyList<SendGridError> Errors,
        string? RawErrorBody)
    {
        /// <summary>
        ///     Whether SendGrid accepted the payload. Any 2xx counts: a normal send answers 202,
        ///     while a sandbox-mode send answers 200.
        /// </summary>
        public bool IsSuccess => StatusCode is >= 200 and < 300;

        /// <summary>The error messages joined for a human-readable failure detail.</summary>
        /// <returns>The joined messages, the raw body when none were parsed, or null when neither exists.</returns>
        public string? DescribeErrors()
        {
            return Errors.Count == 0
                ? RawErrorBody
                : string.Join("; ", Errors.Select(static e =>
                    string.IsNullOrEmpty(e.Field) ? e.Message : $"{e.Field}: {e.Message}"));
        }
    }

    /// <summary>One entry of SendGrid's <c>errors[]</c> failure array.</summary>
    /// <param name="Message">The human-readable message.</param>
    /// <param name="Field">The payload field it concerns, when SendGrid names one.</param>
    /// <param name="Help">A documentation pointer, when SendGrid supplies one.</param>
    public sealed record SendGridError(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("field")] string? Field = null,
        [property: JsonPropertyName("help")] string? Help = null);

    /// <summary>The body SendGrid returns on a failed request.</summary>
    /// <param name="Errors">The individual errors.</param>
    public sealed record SendGridErrorResponse(
        [property: JsonPropertyName("errors")] IReadOnlyList<SendGridError>? Errors);
}
