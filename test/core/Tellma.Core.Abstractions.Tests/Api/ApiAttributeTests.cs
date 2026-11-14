// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>The projection attributes an entity declares: where they may go, what a leaf inherits, and their defaults.</summary>
    public class ApiAttributeTests
    {
        [Theory]
        [InlineData(typeof(ApiResourceAttribute))]
        [InlineData(typeof(DefaultSelectAttribute))]
        [InlineData(typeof(RelatedSelectAttribute))]
        [InlineData(typeof(DetailsExpandAttribute))]
        public void A_projection_attribute_goes_once_on_a_type_and_is_inherited(Type attribute)
        {
            AttributeUsageAttribute usage = attribute.GetCustomAttribute<AttributeUsageAttribute>()!;

            Assert.Equal(AttributeTargets.Class, usage.ValidOn);
            Assert.False(usage.AllowMultiple);
            Assert.True(usage.Inherited);
            Assert.True(attribute.IsSealed);
        }

        [Fact]
        public void An_api_resource_defaults_to_full_exposure_and_a_queryable_child()
        {
            ApiResourceAttribute resource = new();

            Assert.Null(resource.Segment);
            Assert.Null(resource.Description);
            Assert.Equal(McpExposure.Full, resource.Mcp);
            Assert.Equal(0, resource.ContractRevision);
            Assert.True(resource.Queryable);
        }

        [Fact]
        public void A_leaf_inherits_its_pack_defaults_projection_until_it_declares_its_own()
        {
            Assert.Equal("support-requests", typeof(Leaf).GetCustomAttribute<ApiResourceAttribute>(inherit: true)!.Segment);
            Assert.Equal("Id,Name", typeof(Leaf).GetCustomAttribute<RelatedSelectAttribute>(inherit: true)!.Select);
            Assert.Equal("Id,Code", typeof(Leaf).GetCustomAttribute<DefaultSelectAttribute>(inherit: true)!.Select);
            Assert.Equal(["Parent"], typeof(Leaf).GetCustomAttribute<DetailsExpandAttribute>(inherit: true)!.Navigations);
        }

        [Fact]
        public void Details_navigations_are_kept_in_order_and_none_is_empty()
        {
            Assert.Equal(["Parent", "CreatedBy"], new DetailsExpandAttribute("Parent", "CreatedBy").Navigations);
            Assert.Empty(new DetailsExpandAttribute().Navigations);
            Assert.Empty(new DetailsExpandAttribute(null!).Navigations);
        }

        [Fact]
        public void Mcp_exposure_has_three_levels()
        {
            Assert.Equal([McpExposure.Full, McpExposure.ReadOnly, McpExposure.Hidden], Enum.GetValues<McpExposure>());
        }

        /// <summary>A pack default carrying every projection attribute.</summary>
        [ApiResource(Segment = "support-requests", Mcp = McpExposure.Hidden)]
        [DefaultSelect("Id,Name")]
        [RelatedSelect("Id,Name")]
        [DetailsExpand("Parent")]
        private class PackDefault;

        /// <summary>A distribution's leaf that replaces only the default select.</summary>
        [DefaultSelect("Id,Code")]
        private sealed class Leaf : PackDefault;
    }
}
