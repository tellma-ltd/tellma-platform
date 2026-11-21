// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Users;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     The users' normalisations and format checks: emails lower-cased, names trimmed, locale
    ///     members in their canonical case, one plain contact address, an E.164 mobile, and the name
    ///     an invitation carries in the locale's content language.
    /// </summary>
    public sealed class UserRulesTests : IDisposable
    {
        /// <summary>The service's collaborators, for the value sets.</summary>
        private readonly UsersKit _kit = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        [Fact]
        public void Preprocessing_lower_cases_the_emails_trims_the_names_and_canonicalises_the_locale_members()
        {
            User user = new()
            {
                Kind = UserKind.Individual,
                Email = "  Ada@Example.COM ",
                ContactEmail = "   ",
                Name = "  Ada  ",
                Name2 = " آدا ",
                Name3 = null,
                ContactMobile = " +971501234567 ",
                PreferredLanguage = " AR ",
                PreferredCalendar = "UQ",
                PreferredRegion = " ae ",
            };

            UserRules.Preprocess(user);

            Assert.Equal("ada@example.com", user.Email);
            Assert.Null(user.ContactEmail);
            Assert.Equal("Ada", user.Name);
            Assert.Equal("آدا", user.Name2);
            Assert.Null(user.Name3);
            Assert.Equal("+971501234567", user.ContactMobile);
            Assert.Equal("ar", user.PreferredLanguage);
            Assert.Equal("uq", user.PreferredCalendar);
            Assert.Equal("AE", user.PreferredRegion);
        }

        [Fact]
        public void Preprocessing_clears_blank_locale_members()
        {
            User user = new() { Name = "Ada", PreferredLanguage = " ", PreferredCalendar = "", PreferredRegion = "  " };

            UserRules.Preprocess(user);

            Assert.Null(user.PreferredLanguage);
            Assert.Null(user.PreferredCalendar);
            Assert.Null(user.PreferredRegion);
        }

        [Theory]
        [InlineData("ada@example.com", true)]
        [InlineData("ada.lovelace+tellma@mail.example.org", true)]
        [InlineData("Ada <ada@example.com>", false)]
        [InlineData("ada@example.com, bob@example.com", false)]
        [InlineData("ada@@example.com", false)]
        [InlineData("ada", false)]
        [InlineData("a@b@example.com", false)]
        public void A_contact_email_is_one_plain_address(string value, bool valid)
        {
            Assert.Equal(valid, UserRules.IsContactEmail(value));
        }

        [Theory]
        [InlineData("+971501234567", true)]
        [InlineData("+1234567", true)]
        [InlineData("+123456789012345", true)]
        [InlineData("+1234567890123456", false)]
        [InlineData("+123456", false)]
        [InlineData("+0501234567", false)]
        [InlineData("971501234567", false)]
        [InlineData("+971 50 123 4567", false)]
        [InlineData("+971501234567\n", false)]
        public void A_mobile_is_an_e164_number(string value, bool valid)
        {
            Assert.Equal(valid, UserRules.IsMobile(value));
        }

        [Fact]
        public void A_value_set_matches_ignoring_case_and_a_missing_set_is_an_error()
        {
            Assert.True(UserRules.InSet(_kit.ValueSets, CoreValueSets.Calendar, "GC"));
            Assert.True(UserRules.InSet(_kit.ValueSets, CoreValueSets.Calendar, "uq"));
            Assert.False(UserRules.InSet(_kit.ValueSets, CoreValueSets.Calendar, "et"));
            Assert.True(UserRules.InSet(_kit.ValueSets, CoreValueSets.TimeZone, "asia/dubai"));
            Assert.False(UserRules.InSet(_kit.ValueSets, CoreValueSets.TimeZone, "Mars/Olympus"));
            _ = Assert.Throws<InvalidOperationException>(() => UserRules.InSet(_kit.ValueSets, "core.Nothing", "x"));
        }

        [Fact]
        public void The_slot_is_the_tenant_language_sharing_the_locale_language_or_else_the_first()
        {
            IReadOnlyList<LanguageInfo> languages = UsersKit.Settings().Languages;

            Assert.Equal(0, UserRules.Slot(languages, "en"));
            Assert.Equal(1, UserRules.Slot(languages, "ar"));
            Assert.Equal(1, UserRules.Slot(languages, "AR-SA"));
            Assert.Equal(1, UserRules.Slot(languages, "ar_AE"));
            Assert.Equal(0, UserRules.Slot(languages, "fr"));
        }

        [Fact]
        public void The_name_in_a_slot_falls_back_to_the_first_name()
        {
            User user = new() { Name = "Ada", Name2 = "آدا", Name3 = null };

            Assert.Equal("Ada", UserRules.NameIn(user, 0));
            Assert.Equal("آدا", UserRules.NameIn(user, 1));
            Assert.Equal("Ada", UserRules.NameIn(user, 2));
        }
    }
}
