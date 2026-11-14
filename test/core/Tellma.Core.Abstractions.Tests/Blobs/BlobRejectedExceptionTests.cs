// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Tests.Blobs
{
    /// <summary>An upload refused for what it is carries the refusal's code and arguments.</summary>
    public class BlobRejectedExceptionTests
    {
        [Fact]
        public void A_rejected_upload_names_its_code_and_arguments()
        {
            BlobRejectedException rejected = new("Blob.TooLarge", new Dictionary<string, object?> { ["maxBytes"] = 1024 });

            Assert.Equal("Blob.TooLarge", rejected.Code);
            Assert.Equal(1024, rejected.Arguments["maxBytes"]);
            Assert.Equal("The upload was rejected: Blob.TooLarge.", rejected.Message);
            Assert.IsAssignableFrom<TellmaException>(rejected);
        }

        [Fact]
        public void A_rejected_upload_has_a_code_and_an_argument_bag()
        {
            _ = Assert.Throws<ArgumentException>(() => new BlobRejectedException(" ", new Dictionary<string, object?>()));
            _ = Assert.Throws<ArgumentNullException>(() => new BlobRejectedException("Blob.TooLarge", null!));
        }
    }
}
