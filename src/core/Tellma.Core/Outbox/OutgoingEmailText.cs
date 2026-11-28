// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Outbox
{
    /// <summary>
    ///     The text rules the platform's own emails share before they reach the outbox: the tenant's
    ///     name, a recipient's mailbox with their name on one line, and a subject on one line that
    ///     fits its column.
    /// </summary>
    internal static class OutgoingEmailText
    {
        /// <summary>What a subject cut to fit its column ends with.</summary>
        private const char Ellipsis = '…';

        /// <summary>
        ///     A recipient's mailbox: the address without surrounding white space, and their name on
        ///     one line (each control character a space), or none when it is blank.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <param name="name">The recipient's name; null for none.</param>
        /// <returns>The mailbox, which the outbox may still refuse for its address.</returns>
        public static EmailAddress Mailbox(string address, string? name)
        {
            ArgumentNullException.ThrowIfNull(address);
            string? displayName = name is null ? null : OneLine(name);
            return new EmailAddress(address.Trim(), string.IsNullOrEmpty(displayName) ? null : displayName);
        }

        /// <summary>The tenant's name in each content language: the settings' names, else the catalog's.</summary>
        /// <param name="settings">The tenant's settings.</param>
        /// <param name="catalog">The tenant as the catalog describes it.</param>
        /// <returns>The name.</returns>
        public static LocalizedText TenantName(TenantSettings settings, TenantDescriptor catalog)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(catalog);
            IReadOnlyList<string?> names = settings.Names;
            return names.Count > 0 && !string.IsNullOrWhiteSpace(names[0])
                ? new LocalizedText(names[0]!, names.Count > 1 ? names[1] : null, names.Count > 2 ? names[2] : null)
                : new LocalizedText(catalog.Name, catalog.Name2, catalog.Name3);
        }

        /// <summary>A subject on one line that fits its column: control characters become spaces, and a longer one is cut and ends with an ellipsis.</summary>
        /// <param name="subject">The rendered subject.</param>
        /// <returns>The subject to store.</returns>
        public static string FitSubject(string subject)
        {
            ArgumentNullException.ThrowIfNull(subject);
            string line = OneLine(subject);
            if (line.Length <= OutgoingEmail.SubjectLength)
            {
                return line;
            }

            // Cut before a whole character, never between the halves of a surrogate pair.
            int cut = OutgoingEmail.SubjectLength - 1;
            if (char.IsLowSurrogate(line[cut]))
            {
                cut--;
            }

            return string.Concat(line.AsSpan(0, cut).TrimEnd(), Ellipsis.ToString());
        }

        /// <summary>A text on one line: each control character a space, and no surrounding white space.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The line.</returns>
        private static string OneLine(string text)
        {
            StringBuilder builder = new(text.Length);
            foreach (char c in text)
            {
                _ = builder.Append(char.IsControl(c) ? ' ' : c);
            }

            return builder.ToString().Trim();
        }
    }
}
