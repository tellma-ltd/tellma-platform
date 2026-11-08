// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>The kind presets, the blob-reference declaration's policy, and the image contract types.</summary>
    public class BlobKindPolicyTests
    {
        private const long Mebibyte = 1024L * 1024;

        [Fact]
        public void The_attachment_preset_takes_documents_images_text_and_zip_with_a_thumbnail()
        {
            BlobKindPolicy policy = BlobKindPolicy.Attachment;

            Assert.Equal(100 * Mebibyte, policy.MaxSize);
            Assert.Equal(
                [
                    "application/pdf",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                    "image/jpeg",
                    "image/png",
                    "image/webp",
                    "image/gif",
                    "text/plain",
                    "text/csv",
                    "application/json",
                    "application/zip",
                ],
                policy.AllowedContentTypes);
            Assert.Null(policy.Image);
            Assert.Equal(new ThumbnailPolicy(256, ImageFormat.WebP), policy.Thumbnail);
            Assert.Null(policy.StagingTtl);
            Assert.Equal(BlobReadAccess.OwnerRead, policy.ReadAccess);
        }

        [Fact]
        public void The_avatar_preset_crops_a_square_webp_with_a_small_thumbnail()
        {
            BlobKindPolicy policy = BlobKindPolicy.Avatar;

            Assert.Equal(10 * Mebibyte, policy.MaxSize);
            Assert.Equal(["image/jpeg", "image/png", "image/webp", "image/gif"], policy.AllowedContentTypes);
            Assert.Equal(new ImagePolicy(512, ImageFit.CoverSquare, ImageFormat.WebP, 50_000_000), policy.Image);
            Assert.Equal(new ThumbnailPolicy(96, ImageFormat.WebP), policy.Thumbnail);
        }

        [Fact]
        public void The_photo_preset_fits_a_webp_within_1600_pixels()
        {
            BlobKindPolicy policy = BlobKindPolicy.Photo;

            Assert.Equal(10 * Mebibyte, policy.MaxSize);
            Assert.Equal(["image/jpeg", "image/png", "image/webp", "image/gif"], policy.AllowedContentTypes);
            Assert.Equal(new ImagePolicy(1600, ImageFit.Contain, ImageFormat.WebP), policy.Image);
            Assert.Equal(new ThumbnailPolicy(256, ImageFormat.WebP), policy.Thumbnail);
        }

        [Fact]
        public void The_signature_preset_fits_a_png_within_400_pixels_without_a_thumbnail()
        {
            BlobKindPolicy policy = BlobKindPolicy.Signature;

            Assert.Equal(2 * Mebibyte, policy.MaxSize);
            Assert.Equal(["image/jpeg", "image/png", "image/webp", "image/gif"], policy.AllowedContentTypes);
            Assert.Equal(new ImagePolicy(400, ImageFit.Contain, ImageFormat.Png), policy.Image);
            Assert.Null(policy.Thumbnail);
        }

        [Theory]
        [InlineData(BlobPreset.Attachment)]
        [InlineData(BlobPreset.Avatar)]
        [InlineData(BlobPreset.Photo)]
        [InlineData(BlobPreset.Signature)]
        public void Each_preset_resolves_to_its_policy(BlobPreset preset)
        {
            BlobKindPolicy expected = preset switch
            {
                BlobPreset.Attachment => BlobKindPolicy.Attachment,
                BlobPreset.Avatar => BlobKindPolicy.Avatar,
                BlobPreset.Photo => BlobKindPolicy.Photo,
                BlobPreset.Signature => BlobKindPolicy.Signature,
                _ => throw new ArgumentOutOfRangeException(nameof(preset)),
            };

            Assert.Same(expected, BlobKindPolicy.For(preset));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlobKindPolicy.For((BlobPreset)99));
        }

        [Fact]
        public void A_transform_produces_a_new_policy_and_leaves_the_preset_untouched()
        {
            BlobKindPolicy narrowed = BlobKindPolicy.Avatar with { MaxSize = 4 * Mebibyte, Thumbnail = null };

            Assert.Equal(4 * Mebibyte, narrowed.MaxSize);
            Assert.Null(narrowed.Thumbnail);
            Assert.Equal(10 * Mebibyte, BlobKindPolicy.Avatar.MaxSize);
            Assert.NotNull(BlobKindPolicy.Avatar.Thumbnail);
        }

        [Fact]
        public void The_presets_allowed_types_cannot_be_mutated_through_a_cast()
        {
            foreach (BlobKindPolicy policy in new[] { BlobKindPolicy.Attachment, BlobKindPolicy.Avatar, BlobKindPolicy.Photo, BlobKindPolicy.Signature })
            {
                IList<string> list = Assert.IsAssignableFrom<IList<string>>(policy.AllowedContentTypes);
                Assert.True(list.IsReadOnly);
                Assert.Throws<NotSupportedException>(() => list[0] = "text/html");
            }
        }

        [Fact]
        public void A_declaration_starts_from_its_preset()
        {
            BlobReferenceAttribute declaration = new("product-image", BlobPreset.Photo);

            BlobKindPolicy policy = declaration.CreatePolicy();

            Assert.Equal(BlobKindPolicy.Photo, policy);
            Assert.Equal("product-image", declaration.Kind);
            Assert.Equal(BlobPreset.Photo, declaration.Preset);
        }

        [Fact]
        public void A_declaration_defaults_to_the_attachment_preset_and_owner_read()
        {
            BlobReferenceAttribute declaration = new("invoice-attachment");

            BlobKindPolicy policy = declaration.CreatePolicy();

            Assert.Equal(BlobPreset.Attachment, declaration.Preset);
            Assert.Equal(BlobKindPolicy.Attachment.MaxSize, policy.MaxSize);
            Assert.Equal(BlobReadAccess.OwnerRead, policy.ReadAccess);
        }

        [Fact]
        public void A_declared_size_narrows_the_presets_and_read_access_is_applied()
        {
            BlobReferenceAttribute declaration = new("user-signature", BlobPreset.Signature)
            {
                MaxSizeBytes = Mebibyte,
                ReadAccess = BlobReadAccess.AnyMember,
            };

            BlobKindPolicy policy = declaration.CreatePolicy();

            Assert.Equal(Mebibyte, policy.MaxSize);
            Assert.Equal(BlobReadAccess.AnyMember, policy.ReadAccess);
            Assert.Equal(BlobKindPolicy.Signature.Image, policy.Image);
            Assert.Equal(2 * Mebibyte, BlobKindPolicy.Signature.MaxSize);
        }

        [Fact]
        public void A_declared_size_equal_to_the_presets_is_kept()
        {
            BlobReferenceAttribute declaration = new("fixture-photo", BlobPreset.Photo) { MaxSizeBytes = 10 * Mebibyte };

            Assert.Equal(10 * Mebibyte, declaration.CreatePolicy().MaxSize);
        }

        [Fact]
        public void A_declared_size_above_the_presets_is_refused()
        {
            BlobReferenceAttribute declaration = new("user-signature", BlobPreset.Signature) { MaxSizeBytes = 100 * Mebibyte };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(declaration.CreatePolicy);
            Assert.Contains("user-signature", exception.Message, StringComparison.Ordinal);
            Assert.Contains("104857600", exception.Message, StringComparison.Ordinal);
            Assert.Contains("2097152", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_negative_declared_size_is_refused()
        {
            BlobReferenceAttribute declaration = new("fixture-file") { MaxSizeBytes = -1 };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(declaration.CreatePolicy);
            Assert.Contains("fixture-file", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_declaration_sits_on_one_property_and_is_inherited()
        {
            AttributeUsageAttribute usage = typeof(BlobReferenceAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

            Assert.Equal(AttributeTargets.Property, usage.ValidOn);
            Assert.False(usage.AllowMultiple);
            Assert.True(usage.Inherited);

            BlobReferenceAttribute declared = typeof(Owner).GetProperty(nameof(Owner.ImageId))!.GetCustomAttribute<BlobReferenceAttribute>()!;
            Assert.Equal("owner-image", declared.Kind);
            Assert.Equal(BlobPreset.Avatar, declared.Preset);
            Assert.Equal(BlobReadAccess.AnyMember, declared.ReadAccess);
        }

        [Theory]
        [InlineData(ImageFormat.WebP, "image/webp")]
        [InlineData(ImageFormat.Jpeg, "image/jpeg")]
        [InlineData(ImageFormat.Png, "image/png")]
        public void Each_image_format_has_its_media_type(ImageFormat format, string contentType)
        {
            Assert.Equal(contentType, BlobContentTypes.ForImageFormat(format));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlobContentTypes.ForImageFormat((ImageFormat)99));
        }

        [Fact]
        public void An_image_rejection_carries_its_reason_and_a_copy_of_its_arguments()
        {
            Dictionary<string, object?> arguments = new() { [ImageRejectedException.ReasonArgument] = ImageRejectedException.TooManyPixelsReason, ["pixels"] = 60_000_000 };

            ImageRejectedException exception = new("Image.TooManyPixels", arguments);
            arguments["pixels"] = 1;

            Assert.Equal("Image.TooManyPixels", exception.Code);
            Assert.Equal("too-many-pixels", exception.Reason);
            Assert.Equal(60_000_000, exception.Arguments["pixels"]);
            Assert.Contains("too-many-pixels", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_image_rejection_without_a_reason_is_refused()
        {
            Assert.Throws<ArgumentException>(() => new ImageRejectedException("Image.Bad", new Dictionary<string, object?>()));
            Assert.Throws<ArgumentException>(() => new ImageRejectedException("Image.Bad", new Dictionary<string, object?> { ["reason"] = " " }));
            Assert.Throws<ArgumentException>(() => new ImageRejectedException("Image.Bad", new Dictionary<string, object?> { ["reason"] = 3 }));
            Assert.Throws<ArgumentException>(() => new ImageRejectedException(" ", new Dictionary<string, object?> { ["reason"] = "format" }));
            Assert.Throws<ArgumentNullException>(() => new ImageRejectedException("Image.Bad", null!));
        }

        [Fact]
        public void The_store_exceptions_name_what_failed()
        {
            BlobAlreadyExistsException conflict = new("user-image/3f/3fa9c0d1e2f34a5b6c7d8e9f0a1b2c3d");
            IOException inner = new("disk full");
            BlobStoreException failure = new("Writing failed.", inner);

            Assert.Equal("user-image/3f/3fa9c0d1e2f34a5b6c7d8e9f0a1b2c3d", conflict.Name);
            Assert.Contains(conflict.Name, conflict.Message, StringComparison.Ordinal);
            Assert.Same(inner, failure.InnerException);
            Assert.Equal("Writing failed.", failure.Message);
        }

        /// <summary>An owner entity declaring a blob reference, for reading the declaration back.</summary>
        private sealed class Owner
        {
            [BlobReference("owner-image", BlobPreset.Avatar, ReadAccess = BlobReadAccess.AnyMember)]
            public int? ImageId { get; set; }
        }
    }
}
