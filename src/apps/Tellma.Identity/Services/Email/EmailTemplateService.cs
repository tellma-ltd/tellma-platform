// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System.Globalization;
using Tellma.Core.Abstractions.Email;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     Builds localized email messages from the resx templates, rendered in each recipient's
    ///     own locale (set at invitation) — a bulk send can carry a different culture per message.
    /// </summary>
    /// <remarks>
    ///     Every message goes out in both forms. The plain-text body is the one that always arrives:
    ///     it reads correctly in a client that refuses HTML, and mail with no text alternative is
    ///     treated as a spam signal. The HTML body is what most readers actually see, and carries
    ///     the brand mark inline so it renders without fetching anything.
    /// </remarks>
    /// <param name="localizer">The template resources.</param>
    /// <param name="brandingResolver">Product naming for subjects/bodies.</param>
    /// <param name="options">Deployment options, for the legal links in the email footer.</param>
    public sealed class EmailTemplateService(
        IStringLocalizer<EmailTemplates> localizer,
        IBrandingResolver brandingResolver,
        IOptions<TellmaIdentityOptions> options)
    {
        /// <summary>Renders one template in the recipient's locale and gender.</summary>
        /// <param name="key">The resource name.</param>
        /// <param name="args">The positional arguments the template interpolates.</param>
        /// <returns>The rendered string.</returns>
        private delegate string Localize(string key, params object[] args);

        /// <summary>Builds the sign-in / verification code message.</summary>
        /// <param name="user">The recipient.</param>
        /// <param name="code">The one-time code.</param>
        /// <returns>The localized message.</returns>
        public EmailMessage SignInCode(TellmaIdentityUser user, string code)
        {
            ArgumentNullException.ThrowIfNull(user);

            string product = brandingResolver.Resolve(clientId: null).ProductName;
            return Render(
                user,
                product,
                "SignInCodeSubject", [product],
                "SignInCodeBody", [code],
                localize => new EmailContent
                {
                    Preheader = localize("SignInCodePreheader"),
                    Heading = localize("SignInCodeHeading"),
                    Paragraphs = [localize("SignInCodeIntro", product, user.Email!)],
                    Code = code,
                    CodeNote = localize("SignInCodeExpiry"),
                    Notes = [localize("SignInCodeSecurity", product)],
                    FooterNote = localize("SignInCodeFooter", product, user.Email!),
                    FooterLinks = FooterLinks(localize),
                });
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
            return Render(
                user,
                product,
                "InvitationSubject", [product],
                "InvitationBody", [product, link, expiryDays],
                localize => new EmailContent
                {
                    Preheader = localize("InvitationPreheader", product),
                    Heading = localize("InvitationHeading", product),
                    Paragraphs = [localize("InvitationIntro", product)],
                    Action = new EmailAction(
                        localize("InvitationButton"), link, localize("InvitationFallback", expiryDays)),
                    Notes = [localize("InvitationSecurity")],
                    FooterNote = localize("InvitationFooter", product),
                    FooterLinks = FooterLinks(localize),
                });
        }

        /// <summary>Builds the password-reset message.</summary>
        /// <param name="user">The recipient.</param>
        /// <param name="link">The single-use reset link.</param>
        /// <returns>The localized message.</returns>
        public EmailMessage PasswordReset(TellmaIdentityUser user, string link)
        {
            ArgumentNullException.ThrowIfNull(user);

            string product = brandingResolver.Resolve(clientId: null).ProductName;
            return Render(
                user,
                product,
                "PasswordResetSubject", [product],
                "PasswordResetBody", [link],
                localize => new EmailContent
                {
                    Preheader = localize("PasswordResetPreheader", product),
                    Heading = localize("PasswordResetHeading"),
                    Paragraphs = [localize("PasswordResetIntro", product)],
                    Action = new EmailAction(
                        localize("PasswordResetButton"), link, localize("PasswordResetFallback")),
                    Notes = [localize("PasswordResetSecurity")],
                    FooterNote = localize("PasswordResetFooter", product),
                    FooterLinks = FooterLinks(localize),
                });
        }

        /// <summary>Renders one message in the recipient's locale.</summary>
        private EmailMessage Render(
            TellmaIdentityUser user,
            string product,
            string subjectKey,
            object[] subjectArgs,
            string bodyKey,
            object[] bodyArgs,
            Func<Localize, EmailContent> buildContent)
        {
            CultureInfo previous = CultureInfo.CurrentUICulture;
            try
            {
                // IStringLocalizer resolves against the current UI culture; emails follow the
                // recipient's stored locale, not the current request's.
                CultureInfo culture = ResolveCulture(user.Locale);
                CultureInfo.CurrentUICulture = culture;

                // Every template can select on the recipient's grammatical gender, whether or not
                // it currently does; "other" is the neutral branch an unstated gender lands in.
                Dictionary<string, object?> gender = new(StringComparer.Ordinal)
                {
                    [IcuMessageFormatter.GenderArgument] =
                        user.Gender?.ToString().ToLowerInvariant() ?? IcuMessageFormatter.NeutralGender,
                };

                string Localized(string key, params object[] args)
                {
                    return localizer[key, [.. args, gender]];
                }

                string subject = Localized(subjectKey, subjectArgs);
                string body = Localized(bodyKey, bodyArgs);
                string html = EmailHtmlLayout.Render(buildContent(Localized), subject, product, culture);

                // Internal: every message the identity server sends addresses a platform user about
                // their own account, never a recipient of a tenant's own correspondence. No From —
                // the active transport supplies the configured sender. No Correlation — nothing here
                // subscribes to delivery events, and a correlation nobody owns is only noise in the
                // pipeline's logs.
                return new EmailMessage
                {
                    To = [new EmailAddress(user.Email!, user.DisplayName)],
                    Subject = subject,
                    TextBody = body,
                    HtmlBody = html,
                    Attachments = [EmailWordmark.Attachment()],
                    Audience = EmailAudience.Internal,
                };
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }

        /// <summary>
        ///     The footer's legal links, in the order they are read. Each appears only when the
        ///     deployment has published that document, on the same terms as the sign-in page's own
        ///     links: no setting, no link, rather than a link that goes nowhere.
        /// </summary>
        private List<EmailLink> FooterLinks(Localize localize)
        {
            TellmaIdentityUiOptions ui = options.Value.Ui;
            List<EmailLink> links = new(2);

            if (ui.PrivacyPolicyUrl is not null)
            {
                links.Add(new EmailLink(localize("FooterPrivacy"), ui.PrivacyPolicyUrl));
            }

            if (ui.TermsOfServiceUrl is not null)
            {
                links.Add(new EmailLink(localize("FooterTerms"), ui.TermsOfServiceUrl));
            }

            return links;
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
