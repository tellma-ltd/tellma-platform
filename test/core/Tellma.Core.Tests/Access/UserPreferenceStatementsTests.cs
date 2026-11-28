// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using static Tellma.Core.Tests.Access.AccessRulesHarness;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The preference bag's checks in memory: the key grammar, a key named once ignoring case,
    ///     a value required and within its size in UTF-8.
    /// </summary>
    public class UserPreferenceStatementsTests
    {
        /// <summary>The path the items are checked under.</summary>
        private static readonly ValidationPath Items = ValidationPath.Root.Property("items");

        [Theory]
        [InlineData("theme")]
        [InlineData("x")]
        [InlineData("nav.pinned")]
        [InlineData("nav.Pinned")]
        [InlineData("gridState.acme-Widgets.columns")]
        [InlineData("grid.1.2")]
        [InlineData("a9.B-")]
        [InlineData("report.acme-credit-exposure.params")]
        [InlineData("report.2026-q3.params")]
        public void A_key_of_dotted_segments_the_first_led_by_a_lower_case_letter_is_valid(string key)
        {
            Assert.True(UserPreferenceStatements.IsValidKey(key));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Theme")]
        [InlineData("Report.exposure")]
        [InlineData("report.-exposure")]
        [InlineData("1theme")]
        [InlineData("nav-bar")]
        [InlineData("nav.")]
        [InlineData(".nav")]
        [InlineData("nav..pinned")]
        [InlineData("nav.-pinned")]
        [InlineData("nav pinned")]
        [InlineData("nav.pinned\n")]
        [InlineData("nav_pinned")]
        [InlineData("thème")]
        public void A_key_off_the_grammar_is_invalid(string? key)
        {
            Assert.False(UserPreferenceStatements.IsValidKey(key));
        }

        [Fact]
        public void A_key_is_at_most_the_column_s_width()
        {
            Assert.True(UserPreferenceStatements.IsValidKey(new string('a', UserPreferenceStatements.MaxKeyLength)));
            Assert.False(UserPreferenceStatements.IsValidKey(new string('a', UserPreferenceStatements.MaxKeyLength + 1)));
        }

        [Fact]
        public void Each_item_s_key_and_value_are_checked_where_they_stand()
        {
            ValidationErrors errors = new();

            // Four bytes of UTF-8 at most: "éé" is four, "ééa" five.
            UserPreferenceStatements.Check(
                [
                    Item("theme", "dark"),
                    Item("Theme", "dark"),
                    Item("nav.pinned", "éé"),
                    Item("nav.PINNED", "x"),
                    Item("grid.columns", null),
                    Item("grid.widths", "ééa"),
                ],
                4,
                errors,
                Items);

            Assert.Equal(
                [
                    ("items[1].Key", AccessCodes.PreferenceKeyInvalid),
                    ("items[3].Key", AccessCodes.PreferenceKeyDuplicate),
                    ("items[4].Value", ValidationCodes.Required),
                    ("items[5].Value", AccessCodes.PreferenceValueTooLarge),
                ],
                Errors(errors));
            Assert.Equal(UserPreferenceStatements.MaxKeyLength, errors.Items[0].Arguments["maxLength"]);
            Assert.Equal(4, errors.Items[3].Arguments["maxBytes"]);
        }

        [Fact]
        public void A_delete_s_keys_are_checked_where_they_stand()
        {
            ValidationErrors errors = new();

            UserPreferenceStatements.CheckKeys(["theme", "nav..x", "theME", "nav.x"], errors, ValidationPath.Root.Property("keys"));

            Assert.Equal([("keys[1]", AccessCodes.PreferenceKeyInvalid), ("keys[2]", AccessCodes.PreferenceKeyDuplicate)], Errors(errors));
        }

        /// <summary>An item.</summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <returns>The item.</returns>
        private static UserPreferenceList Item(string key, string? value)
        {
            return new UserPreferenceList { Key = key, Value = value! };
        }
    }
}
