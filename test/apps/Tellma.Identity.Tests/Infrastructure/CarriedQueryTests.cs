// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Tellma.Identity.Infrastructure;

namespace Tellma.Identity.Tests.Infrastructure
{
    /// <summary>
    ///     The three query values the identity pages pass along: how they are read off a URL
    ///     anyone can write, and how they are written onto the next one. Reading decides what is
    ///     trusted enough to repeat; writing must never change a URL beyond adding to it.
    /// </summary>
    public sealed class CarriedQueryTests
    {
        [Fact]
        public void The_three_values_are_read_from_the_query()
        {
            CarriedQuery carried = Read("?client_id=acme-erp&return_url=https%3A%2F%2Facme.test%2Forders%3Fa%3D1&ui_locales=ar&section=passkeys");

            Assert.Equal("acme-erp", carried.ClientId);
            Assert.Equal("https://acme.test/orders?a=1", carried.ReturnUrl);
            Assert.Equal("ar", carried.UiLocales);
            Assert.False(carried.IsEmpty);
        }

        [Theory]
        [InlineData("")]
        [InlineData("?section=passkeys&returnUrl=%2Fconnect%2Fauthorize")]

        // Blank values say nothing.
        [InlineData("?client_id=&return_url=%20&ui_locales=")]
        public void A_query_without_them_carries_nothing(string query)
        {
            CarriedQuery carried = Read(query);

            Assert.True(carried.IsEmpty);
            Assert.Same(CarriedQuery.None, carried);
        }

        [Fact]
        public void A_value_given_twice_is_not_read()
        {
            // Two values under one name is a caller hoping two readers disagree about which one
            // counts. Neither does, and the others are unaffected.
            CarriedQuery carried = Read("?client_id=a&client_id=b&return_url=https%3A%2F%2Fa.test%2F&ui_locales=ar&ui_locales=en");

            Assert.Null(carried.ClientId);
            Assert.Equal("https://a.test/", carried.ReturnUrl);
            Assert.Null(carried.UiLocales);
        }

        [Fact]
        public void A_value_over_its_length_limit_is_not_read()
        {
            string longClient = new('c', CarriedQuery.MaxClientIdLength + 1);
            string longReturn = "https://a.test/" + new string('p', CarriedQuery.MaxReturnUrlLength);
            string longLocales = string.Join(' ', Enumerable.Repeat("ar", CarriedQuery.MaxUiLocalesLength));

            CarriedQuery carried = Read(QueryHelpers.AddQueryString(string.Empty, new Dictionary<string, string?>
            {
                ["client_id"] = longClient,
                ["return_url"] = longReturn,
                ["ui_locales"] = longLocales,
            }));

            Assert.True(carried.IsEmpty);
        }

        [Fact]
        public void A_value_at_its_length_limit_is_read()
        {
            string client = new('c', CarriedQuery.MaxClientIdLength);
            string returnUrl = "https://a.test/" + new string('p', CarriedQuery.MaxReturnUrlLength - 15);

            CarriedQuery carried = Read(QueryHelpers.AddQueryString(string.Empty, new Dictionary<string, string?>
            {
                ["client_id"] = client,
                ["return_url"] = returnUrl,
            }));

            Assert.Equal(client, carried.ClientId);
            Assert.Equal(returnUrl, carried.ReturnUrl);
        }

        [Theory]
        [InlineData("ar", "ar")]
        [InlineData("ar-SA en", "ar-SA en")]
        [InlineData("  ar   en-GB  ", "ar en-GB")]
        [InlineData("zh-Hant-TW", "zh-Hant-TW")]

