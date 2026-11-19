// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Users;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The collapse of a role's exact duplicate permissions: one row per resource, action and
    ///     filter, compared ordinally, a stored row kept over a new one, the survivors in order.
    /// </summary>
    public sealed class PermissionCollapseTests
    {
        [Fact]
        public void Exact_duplicates_collapse_to_their_first_row_and_the_survivors_keep_their_order()
        {
            Permission read = Row(0, "fx.Widget", "Read", null);
            Permission save = Row(0, "fx.Widget", "Save", "Amount > 5");
            Role role = new() { Name = "Clerks", Permissions = [read, save, Row(0, "fx.Widget", "Read", null), Row(0, "fx.Widget", "Save", "Amount > 5")] };

            Assert.True(PermissionCollapse.Collapse(role));
            Assert.Equal([read, save], role.Permissions!);
        }

        [Fact]
        public void A_stored_row_survives_over_a_new_duplicate_sent_before_it()
        {
            Permission stored = Row(7, "fx.Widget", "Read", "Amount > 5");
            Permission kind = Row(0, "fx.Kind", "Read", null);
            Role role = new() { Name = "Clerks", Permissions = [Row(0, "fx.Widget", "Read", "Amount > 5"), kind, stored, Row(8, "fx.Widget", "Read", "Amount > 5")] };

            Assert.True(PermissionCollapse.Collapse(role));
            Assert.Equal([kind, stored], role.Permissions!);
        }

        [Fact]
        public void Rows_that_differ_in_case_or_in_their_filter_alone_are_not_duplicates_and_the_list_is_left_as_sent()
        {
            IReadOnlyList<Permission> permissions =
            [
                Row(0, "fx.Widget", "Read", null),
                Row(0, "fx.widget", "Read", null),
                Row(0, "fx.Widget", "Read", "Amount > 5"),
                Row(0, "fx.Widget", "Read", "amount > 5"),
            ];
            Role role = new() { Name = "Clerks", Permissions = permissions };

            Assert.False(PermissionCollapse.Collapse(role));
            Assert.Same(permissions, role.Permissions);
        }

        [Fact]
        public void A_role_without_permissions_or_with_one_is_left_as_sent()
        {
            Role absent = new() { Name = "Absent" };
            Role single = new() { Name = "Single", Permissions = [Row(0, "fx.Widget", "Read", null)] };

            Assert.False(PermissionCollapse.Collapse(absent));
            Assert.False(PermissionCollapse.Collapse(single));
            Assert.Null(absent.Permissions);
            _ = Assert.Single(single.Permissions!);
        }

        /// <summary>A permission row.</summary>
        /// <param name="id">Its id; zero for a new row.</param>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <param name="filter">The filter.</param>
        /// <returns>The row.</returns>
        private static Permission Row(int id, string resource, string action, string? filter)
        {
            return new Permission { Id = id, Resource = resource, Action = action, Filter = filter };
        }
    }
}
