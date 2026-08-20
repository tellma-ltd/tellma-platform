// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Tests.Localization
{
    /// <summary>
    ///     The ICU formatting layer: gender selection, the neutral default that keeps an
    ///     ungendered call site readable, and the pass-through behaviour that lets the existing
    ///     positional catalog keep working.
    /// </summary>
    public sealed class IcuMessageFormatterTests
    {
        [Theory]
        [InlineData("female", "أدخلي الرمز")]
        [InlineData("male", "أدخل الرمز")]
        [InlineData("other", "أدخل الرمز")]
        public void Selects_the_grammatical_form_for_the_stated_gender(string gender, string expected)
        {
            const string Pattern = "{gender, select, female{أدخلي} other{أدخل}} الرمز";

            string rendered = IcuMessageFormatter.Format(
                Pattern, new Dictionary<string, object?> { [IcuMessageFormatter.GenderArgument] = gender });

            Assert.Equal(expected, rendered);
        }

        [Fact]
        public void Falls_back_to_the_neutral_form_when_no_gender_was_supplied()
        {
            // The pre-sign-in pages render without a gender on purpose — knowing how to address
            // someone would disclose that their account exists — so the absence must read
            // naturally rather than leaving ICU syntax on the page.
            const string Pattern = "{gender, select, female{أدخلي} other{أدخل}} الرمز";

            string rendered = IcuMessageFormatter.Format(Pattern, new Dictionary<string, object?>());

            Assert.Equal("أدخل الرمز", rendered);
        }

        [Fact]
        public void Leaves_a_string_without_placeholders_untouched()
        {
            const string Pattern = "Sign in";

            Assert.Equal(Pattern, IcuMessageFormatter.Format(Pattern, new Dictionary<string, object?>()));
        }

        [Fact]
        public void Still_substitutes_the_positional_arguments_the_existing_catalog_uses()
        {
            // The catalog predates ICU and uses {0}/{1}; those keys are the argument index.
            const string Pattern = "{0} wants access with: {1}";

            string rendered = IcuMessageFormatter.Format(
                Pattern, new Dictionary<string, object?> { ["0"] = "Acme", ["1"] = "openid profile" });

            Assert.Equal("Acme wants access with: openid profile", rendered);
        }

        [Fact]
        public void Shows_the_raw_value_rather_than_failing_when_a_pattern_is_malformed()
        {
            // A typo in a translation must not take the sign-in page down with it.
            const string Pattern = "{gender, select, female{أدخلي}";

            Assert.Equal(Pattern, IcuMessageFormatter.Format(Pattern, new Dictionary<string, object?>()));
        }

        [Fact]
        public void Formats_in_the_current_ui_culture()
        {
            CultureInfo previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
                string rendered = IcuMessageFormatter.Format(
                    "{gender, select, female{مرحباً بكِ} other{مرحباً بك}}",
                    new Dictionary<string, object?> { [IcuMessageFormatter.GenderArgument] = "female" });

                Assert.Equal("مرحباً بكِ", rendered);
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }
    }
}