        // A malformed entry is dropped on its own; the entries around it still count.
        [InlineData("<script> ar", "ar")]
        [InlineData("ar \"onmouseover=x en", "ar en")]
        [InlineData("-ar ar- a--r fr", "fr")]
        [InlineData("ar,en", null)]
        [InlineData("ar\ten", null)]
        [InlineData("العربية", null)]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa en", "en")]
        [InlineData("   ", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void A_language_list_is_reduced_to_its_well_formed_tags(string? value, string? expected)
        {
            Assert.Equal(expected, CarriedQuery.NormalizeUiLocales(value));
        }

        [Fact]
        public void The_language_list_is_normalized_as_it_is_read()
        {
            Assert.Equal("ar en", Read("?ui_locales=ar%20%20%3Cb%3E%20en").UiLocales);
        }

        [Fact]
        public void Missing_values_are_appended_in_a_fixed_order()
        {
            CarriedQuery carried = new("acme-erp", "https://acme.test/orders?a=1&b=2", "ar en");

            Assert.Equal(
                "/Identity/Manage/Passkeys?client_id=acme-erp&return_url=https%3A%2F%2Facme.test%2Forders%3Fa%3D1%26b%3D2&ui_locales=ar%20en",
                carried.AppendTo("/Identity/Manage/Passkeys"));
        }

        [Fact]
        public void Only_the_values_that_are_held_are_appended()
        {
            Assert.Equal("/Identity/Account/EmailCode?ui_locales=ar", new CarriedQuery(null, null, "ar").AppendTo("/Identity/Account/EmailCode"));
            Assert.Equal("/Identity/Manage?client_id=acme-erp", new CarriedQuery("acme-erp", null, null).AppendTo("/Identity/Manage"));
        }

        [Fact]
        public void A_url_keeps_the_values_it_already_states()
        {
            CarriedQuery carried = new("acme-erp", "https://acme.test/", "ar");

            // The link's own language wins, and so would its own client or return address; only
            // what it does not name is added. Names match without case, as the reader's do.
            Assert.Equal(
                "/Identity/Manage?ui_locales=en&client_id=acme-erp&return_url=https%3A%2F%2Facme.test%2F",
                carried.AppendTo("/Identity/Manage?ui_locales=en"));
            Assert.Equal(
                "/Identity/Manage?CLIENT_ID=other&Return_Url=x&UI_LOCALES=en",
                carried.AppendTo("/Identity/Manage?CLIENT_ID=other&Return_Url=x&UI_LOCALES=en"));
        }

        [Fact]
        public void The_existing_query_and_fragment_are_kept_as_written()
        {
            CarriedQuery carried = new(null, null, "ar");

            Assert.Equal(
                "/Identity/Account/Login?returnUrl=%2Fconnect%2Fauthorize%3Fui_locales%3Den&methods=passkey%20email_code&ui_locales=ar#main",
                carried.AppendTo("/Identity/Account/Login?returnUrl=%2Fconnect%2Fauthorize%3Fui_locales%3Den&methods=passkey%20email_code#main"));
        }

        [Fact]
        public void Nothing_carried_leaves_a_url_untouched()
        {
            const string Url = "/Identity/Manage?handler=Remove";

            Assert.Same(Url, CarriedQuery.None.AppendTo(Url));
        }

        [Fact]
        public void A_stated_language_is_replaced()
        {
            Assert.Equal(
                "/Identity/Manage?client_id=acme-erp&ui_locales=en&return_url=https%3A%2F%2Facme.test%2F",
                CarriedQuery.WithUiLocales("/Identity/Manage?client_id=acme-erp&ui_locales=ar%20fr&return_url=https%3A%2F%2Facme.test%2F", "en"));
        }

        [Fact]
        public void A_repeated_language_collapses_into_the_replacement()
        {
            Assert.Equal(
                "/Identity/Account/Login?ui_locales=en&methods=passkey",
                CarriedQuery.WithUiLocales("/Identity/Account/Login?ui_locales=ar&methods=passkey&UI_LOCALES=fr", "en"));
        }

        [Theory]
        [InlineData("/Identity/Account/Login")]
        [InlineData("/Identity/Account/Login?returnUrl=%2Fconnect%2Fauthorize%3Fui_locales%3Dar")]
        [InlineData("/connect/authorize?client_id=acme-erp&request_uri=urn%3Aietf%3Aparams%3Aoauth%3Arequest_uri%3Aabc#frag")]
        public void A_url_that_states_no_language_is_left_alone(string url)
        {
            // In particular a language nested inside a return address is not this URL's own.
            Assert.Same(url, CarriedQuery.WithUiLocales(url, "en"));
        }

        [Fact]
        public void Replacing_the_language_keeps_every_other_pair_exactly()
        {
            // Encodings, a bare name, an empty value, a repeated name and the fragment all
            // survive: the replacement touches one pair and nothing else.
            Assert.Equal(
                "/connect/authorize?scope=openid+profile&flag&empty=&ui_locales=en&redirect_uri=https%3a%2f%2fa.test%2Fcb&x=1&x=2#state",
                CarriedQuery.WithUiLocales(
                    "/connect/authorize?scope=openid+profile&flag&empty=&ui_locales=ar&redirect_uri=https%3a%2f%2fa.test%2Fcb&x=1&x=2#state", "en"));
        }

        /// <summary>Reads the carried values of a query string.</summary>
        /// <param name="query">The query string, with its leading question mark.</param>
        /// <returns>The carried values.</returns>
        private static CarriedQuery Read(string query)
        {
            return CarriedQuery.Read(new QueryCollection(QueryHelpers.ParseQuery(query)));
        }
    }
}
