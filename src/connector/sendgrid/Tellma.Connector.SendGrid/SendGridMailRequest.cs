// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Connector.SendGrid
{
    /// <summary>The body of a <c>POST /v3/mail/send</c> request.</summary>
    /// <remarks>
    ///     Every property carries an explicit <see cref="JsonPropertyNameAttribute" /> rather than
    ///     relying on a naming policy: SendGrid expects <c>filename</c>, which no snake-case policy
    ///     produces. Declaration order is wire order under source generation, which is what keeps the
    ///     payload snapshots in the test suite stable.
    /// </remarks>
    public sealed record SendGridMailRequest
    {
        /// <summary>The recipient sets. This client sends exactly one per request.</summary>
        [JsonPropertyName("personalizations")]
        public required IReadOnlyList<SendGridPersonalization> Personalizations { get; init; }

        /// <summary>The sender.</summary>
        [JsonPropertyName("from")]
        public required SendGridEmailAddress From { get; init; }

        /// <summary>The reply-to address, when replies should divert from the sender.</summary>
        [JsonPropertyName("reply_to")]
        public SendGridEmailAddress? ReplyTo { get; init; }

        /// <summary>The subject.</summary>
        [JsonPropertyName("subject")]
        public required string Subject { get; init; }

        /// <summary>
        ///     The bodies. SendGrid requires <c>text/plain</c> before <c>text/html</c> and rejects the
        ///     reverse order, so this is data ordering, not property ordering.
        /// </summary>
        [JsonPropertyName("content")]
        public required IReadOnlyList<SendGridContent> Content { get; init; }

        /// <summary>The attachments, if any.</summary>
        [JsonPropertyName("attachments")]
        public IReadOnlyList<SendGridAttachment>? Attachments { get; init; }

        /// <summary>Key/value pairs echoed back on every delivery event for this message.</summary>
        [JsonPropertyName("custom_args")]
        public IReadOnlyDictionary<string, string>? CustomArgs { get; init; }

        /// <summary>Per-request settings, chiefly the sandbox mode.</summary>
        [JsonPropertyName("mail_settings")]
        public SendGridMailSettings? MailSettings { get; init; }
    }

    /// <summary>An address in a mail-send payload.</summary>
    /// <param name="Email">The address.</param>
    /// <param name="Name">The display name, when one is set.</param>
    public sealed record SendGridEmailAddress(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("name")] string? Name = null);

    /// <summary>One recipient set of a mail-send payload.</summary>
    /// <param name="To">The primary recipients.</param>
    /// <param name="Cc">The carbon-copy recipients.</param>
    /// <param name="Bcc">The blind-carbon-copy recipients.</param>
    public sealed record SendGridPersonalization(
        [property: JsonPropertyName("to")] IReadOnlyList<SendGridEmailAddress> To,
        [property: JsonPropertyName("cc")] IReadOnlyList<SendGridEmailAddress>? Cc = null,
        [property: JsonPropertyName("bcc")] IReadOnlyList<SendGridEmailAddress>? Bcc = null);

    /// <summary>One body part of a mail-send payload.</summary>
    /// <param name="Type">The MIME type ("text/plain", "text/html").</param>
    /// <param name="Value">The body.</param>
    public sealed record SendGridContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("value")] string Value);

    /// <summary>One attachment of a mail-send payload.</summary>
    /// <param name="Content">The base64-encoded bytes. A string, because that is the wire shape;
    ///     the caller encodes.</param>
    /// <param name="Type">The MIME content type.</param>
    /// <param name="Filename">The file name presented to the recipient. SendGrid spells this
    ///     property without a separator, which is why no naming policy can produce it.</param>
    /// <param name="Disposition">"inline" for a resource the HTML body references, "attachment"
    ///     otherwise.</param>
    /// <param name="ContentId">The content id an inline resource is referenced by.</param>
    public sealed record SendGridAttachment(
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("disposition")] string? Disposition = null,
        [property: JsonPropertyName("content_id")] string? ContentId = null);

    /// <summary>Per-request mail settings.</summary>
    /// <param name="SandboxMode">Validate the payload without delivering anything.</param>
    public sealed record SendGridMailSettings(
        [property: JsonPropertyName("sandbox_mode")] SendGridSandboxMode? SandboxMode = null);

    /// <summary>The sandbox-mode setting.</summary>
    /// <param name="Enable">True to validate without delivering.</param>
    public sealed record SendGridSandboxMode(
        [property: JsonPropertyName("enable")] bool Enable);
}
