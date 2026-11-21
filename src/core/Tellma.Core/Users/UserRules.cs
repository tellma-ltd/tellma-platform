// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net.Mail;
using System.Text.RegularExpressions;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Users
{
    // The normalisations and the checks are spec 0018 §3.2's PreprocessAsync and ValidateAsync rows.
    // They are kept apart from the service so that the self-service locale write, which validates
    // the same members outside a save, can reuse them.
    /// <summary>The users' field rules: how a saved user's members are normalised, and which values they accept.</summary>
    internal static partial class UserRules
    {
        /// <summary>
        ///     Normalises a user's members in place: the emails trimmed and lower-cased, the names and
        ///     the mobile trimmed, the language trimmed and lower-cased, the calendar lower-cased and
        ///     the region trimmed and upper-cased. An email or a locale member left empty becomes null.
        /// </summary>
        /// <param name="user">The user.</param>
        public static void Preprocess(User user)
        {
            ArgumentNullException.ThrowIfNull(user);

            // --- The emails: the identity server compares them ignoring case ---
            user.Email = Email(user.Email);
            user.ContactEmail = Email(user.ContactEmail);

            // --- The names and the mobile: trimmed only; the required name stays as the caller sent it when empty ---
            user.Name = user.Name?.Trim()!;
            user.Name2 = user.Name2?.Trim();
            user.Name3 = user.Name3?.Trim();
            user.ContactMobile = user.ContactMobile?.Trim();

            // --- The locale members: a bare language code, a calendar code and a region code ---
            user.PreferredLanguage = Language(user.PreferredLanguage);
            user.PreferredCalendar = Calendar(user.PreferredCalendar);
            user.PreferredRegion = Region(user.PreferredRegion);
        }

        /// <summary>An email address trimmed and lower-cased, local part and domain alike; null when empty.</summary>
        /// <param name="value">The address.</param>
        /// <returns>The normalised address, or null.</returns>
        public static string? Email(string? value)
        {
            string? trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
        }

        /// <summary>A language code trimmed and lower-cased; null when empty.</summary>
        /// <param name="value">The code.</param>
        /// <returns>The normalised code, or null.</returns>
        public static string? Language(string? value)
        {
            string? trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
        }

        /// <summary>A calendar code lower-cased; null when empty.</summary>
        /// <param name="value">The code.</param>
        /// <returns>The normalised code, or null.</returns>
        public static string? Calendar(string? value)
        {
            return string.IsNullOrEmpty(value) ? null : value.ToLowerInvariant();
        }

        /// <summary>A region code trimmed and upper-cased; null when empty.</summary>
        /// <param name="value">The code.</param>
        /// <returns>The normalised code, or null.</returns>
        public static string? Region(string? value)
        {
            string? trimmed = value?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToUpperInvariant();
        }

        /// <summary>Whether a value lies in a value set, ignoring case; an alias is never a value.</summary>
        /// <param name="valueSets">The value sets.</param>
        /// <param name="set">The set's name.</param>
        /// <param name="value">The value.</param>
        /// <returns>True when one of the set's values matches.</returns>
        /// <exception cref="InvalidOperationException">No set has the name.</exception>
        public static bool InSet(IValueSetRegistry valueSets, string set, string value)
        {
            ArgumentNullException.ThrowIfNull(valueSets);
            ValueSetDescriptor descriptor = valueSets.Find(set)
                ?? throw new InvalidOperationException($"No value set is named '{set}', though the users' rules check against it.");
            foreach (ValueSetEntry entry in descriptor.Values)
            {
                if (string.Equals(entry.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Whether a contact email is a single plain address: it parses as one, has exactly one
        ///     <c>@</c>, and carries no display name or angle brackets.
        /// </summary>
        /// <param name="value">The address.</param>
        /// <returns>True when it is valid.</returns>
        public static bool IsContactEmail(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return value.Count(static character => character == '@') == 1
                && MailAddress.TryCreate(value, out MailAddress? address)
                && string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether a mobile number is in E.164 form: a plus, a non-zero digit, then six to fourteen digits.</summary>
        /// <param name="value">The number.</param>
        /// <returns>True when it is valid.</returns>
        public static bool IsMobile(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            return MobilePattern().IsMatch(value);
        }

        /// <summary>The E.164 pattern; <c>\z</c> so a trailing line break never passes.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"^\+[1-9][0-9]{6,14}\z", RegexOptions.CultureInvariant)]
        private static partial Regex MobilePattern();

        /// <summary>The content-language slot of a locale among the tenant's languages: the position of the language whose subtag equals the locale's; 0 when none does.</summary>
        /// <param name="languages">The tenant's languages, in slot order.</param>
        /// <param name="locale">The locale, such as <c>ar</c> or <c>ar-SA</c>.</param>
        /// <returns>The slot, 0 to 2.</returns>
        public static int Slot(IReadOnlyList<LanguageInfo> languages, string locale)
        {
            ArgumentNullException.ThrowIfNull(languages);
            ArgumentNullException.ThrowIfNull(locale);
            string subtag = Subtag(locale);
            for (int index = 0; index < languages.Count && index < 3; index++)
            {
                if (string.Equals(Subtag(languages[index].Code), subtag, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return 0;
        }

        /// <summary>A user's name in a content-language slot, falling back to the first slot's name.</summary>
        /// <param name="user">The user.</param>
        /// <param name="slot">The slot, 0 to 2.</param>
        /// <returns>The name.</returns>
        public static string NameIn(User user, int slot)
        {
            ArgumentNullException.ThrowIfNull(user);
            return slot switch
            {
                1 => user.Name2 ?? user.Name,
                2 => user.Name3 ?? user.Name,
                _ => user.Name,
            };
        }

        /// <summary>The language subtag of a code: <c>ar</c> of <c>ar-SA</c>.</summary>
        /// <param name="code">The code.</param>
        /// <returns>The subtag.</returns>
        private static string Subtag(string code)
        {
            int dash = code.IndexOfAny(['-', '_']);
            return dash < 0 ? code : code[..dash];
        }
    }
}
