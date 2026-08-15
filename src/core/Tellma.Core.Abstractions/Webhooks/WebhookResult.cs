// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Webhooks
{
    /// <summary>The receiver's response to a webhook call.</summary>
    /// <param name="Outcome">The semantic outcome; determines the HTTP status code.</param>
    /// <param name="Detail">Diagnostic detail for logs. Never sent to the external system.</param>
    /// <param name="ResponseBody">A response body, only for providers whose protocol requires one:
    ///     challenge echoes at endpoint verification, literal acknowledgment tokens. A connector that
    ///     needs full response control beyond this is an inbound API, not a webhook.</param>
    /// <param name="ResponseContentType">The content type of <paramref name="ResponseBody" />
    ///     (e.g. "text/plain"); required when a body is set — providers are strict about it.</param>
    public sealed record WebhookResult(
        WebhookOutcome Outcome,
        string? Detail = null,
        ReadOnlyMemory<byte>? ResponseBody = null,
        string? ResponseContentType = null);
}
