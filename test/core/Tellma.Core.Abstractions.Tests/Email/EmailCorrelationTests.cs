// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Abstractions.Tests.Email
{
    /// <summary>
    ///     The correlation's canonical form is what survives a round trip through a third-party
    ///     system, so its parsing is pinned in both directions.
    /// </summary>
    public class EmailCorrelationTests
    {
        [Theory]
        [InlineData("outbox", "12345", 7)]
        [InlineData("identity", "abc", null)]
        [InlineData("a", "x", 0)]
        [InlineData("outbox", "12345", -3)]
        public void Round_trips_through_the_canonical_form(string ownerKey, string reference, int? tenantId)
        {
            EmailCorrelation original = new(ownerKey, reference, tenantId);

            Assert.True(EmailCorrelation.TryParse(original.ToString(), out EmailCorrelation? parsed));
            Assert.Equal(original, parsed);
        }

        [Fact]
        public void Keeps_colons_inside_the_reference()
        {
            // Only the first two colons delimit; everything after belongs to the reference.
            EmailCorrelation original = new("outbox", "invoice:2026:00042", 3);

            Assert.Equal("outbox:3:invoice:2026:00042", original.ToString());
            Assert.True(EmailCorrelation.TryParse(original.ToString(), out EmailCorrelation? parsed));
            Assert.Equal("invoice:2026:00042", parsed.Reference);
            Assert.Equal(3, parsed.TenantId);
        }

        [Fact]
        public void Renders_a_null_tenant_as_an_empty_middle_segment()
        {
            Assert.Equal("identity::.:token", new EmailCorrelation("identity", ".:token").ToString());
        }

        [Fact]
        public void Round_trips_a_unicode_reference()
        {
            EmailCorrelation original = new("outbox", "فاتورة-٤٢", 9);

            Assert.True(EmailCorrelation.TryParse(original.ToString(), out EmailCorrelation? parsed));
            Assert.Equal(original.Reference, parsed.Reference);
        }

        [Fact]
        public void Round_trips_through_the_wire_envelope()
        {
            // The deployment id is colon-free by construction, so the envelope splits on the first
            // colon and the rest parses as the correlation.
            EmailCorrelation original = new("outbox", "42:b", 1);
            string envelope = $"etpharma-staging:{original}";

            int separator = envelope.IndexOf(':', StringComparison.Ordinal);
            Assert.Equal("etpharma-staging", envelope[..separator]);
            Assert.True(EmailCorrelation.TryParse(envelope[(separator + 1)..], out EmailCorrelation? parsed));
            Assert.Equal(original, parsed);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("outbox")]
        [InlineData("outbox:1")]
        [InlineData("OUTBOX:1:42")]
        [InlineData("out_box:1:42")]
        [InlineData("outbox:x:42")]
        [InlineData("outbox: 1 :42")]
        [InlineData("outbox:1.5:42")]
        [InlineData("outbox:1:")]
        [InlineData("outbox:1:   ")]
        [InlineData(":1:42")]
        public void Rejects_a_malformed_canonical_form(string? value)
        {
            Assert.False(EmailCorrelation.TryParse(value, out EmailCorrelation? parsed));
            Assert.Null(parsed);
        }

        [Fact]
        public void Rejects_an_over_long_owner_key_or_reference()
        {
            Assert.False(EmailCorrelation.TryParse(
                new string('a', EmailCorrelation.MaxOwnerKeyLength + 1) + ":1:x", out _));
            Assert.False(EmailCorrelation.TryParse(
                "outbox:1:" + new string('x', EmailCorrelation.MaxReferenceLength + 1), out _));
        }

        [Theory]
        [InlineData("Outbox", "42")]
        [InlineData("out box", "42")]
        [InlineData("out:box", "42")]
        [InlineData("", "42")]
        [InlineData("outbox", "")]
        [InlineData("outbox", "   ")]
        public void Refuses_to_construct_an_invalid_correlation(string ownerKey, string reference)
        {
            Assert.Throws<ArgumentException>(() => new EmailCorrelation(ownerKey, reference));
        }

        [Fact]
        public void Accepts_the_longest_permitted_values()
        {
            EmailCorrelation correlation = new(
                new string('a', EmailCorrelation.MaxOwnerKeyLength),
                new string('x', EmailCorrelation.MaxReferenceLength));

            Assert.True(EmailCorrelation.TryParse(correlation.ToString(), out EmailCorrelation? parsed));
            Assert.Equal(correlation, parsed);
        }
    }
}
