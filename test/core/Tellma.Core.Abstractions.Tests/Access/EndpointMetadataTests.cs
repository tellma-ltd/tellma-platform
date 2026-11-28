// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Abstractions.Tests.Access
{
    /// <summary>The endpoint metadata records the startup audit reads: value records naming a securable, or none.</summary>
    public class EndpointMetadataTests
    {
        [Fact]
        public void A_securable_endpoint_names_its_pair_and_compares_by_value()
        {
            SecurableEndpointMetadata metadata = new("gl.Center", "Save");

            Assert.Equal(("gl.Center", "Save"), (metadata.Resource, metadata.Action));
            Assert.Equal(new SecurableEndpointMetadata("gl.Center", "Save"), metadata);
            Assert.NotEqual(new SecurableEndpointMetadata("gl.Center", "Read"), metadata);
        }

        [Theory]
        [InlineData(null, "Save")]
        [InlineData(" ", "Save")]
        [InlineData("gl.Center", null)]
        [InlineData("gl.Center", "")]
        public void A_securable_endpoint_names_both_halves_of_the_pair(string? resource, string? action)
        {
            _ = Assert.ThrowsAny<ArgumentException>(() => new SecurableEndpointMetadata(resource!, action!));
        }

        [Fact]
        public void Every_member_endpoint_marker_is_the_same_value()
        {
            Assert.Equal(new MemberEndpointMetadata(), new MemberEndpointMetadata());
        }
    }
}
