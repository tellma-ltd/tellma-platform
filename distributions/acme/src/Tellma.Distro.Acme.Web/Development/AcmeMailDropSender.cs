// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Email;

namespace Tellma.Distro.Acme.Web.Development
{
    /// <summary>
    ///     The development mail drop's sender: writes each message to the drop directory as one JSON
    ///     file and delivers nothing. A reader never sees a partial file: each one is written under a
    ///     temporary name and then renamed into place.
    /// </summary>
    internal sealed class AcmeMailDropSender : IEmailSender
    {
        /// <summary>The extension of a dropped message.</summary>
        public const string Extension = ".json";

        /// <summary>The extension of a message while it is being written.</summary>
        private const string PartialExtension = ".partial";

        private readonly string _directory;
        private readonly EmailChannel _channel;
        private readonly TimeProvider _time;

        /// <summary>Creates the sender, refusing to run outside Development or without an absolute directory.</summary>
        /// <param name="environment">The host environment, consulted by the guard.</param>
        /// <param name="directory">The drop directory, from <see cref="AcmeMailDrop.DirectoryKey" />.</param>
        /// <param name="channel">The channel this instance stands in for, recorded in every file.</param>
        /// <param name="time">The clock that stamps each file.</param>
        /// <exception cref="InvalidOperationException">The environment is not Development, or the directory is missing or relative.</exception>
        public AcmeMailDropSender(IHostEnvironment environment, string? directory, EmailChannel channel, TimeProvider time)
        {
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(time);

            // The last tripwire, behind the startup check: a host that builds the sender by hand still
            // cannot write real mail to disk outside Development.
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"The development mail drop cannot run in the '{environment.EnvironmentName}' environment, where it would write real mail to disk instead of delivering it. Configure {AcmeMailDrop.ProviderKey} to a real transport.");
            }

            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            {
                throw new InvalidOperationException(
                    $"The development mail drop needs {AcmeMailDrop.DirectoryKey} set to an absolute directory, but it is '{directory}'.");
            }

            _directory = directory;
            _channel = channel;
            _time = time;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<EmailSendResult>> SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);
            cancellationToken.ThrowIfCancellationRequested();

            _ = Directory.CreateDirectory(_directory);
            List<EmailSendResult> results = new(messages.Count);
            foreach (EmailMessage message in messages)
            {
                // The drop is bound by the batch contract every transport keeps: a structurally invalid
                // message is rejected on its own rather than dropped as sent.
                if (!EmailMessageValidation.TryValidate(message, out string? error))
                {
                    results.Add(new EmailSendResult(EmailSendOutcome.Rejected, Error: error));
                    continue;
                }

                await WriteAsync(message, cancellationToken).ConfigureAwait(false);
                results.Add(new EmailSendResult(EmailSendOutcome.Sent));
            }

            return results;
        }

        /// <summary>Writes one message's file, under a temporary name first, then under its own.</summary>
        /// <param name="message">The message.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the file is in place.</returns>
        private async Task WriteAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            DateTimeOffset now = _time.GetUtcNow();
            AcmeDroppedMail mail = new(
                Version: 1,
                SentAt: now,
                Channel: _channel == EmailChannel.Live ? "live" : "sandbox",
                Audience: message.Audience == EmailAudience.Internal ? "internal" : "external",
                From: message.From is { } from ? Address(from) : null,
                To: [.. message.To.Select(Address)],
                Cc: [.. message.Cc.Select(Address)],
                Bcc: [.. message.Bcc.Select(Address)],
                Subject: message.Subject,
                TextBody: message.TextBody,
                HtmlBody: message.HtmlBody,
                Attachments: [.. message.Attachments.Select(static attachment => new AcmeDroppedAttachment(attachment.FileName, attachment.ContentType, attachment.Content.Length))]);

            // The name sorts by time, and the random part keeps two messages of one tick apart.
            string name = string.Create(CultureInfo.InvariantCulture, $"{now.UtcDateTime:yyyyMMdd'T'HHmmssfffffff'Z'}-{Guid.NewGuid():N}");
            string final = Path.Combine(_directory, name + Extension);
            string partial = Path.Combine(_directory, name + PartialExtension);

            FileStream stream = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, mail, AcmeMailDropJsonContext.Default.AcmeDroppedMail, cancellationToken).ConfigureAwait(false);
            }

            File.Move(partial, final);
        }

        /// <summary>One address as the file records it.</summary>
        /// <param name="address">The address.</param>
        /// <returns>The record.</returns>
        private static AcmeDroppedAddress Address(EmailAddress address)
        {
            return new AcmeDroppedAddress(address.Address, string.IsNullOrWhiteSpace(address.DisplayName) ? null : address.DisplayName);
        }
    }

    /// <summary>One dropped message, the file's whole content.</summary>
    /// <param name="Version">The file format's version: 1.</param>
    /// <param name="SentAt">When the message was dropped.</param>
    /// <param name="Channel">The channel: <c>live</c> or <c>sandbox</c>.</param>
    /// <param name="Audience">Whose inbox the message targeted: <c>internal</c> or <c>external</c>.</param>
    /// <param name="From">The sender, or null for the transport's default.</param>
    /// <param name="To">The primary recipients.</param>
    /// <param name="Cc">The carbon-copy recipients.</param>
    /// <param name="Bcc">The blind-carbon-copy recipients.</param>
    /// <param name="Subject">The subject.</param>
    /// <param name="TextBody">The plain-text body.</param>
    /// <param name="HtmlBody">The HTML body, or null.</param>
    /// <param name="Attachments">The attachments' names, types and sizes; never their bytes.</param>
    internal sealed record AcmeDroppedMail(
        int Version,
        DateTimeOffset SentAt,
        string Channel,
        string Audience,
        AcmeDroppedAddress? From,
        IReadOnlyList<AcmeDroppedAddress> To,
        IReadOnlyList<AcmeDroppedAddress> Cc,
        IReadOnlyList<AcmeDroppedAddress> Bcc,
        string Subject,
        string TextBody,
        string? HtmlBody,
        IReadOnlyList<AcmeDroppedAttachment> Attachments);

    /// <summary>One address of a dropped message.</summary>
    /// <param name="Address">The address.</param>
    /// <param name="DisplayName">The display name, or null.</param>
    internal sealed record AcmeDroppedAddress(string Address, string? DisplayName);

    /// <summary>One attachment of a dropped message, without its bytes.</summary>
    /// <param name="FileName">The file name.</param>
    /// <param name="ContentType">The media type.</param>
    /// <param name="Length">The size in bytes.</param>
    internal sealed record AcmeDroppedAttachment(string FileName, string ContentType, int Length);

    /// <summary>The source-generated serializer of the drop's files: camelCase, indented for a reader.</summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
    [JsonSerializable(typeof(AcmeDroppedMail))]
    internal sealed partial class AcmeMailDropJsonContext : JsonSerializerContext
    {
    }
}
