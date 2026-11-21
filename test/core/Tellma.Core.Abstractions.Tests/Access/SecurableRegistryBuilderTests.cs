// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tests.Access
{
    /// <summary>
    ///     The securable contracts: the builder records every call as made, on behalf of the running
    ///     contributor, and refuses every call once frozen; the descriptor and the pair records.
    /// </summary>
    public class SecurableRegistryBuilderTests
    {
        [Fact]
        public void The_builder_records_each_call_with_the_feature_of_the_running_contributor()
        {
            SecurableRegistryBuilder builder = new() { Contributor = "gl" };
            SecurableOwner owner = new("gl.Center", "Center");

            _ = builder
                .Add("gl.Center", "Read", "gl.Center")
                .Add("gl.CenterLine", "Read", "gl.CenterLine", isSensitive: true, owner: owner, feature: "gl.reports")
                .Alias("gl.CostCenter", "gl.Center")
                .MarkSensitive("gl.Center", "Read");
            builder.Contributor = "acme";
            _ = builder.MarkNotSensitive("gl.Center", "Read");

            Assert.Equal(
                [
                    new SecurableDeclaration("gl.Center", "Read", "gl.Center", false, null, null, "gl"),
                    new SecurableDeclaration("gl.CenterLine", "Read", "gl.CenterLine", true, owner, "gl.reports", "gl"),
                ],
                builder.Securables);
            Assert.Equal([new SecurableAliasDeclaration("gl.CostCenter", "gl.Center", "gl")], builder.Aliases);
            Assert.Equal(
                [new SecurableMarkDeclaration("gl.Center", "Read", true, "gl"), new SecurableMarkDeclaration("gl.Center", "Read", false, "acme")],
                builder.Marks);
        }

        [Fact]
        public void A_frozen_builder_refuses_every_call_and_records_nothing_more()
        {
            SecurableRegistryBuilder builder = new();
            _ = builder.Add("gl.Center", "Read", "gl.Center");
            builder.Freeze();

            _ = Assert.Throws<InvalidOperationException>(() => builder.Add("gl.Center", "Save", "gl.Center"));
            _ = Assert.Throws<InvalidOperationException>(() => builder.Alias("gl.CostCenter", "gl.Center"));
            _ = Assert.Throws<InvalidOperationException>(() => builder.MarkSensitive("gl.Center", "Read"));
            _ = Assert.Throws<InvalidOperationException>(() => builder.MarkNotSensitive("gl.Center", "Read"));

            _ = Assert.Single(builder.Securables);
            Assert.Empty(builder.Aliases);
            Assert.Empty(builder.Marks);
        }

        [Fact]
        public void The_builder_refuses_null_names_and_an_owner_missing_a_part()
        {
            SecurableRegistryBuilder builder = new();

            _ = Assert.Throws<ArgumentNullException>(() => builder.Add(null!, "Read", null));
            _ = Assert.Throws<ArgumentNullException>(() => builder.Add("gl.Center", null!, null));
            _ = Assert.Throws<ArgumentException>(() => builder.Add("gl.CenterLine", "Read", "gl.CenterLine", owner: new SecurableOwner(null!, "Center")));
            _ = Assert.Throws<ArgumentException>(() => builder.Add("gl.CenterLine", "Read", "gl.CenterLine", owner: new SecurableOwner("gl.Center", null!)));
            _ = Assert.Throws<ArgumentNullException>(() => builder.Alias(null!, "gl.Center"));
            _ = Assert.Throws<ArgumentNullException>(() => builder.Alias("gl.CostCenter", null!));
            _ = Assert.Throws<ArgumentNullException>(() => builder.MarkSensitive(null!, "Read"));
            _ = Assert.Throws<ArgumentNullException>(() => builder.MarkNotSensitive("gl.Center", null!));

            // Malformed names are recorded: the registry reports them, all at once.
            _ = builder.Add("*", "", null);
            _ = Assert.Single(builder.Securables);
        }

        [Fact]
        public void A_descriptor_supports_filters_exactly_when_it_has_a_filter_root_and_displays_its_pair()
        {
            SecurableDescriptor filtered = new("gl.Center", "Read", "gl.Center", false, "gl", null);
            SecurableDescriptor unfiltered = filtered with { FilterRoot = null };

            Assert.True(filtered.SupportsFilter);
            Assert.False(unfiltered.SupportsFilter);
            Assert.Equal("gl.Center:Read", filtered.ToString());
            Assert.Equal("gl.Center:Read", new SecurableRef("gl.Center", "Read").ToString());
            Assert.Equal(new NoActivityStampMetadata(), new NoActivityStampMetadata());
        }

        [Fact]
        public void The_sugar_records_one_item_and_refuses_null()
        {
            FeatureContribution contribution = new();

            _ = contribution.Securables(Configure);
            _ = Assert.Throws<ArgumentNullException>(() => contribution.Securables(null!));

            Assert.Equal([new SecurablesContributionItem(Configure)], contribution.Items);

            // The configuration the item carries.
            static void Configure(SecurableRegistryBuilder builder)
            {
                _ = builder.Add("acme.Report", "Read", null);
            }
        }
    }
}
