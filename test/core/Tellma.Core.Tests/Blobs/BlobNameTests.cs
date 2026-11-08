// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>The blob name grammar: kinds, variants, keys, names and listing prefixes.</summary>
    public class BlobNameTests
    {
        private const string Key = "3fa9c0d1e2f34a5b6c7d8e9f0a1b2c3d";

        [Theory]
        [InlineData("ab")]
        [InlineData("user-image")]
        [InlineData("a1")]
        [InlineData("a-")]
        [InlineData("a--b")]
        [InlineData("export-file")]
        [InlineData("abcdefghijklmnopqrstuvwxyz0123456789-abc")]
        [InlineData("cons")]
        [InlineData("com10")]
        [InlineData("lpt")]
        [InlineData("nul1")]
        public void Valid_kinds_are_accepted(string kind)
        {
            Assert.True(BlobName.IsValidKind(kind));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("a")]
        [InlineData("1a")]
        [InlineData("-a")]
        [InlineData("User-image")]
        [InlineData("user_image")]
        [InlineData("user.image")]
        [InlineData("user/image")]
        [InlineData("user image")]
        [InlineData("abcdefghijklmnopqrstuvwxyz0123456789-abcd")]
        [InlineData("ümage")]
        [InlineData("image\0")]
        public void Kinds_outside_the_grammar_are_refused(string? kind)
        {
            Assert.False(BlobName.IsValidKind(kind));
        }

        [Theory]
        [InlineData("con")]
        [InlineData("prn")]
        [InlineData("aux")]
        [InlineData("nul")]
        [InlineData("com0")]
        [InlineData("com1")]
        [InlineData("com9")]
        [InlineData("lpt0")]
        [InlineData("lpt1")]
        [InlineData("lpt9")]
        public void Windows_device_names_are_refused_as_kinds(string kind)
        {
            Assert.False(BlobName.IsValidKind(kind));
            Assert.Throws<ArgumentException>(() => BlobName.Primary(kind, Key));
        }

        [Theory]
        [InlineData("thumb")]
        [InlineData("a")]
        [InlineData("0")]
        [InlineData("w256")]
        [InlineData("abcdefghijklmnop")]
        public void Valid_variants_are_accepted(string variant)
        {
            Assert.True(BlobName.IsValidVariant(variant));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Thumb")]
        [InlineData("thumb-1")]
        [InlineData("thumb.x")]
        [InlineData("thumb/x")]
        [InlineData("abcdefghijklmnopq")]
        [InlineData("..")]
        public void Variants_outside_the_grammar_are_refused(string? variant)
        {
            Assert.False(BlobName.IsValidVariant(variant));
        }

        [Theory]
        [InlineData(Key, true)]
        [InlineData("00000000000000000000000000000000", true)]
        [InlineData("3FA9C0D1E2F34A5B6C7D8E9F0A1B2C3D", false)]
        [InlineData("3fa9c0d1e2f34a5b6c7d8e9f0a1b2c3", false)]
        [InlineData("3fa9c0d1e2f34a5b6c7d8e9f0a1b2c3d0", false)]
        [InlineData("3fa9c0d1-e2f3-4a5b-6c7d-8e9f0a1b", false)]
        [InlineData("3fa9c0d1e2f34a5b6c7d8e9f0a1b2c3g", false)]
        [InlineData(null, false)]
        public void Keys_are_32_lowercase_hexadecimal_digits(string? key, bool valid)
        {
            Assert.Equal(valid, BlobName.IsValidKey(key));
        }

        [Fact]
        public void A_primary_name_fans_out_by_the_first_two_digits_of_its_key()
        {
            string name = BlobName.Primary("user-image", Key);

            Assert.Equal("user-image/3f/" + Key, name);
            Assert.True(BlobName.IsValid(name));
            Assert.True(BlobName.IsValidPrimary(name));
        }

        [Fact]
        public void A_variant_name_appends_the_variant_to_the_primary()
        {
            string primary = BlobName.Primary("user-image", Key);
            string variant = BlobName.Variant(primary, ThumbnailPolicy.VariantName);

            Assert.Equal(primary + ".thumb", variant);
            Assert.True(BlobName.IsValid(variant));
            Assert.False(BlobName.IsValidPrimary(variant));
        }

        [Fact]
        public void A_guid_formatted_as_n_is_a_valid_key()
        {
            for (int i = 0; i < 100; i++)
            {
                string key = Guid.NewGuid().ToString("N");
                Assert.True(BlobName.IsValidKey(key));
                Assert.True(BlobName.IsValid(BlobName.Primary("fixture-file", key)));
            }
        }

        [Fact]
        public void The_longest_name_fits_the_storage_key_column_and_the_declared_maximum()
        {
            string kind = new('k', BlobName.MaxKindLength);
            string variant = new('v', BlobName.MaxVariantLength);
            string name = BlobName.Variant(BlobName.Primary(kind, Key), variant);

            Assert.True(BlobName.IsValid(name));
            Assert.Equal(BlobName.MaxNameLength, name.Length);
            Assert.False(BlobName.IsValid(name + "v"));

            // The primary is what the blob row stores, in an nvarchar(120) column.
            Assert.True(BlobName.Primary(kind, Key).Length <= 120);
        }

        [Theory]
        [InlineData("user-image/3f/" + Key + ".thumb", true)]
        [InlineData("user-image/3f/" + Key, true)]
        [InlineData("user-image/3e/" + Key, false)]
        [InlineData("user-image/3F/" + Key, false)]
        [InlineData("user-image/3f" + Key, false)]
        [InlineData("user-image/3f/" + Key + "/", false)]
        [InlineData("user-image/3f/" + Key + ".", false)]
        [InlineData("user-image/3f/" + Key + ".thumb.thumb", false)]
        [InlineData("user-image/3f/" + Key + ".Thumb", false)]
        [InlineData("user-image//3f/" + Key, false)]
        [InlineData("/user-image/3f/" + Key, false)]
        [InlineData("user-image\\3f\\" + Key, false)]
        [InlineData("../user-image/3f/" + Key, false)]
        [InlineData("user-image/3f/../../../etc/passwd", false)]
        [InlineData("user-image/../3f/" + Key, false)]
        [InlineData("user-image/3f/..", false)]
        [InlineData("user-image/3f/.", false)]
        [InlineData("./user-image/3f/" + Key, false)]
        [InlineData("C:/user-image/3f/" + Key, false)]
        [InlineData("user-image/3f/" + Key + "\0", false)]
        [InlineData("user-image/3f/" + Key + " ", false)]
        [InlineData("user-image/3f/%2e%2e%2f", false)]
        [InlineData("con/3f/" + Key, false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Names_follow_the_grammar_and_refuse_traversal(string? name, bool valid)
        {
            Assert.Equal(valid, BlobName.IsValid(name));
        }

        [Theory]
        [InlineData("user-image", true)]
        [InlineData("user-image/3f", true)]
        [InlineData("user-image/00", true)]
        [InlineData("user-image/3F", false)]
        [InlineData("user-image/3", false)]
        [InlineData("user-image/3fa", false)]
        [InlineData("user-image/", false)]
        [InlineData("user-image/3f/", false)]
        [InlineData("user-image/3f/" + Key, false)]
        [InlineData("user-image/..", false)]
        [InlineData("..", false)]
        [InlineData("", false)]
        [InlineData("con", false)]
        [InlineData(null, false)]
        public void Listing_prefixes_are_a_kind_or_a_kind_and_a_fan_out_segment(string? prefix, bool valid)
        {
            Assert.Equal(valid, BlobName.IsValidPrefix(prefix));
        }

        [Theory]
        [InlineData("User-image", Key)]
        [InlineData("user-image", "3FA9C0D1E2F34A5B6C7D8E9F0A1B2C3D")]
        [InlineData("user-image", "../../../../../../../../../../../")]
        [InlineData("../t2", Key)]
        public void Primary_refuses_a_kind_or_key_outside_its_grammar(string kind, string key)
        {
            Assert.Throws<ArgumentException>(() => BlobName.Primary(kind, key));
        }

        [Fact]
        public void Variant_refuses_a_name_that_is_not_a_primary_or_a_bad_variant()
        {
            string primary = BlobName.Primary("user-image", Key);

            Assert.Throws<ArgumentException>(() => BlobName.Variant(primary + ".thumb", "thumb"));
            Assert.Throws<ArgumentException>(() => BlobName.Variant("user-image/../x", "thumb"));
            Assert.Throws<ArgumentException>(() => BlobName.Variant(primary, "Thumb"));
            Assert.Throws<ArgumentException>(() => BlobName.Variant(primary, "../x"));
            Assert.Throws<ArgumentNullException>(() => BlobName.Variant(primary, null!));
            Assert.Throws<ArgumentNullException>(() => BlobName.Primary(null!, Key));
        }
    }
}
