// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Localization;
using System.Globalization;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     Builds localized email messages from the resx templates, rendered in each recipient's
    ///     own locale (set at invitation) — a bulk send can carry a different culture per message.
    /// </summary>
    /// <param name="localizer">The template resources.</param>
    /// <param name="brandingResolver">Product naming for subjects/bodies.</param>
    public sealed class EmailTemplateService(
        IStringLocalizer<EmailTemplates> localizer,
        IBrandingResolver brandingResolver)
    {
        /// <summary>Builds the sign-in / verification code message.</summary>
        /// <param name="user">The recipient.</param>
        /// <param name="code">The one-time code.</param>
        /// <returns>The localized message.</returns>
        public EmailMessage SignInCode(TellmaIdentityUser user, string code)
        {
            ArgumentNullException.ThrowIfNull(user);

            string product = brandingResolver.Resolve(clientId: null).ProductName;
            return Render(user, "SignInCodeSubject", [product], "SignInCodeBody", [code]);
        }

        /// <summary>Builds the invitation message.</summary>
        /// <param name="user">The recipient.</param>
        /// <param name="link">The single-use invitation link.</param>
        /// <param name="expiryDays">How many days the link stays valid.</param>
        /// <returns>The localized message.</returns>
        public EmailMessage Invitation(TellmaIdentityUser user, string link, int expiryDays)
        {
            ArgumentNullException.ThrowIfNull(user);

            string product = brandingResolver.Resolve(clientId: null).ProductName;
            return Render(user, "InvitationSubject", [product], "InvitationBody", [product, link, expiryDays]);
        }

        /// <summary>Builds the password-reset message.</summary>
        /// <param name="user">The recipient.</param>
        /// <param name="link">The single-use reset link.</param>
        /// <returns>The localized message.</returns>
        public EmailMessage PasswordReset(TellmaIdentityUser user, string link)
        {
            ArgumentNullException.ThrowIfNull(user);

            string product = brandingResolver.Resolve(clientId: null).ProductName;
            return Render(user, "PasswordResetSubject", [product], "PasswordResetBody", [link]);
        }

        /// <summary>Renders one message in the recipient's locale.</summary>
        private EmailMessage Render(
            TellmaIdentityUser user, string subjectKey, object[] subjectArgs, string bodyKey, object[] bodyArgs)
        {
            CultureInfo previous = CultureInfo.CurrentUICulture;
            try
            {
                // IStringLocalizer resolves against the current UI culture; emails follow the
                // recipient's stored locale, not the current request's.
                CultureInfo.CurrentUICulture = ResolveCulture(user.Locale);

                string subject = localizer[subjectKey, subjectArgs];
                string body = localizer[bodyKey, bodyArgs];
                return new EmailMessage(user.Email!, user.DisplayName, subject, body);
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }

        /// <summary>Resolves a stored locale to a culture, falling back to English.</summary>
        private static CultureInfo ResolveCulture(string locale)
        {
            try
            {
                return CultureInfo.GetCultureInfo(locale);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.GetCultureInfo("en");
            }
        }
    }
}
