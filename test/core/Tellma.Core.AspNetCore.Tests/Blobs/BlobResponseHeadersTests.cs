// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.Extensions.Primitives;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.AspNetCore.Blobs;

namespace Tellma.Core.AspNetCore.Tests.Blobs
{
    /// <summary>
    ///     The headers of a blob download, computed from the resolved blob alone: the framing and
    ///     sniffing guards, private caching by state, inline for the types a browser shows and
    ///     attachment otherwise, the encoded file name, and the conditional match that answers 304.
    /// </summary>
    public sealed class BlobResponseHeadersTests
    {
        [Theory]
        [InlineData(BlobContentTypes.Png)]
        [InlineData(BlobContentTypes.Jpeg)]
        [InlineData(BlobContentTypes.Gif)]
        [InlineData(BlobContentTypes.WebP)]
        [InlineData(BlobContentTypes.Pdf)]
        [InlineData(BlobContentTypes.Text)]
        [InlineData(BlobContentTypes.Csv)]
        [InlineData(BlobContentTypes.Json)]
        public void A_type_the_browser_shows_opens_inline_unless_the_caller_asks_to_download(string contentType)
        {
            Assert.True(BlobResponseHeaders.IsInline(contentType, download: false));
            Assert.Equal("inline; filename*=UTF-8''a.bin", BlobResponseHeaders.ContentDisposition(contentType, "a.bin", download: false));
            Assert.Equal("attachment; filename*=UTF-8''a.bin", BlobResponseHeaders.ContentDisposition(contentType, "a.bin", download: true));
        }

        [Theory]
        [InlineData(BlobContentTypes.Docx)]
        [InlineData(BlobContentTypes.Xlsx)]
        [InlineData(BlobContentTypes.Pptx)]
        [InlineData(BlobContentTypes.Zip)]
        [InlineData("image/svg+xml")]
        [InlineData("text/html")]
        public void Any_other_type_downloads(string contentType)
        {
            Assert.False(BlobResponseHeaders.IsInline(contentType, download: false));
            Assert.Equal("attachment; filename*=UTF-8''report.docx", BlobResponseHeaders.ContentDisposition(contentType, "report.docx", download: false));
        }

        [Fact]
        public void A_blob_without_a_file_name_carries_the_disposition_alone()
        {
            Assert.Equal("inline", BlobResponseHeaders.ContentDisposition(BlobContentTypes.Png, fileName: null, download: false));
            Assert.Equal("attachment", BlobResponseHeaders.ContentDisposition(BlobContentTypes.Zip, fileName: null, download: false));
        }

        [Theory]
        [InlineData("plain-name_1.pdf", "plain-name_1.pdf")]
        [InlineData("two words.pdf", "two%20words.pdf")]
        [InlineData("quote\"semi;comma,.txt", "quote%22semi%3Bcomma%2C.txt")]
        [InlineData("تقرير.pdf", "%D8%AA%D9%82%D8%B1%D9%8A%D8%B1.pdf")]
        [InlineData("naïve 😀.png", "na%C3%AFve%20%F0%9F%98%80.png")]
        [InlineData("!#$&+-.^_`|~", "!#$&+-.^_`|~")]
        [InlineData("50%'*()", "50%25%27%2A%28%29")]
        public void The_file_name_is_percent_encoded_UTF_8_leaving_only_attribute_characters(string fileName, string encoded)
        {
            Assert.Equal(encoded, BlobResponseHeaders.EncodeFileName(fileName));
        }

        [Fact]
        public void A_committed_blob_is_cached_privately_for_a_year_and_a_staged_one_is_never_stored()
        {
            Assert.Equal("private, max-age=31536000, immutable", BlobResponseHeaders.CacheControl(BlobState.Committed));
            Assert.Equal("private, no-store", BlobResponseHeaders.CacheControl(BlobState.Staged));
        }

        [Fact]
        public void Every_response_is_framed_only_by_the_platform_and_never_sniffed()
        {
            IHeaderDictionary headers = new HeaderDictionary();

            BlobResponseHeaders.ApplyGuards(headers);

            Assert.Equal("frame-ancestors 'self'", headers.ContentSecurityPolicy);
            Assert.Equal("nosniff", headers.XContentTypeOptions);
        }

        [Fact]
        public void A_resolved_blob_s_answer_carries_its_tag_and_its_caching()
        {
            IHeaderDictionary headers = new HeaderDictionary();

            BlobResponseHeaders.ApplyCaching(headers, Download(BlobState.Staged, "\"7-thumb\""));

            Assert.Equal("\"7-thumb\"", headers.ETag);
            Assert.Equal("private, no-store", headers.CacheControl);
        }

        [Theory]
        [InlineData("\"7\"", true)]
        [InlineData("W/\"7\"", true)]
        [InlineData("\"3\", \"7\"", true)]
        [InlineData("*", true)]
        [InlineData("\"7-thumb\"", false)]
        [InlineData("\"70\"", false)]
        [InlineData("7", false)]
        [InlineData("", false)]
        public void A_conditional_request_naming_the_tag_already_holds_the_bytes(string ifNoneMatch, bool notModified)
        {
            Assert.Equal(notModified, BlobResponseHeaders.IsNotModified(new StringValues(ifNoneMatch), "\"7\""));
        }

        [Fact]
        public void A_request_without_a_condition_is_always_served()
        {
            Assert.False(BlobResponseHeaders.IsNotModified(StringValues.Empty, "\"7\""));
        }

        [Fact]
        public void The_binary_body_limit_is_the_endpoint_s_request_size_limit()
        {
            IRequestSizeLimitMetadata limit = new AcceptsBinaryMetadata(4096);

            Assert.Equal(4096, limit.MaxRequestBodySize);
        }

        /// <summary>A resolved image blob in a state, under a tag.</summary>
        /// <param name="state">The state.</param>
        /// <param name="etag">The tag.</param>
        /// <returns>The resolved blob.</returns>
        private static BlobDownload Download(BlobState state, string etag)
        {
            return new BlobDownload(7, "user-image", state, "user-image/ab/0123", BlobContentTypes.WebP, 10, "me.webp", IsImage: true, etag);
        }
    }
}
