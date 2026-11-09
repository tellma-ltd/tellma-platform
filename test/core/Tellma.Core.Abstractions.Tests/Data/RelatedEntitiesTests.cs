// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Tests.Data
{
    /// <summary>The related entities a read carries: one typed set per entity, restricted to its projection.</summary>
    public class RelatedEntitiesTests
    {
        [Fact]
        public void A_set_holds_its_instances_and_projection_by_entity_name()
        {
            RelatedEntities related = new();
            Item first = new(1);

            related.Add("gl.Center", [first], new HashSet<string> { "Id", "Name" });

            RelatedEntitySet set = related.Sets["gl.Center"];
            Assert.Equal(typeof(Item), set.EntityType);
            Assert.Same(first, Assert.Single(set.Entities));
            Assert.Equal(["Id", "Name"], set.Projection.Order(StringComparer.Ordinal));
        }

        [Fact]
        public void Adding_to_a_set_appends_its_instances_and_narrows_its_projection_to_the_shared_columns()
        {
            RelatedEntities related = new();
            related.Add("gl.Center", [new Item(1)], new HashSet<string> { "Id", "Name", "Code" });

            related.Add("gl.Center", [new Item(2)], new HashSet<string> { "Id", "Name", "Region" });

            RelatedEntitySet set = related.Sets["gl.Center"];
            Assert.Equal([1, 2], set.Entities.Cast<Item>().Select(static item => item.Id));
            Assert.Equal(["Id", "Name"], set.Projection.Order(StringComparer.Ordinal));
        }

        [Fact]
        public void A_set_refuses_instances_of_another_type_and_null_entries()
        {
            RelatedEntities related = new();
            related.Add("gl.Center", [new Item(1)], new HashSet<string> { "Id" });

            _ = Assert.Throws<InvalidOperationException>(() => related.Add("gl.Center", ["text"], new HashSet<string> { "Id" }));
            _ = Assert.Throws<ArgumentNullException>(() => related.Add<Item>("gl.Other", [null!], new HashSet<string> { "Id" }));
            _ = Assert.Throws<ArgumentException>(() => related.Add("  ", [new Item(3)], new HashSet<string> { "Id" }));
        }

        [Fact]
        public void A_projection_is_copied_so_the_callers_set_cannot_change_it()
        {
            RelatedEntities related = new();
            HashSet<string> projection = ["Id"];
            related.Add("gl.Center", [new Item(1)], projection);

            _ = projection.Add("Secret");

            Assert.DoesNotContain("Secret", related.Sets["gl.Center"].Projection);
        }

        /// <summary>A related instance.</summary>
        /// <param name="Id">Its id.</param>
        private sealed record Item(int Id);
    }
}
