// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Testing.Crud;
using static Tellma.Module.Gl.Tests.GlTestKit;

namespace Tellma.Module.Gl.Tests.Rules
{
    /// <summary>
    ///     The centers' save and delete rules over a scripted loader: a parent's type read from the
    ///     payload or loaded once, a type change refused only while the center keeps a child, a row
    ///     under itself left to the platform, and a delete refused only while it leaves a child behind.
    /// </summary>
    public class CenterServiceRulesTests
    {
        private readonly TestCenterService _service = new();

        // --- The parent's type ---

        [Fact]
        public async Task A_parent_in_the_payload_is_read_from_the_payload_and_nothing_loads()
        {
            ScriptedLoader loader = new();
            SaveContext<Center> context = Save(loader, (Center(-1, CenterType.Sale), null), (Center(-2, CenterType.Operation, parentId: -1), null));

            await _service.ValidateSaveAsync(context);

            Assert.Equal([Error("[1].ParentId", GlValidationCodes.ParentMustBeGrouping)], Errors(context));
            Assert.Empty(loader.ByIdLoads);
            Assert.Equal(0, loader.Rounds);
        }

        [Fact]
        public async Task A_grouping_parent_in_the_payload_passes()
        {
            ScriptedLoader loader = new();
            SaveContext<Center> context = Save(
                loader,
                (Center(-1, CenterType.Abstract), null),
                (Center(-2, CenterType.BusinessUnit, parentId: -1), null),
                (Center(-3, CenterType.Sale, parentId: -2), null));

            await _service.ValidateSaveAsync(context);

            Assert.Empty(Errors(context));
        }

        [Fact]
        public async Task Parents_outside_the_payload_load_once_by_id_in_one_round()
        {
            ScriptedLoader loader = new(Center(10, CenterType.Service), Center(11, CenterType.BusinessUnit));
            SaveContext<Center> context = Save(
                loader,
                (Center(-1, CenterType.Sale, parentId: 10), null),
                (Center(-2, CenterType.Sale, parentId: 11), null),
                (Center(-3, CenterType.Sale, parentId: 10), null));

            await _service.ValidateSaveAsync(context);

            Assert.Equal(
                [Error("[0].ParentId", GlValidationCodes.ParentMustBeGrouping), Error("[2].ParentId", GlValidationCodes.ParentMustBeGrouping)],
                Errors(context));
            (IReadOnlyList<int> ids, string? select) = Assert.Single(loader.ByIdLoads);
            Assert.Equal([10, 11], ids);
            Assert.Equal("Id, CenterType", select);
            Assert.Equal(1, loader.Rounds);
        }

        [Fact]
        public async Task A_parent_the_platform_already_refused_is_not_judged_again()
        {
            // A parent the caller cannot see is the pipeline's not-found reference; its type must not show.
            ScriptedLoader loader = new(Center(10, CenterType.Service));
            SaveContext<Center> context = Save(loader, (Center(-1, CenterType.Sale, parentId: 10), null));
            context.Errors.Add(ValidationPath.Root.Index(0).Property(nameof(Center.ParentId)), "Fk.NotFound");

            await _service.ValidateSaveAsync(context);

            Assert.Equal([Error("[0].ParentId", "Fk.NotFound")], Errors(context));
        }

        [Fact]
        public async Task A_parent_that_does_not_exist_is_left_to_the_platform()
        {
            ScriptedLoader loader = new();
            SaveContext<Center> context = Save(loader, (Center(-1, CenterType.Sale, parentId: 99), null));

            await _service.ValidateSaveAsync(context);

            Assert.Empty(Errors(context));
        }

        [Fact]
        public async Task An_update_that_keeps_its_stored_parent_is_not_judged_by_it()
        {
            // The stored parent grouped when the row was saved under it, and stops grouping only once childless.
            Center stored = Center(20, CenterType.Sale, parentId: 10);
            ScriptedLoader loader = new(Center(10, CenterType.Service), stored);
            SaveContext<Center> context = Save(loader, (Edited(stored, static center => center.Name = "Renamed"), stored));

            await _service.ValidateSaveAsync(context);

            Assert.Empty(Errors(context));
        }

        [Fact]
        public async Task An_update_that_moves_under_a_non_grouping_parent_is_refused_at_its_parent()
        {
            Center stored = Center(20, CenterType.Sale, parentId: 11);
            ScriptedLoader loader = new(Center(10, CenterType.Service), Center(11, CenterType.Abstract), stored);
            SaveContext<Center> context = Save(loader, (Edited(stored, static center => center.ParentId = 10), stored));

            await _service.ValidateSaveAsync(context);

            Assert.Equal([Error("[0].ParentId", GlValidationCodes.ParentMustBeGrouping)], Errors(context));
        }

        [Fact]
        public async Task A_row_under_itself_is_left_to_the_platforms_tree_cycle()
        {
            Center stored = Center(20, CenterType.Sale);
            ScriptedLoader loader = new(stored);
            SaveContext<Center> context = Save(loader, (Edited(stored, static center => center.ParentId = 20), stored));

            await _service.ValidateSaveAsync(context);

            Assert.Empty(Errors(context));
            Assert.Empty(loader.ByIdLoads);
        }

        // --- A type change ---

        [Fact]
        public async Task A_center_with_stored_children_cannot_stop_grouping()
        {
            Center stored = Center(10, CenterType.BusinessUnit, subtreeCount: 3);
            ScriptedLoader loader = new(stored);
            SaveContext<Center> context = Save(loader, (Edited(stored, static center => center.CenterType = CenterType.Sale), stored));

            await _service.ValidateSaveAsync(context);

            Assert.Equal([Error("[0].CenterType", GlValidationCodes.ParentMustBeGrouping)], Errors(context));
        }

        [Fact]
        public async Task A_center_without_children_may_stop_grouping()
        {
            Center stored = Center(10, CenterType.BusinessUnit);
            ScriptedLoader loader = new(stored);
            SaveContext<Center> context = Save(loader, (Edited(stored, static center => center.CenterType = CenterType.Service), stored));

            await _service.ValidateSaveAsync(context);

            Assert.Empty(Errors(context));
        }

        [Fact]
        public async Task A_center_that_stops_grouping_while_the_payload_puts_a_child_under_it_is_refused_at_both()
        {
            Center stored = Center(10, CenterType.BusinessUnit);
            ScriptedLoader loader = new(stored);
            SaveContext<Center> context = Save(
                loader,
                (Edited(stored, static center => center.CenterType = CenterType.Operation), stored),
                (Center(-1, CenterType.Sale, parentId: 10), null));

            await _service.ValidateSaveAsync(context);

            Assert.Equal(
                [Error("[1].ParentId", GlValidationCodes.ParentMustBeGrouping), Error("[0].CenterType", GlValidationCodes.ParentMustBeGrouping)],
                Errors(context));
        }

        [Fact]
        public async Task A_center_that_moves_every_child_away_in_the_same_save_may_stop_grouping()
        {
            // The center holds two children, one with a child of its own: 4 rows in its subtree.
            Center stored = Center(10, CenterType.BusinessUnit, subtreeCount: 4);
            Center first = Center(11, CenterType.BusinessUnit, parentId: 10, subtreeCount: 2);
            Center second = Center(12, CenterType.Sale, parentId: 10);
            ScriptedLoader loader = new(stored, first, second, Center(30, CenterType.Abstract));
            SaveContext<Center> context = Save(
                loader,
                (Edited(stored, static center => center.CenterType = CenterType.Sale), stored),
                (Edited(first, static center => center.ParentId = 30), first),
                (Edited(second, static center => center.ParentId = 30), second));

            await _service.ValidateSaveAsync(context);

            Assert.Empty(Errors(context));
        }

        [Fact]
        public async Task A_center_that_moves_only_some_children_away_cannot_stop_grouping()
        {
            Center stored = Center(10, CenterType.BusinessUnit, subtreeCount: 4);
            Center first = Center(11, CenterType.BusinessUnit, parentId: 10, subtreeCount: 2);
            ScriptedLoader loader = new(stored, first, Center(30, CenterType.Abstract));
            SaveContext<Center> context = Save(
                loader,
                (Edited(stored, static center => center.CenterType = CenterType.Sale), stored),
                (Edited(first, static center => center.ParentId = 30), first));

            await _service.ValidateSaveAsync(context);

            Assert.Equal([Error("[0].CenterType", GlValidationCodes.ParentMustBeGrouping)], Errors(context));
        }

        [Fact]
        public async Task A_switch_between_grouping_types_keeps_the_children()
        {
            Center stored = Center(10, CenterType.BusinessUnit, subtreeCount: 5);
            ScriptedLoader loader = new(stored);
            SaveContext<Center> context = Save(loader, (Edited(stored, static center => center.CenterType = CenterType.Abstract), stored));

            await _service.ValidateSaveAsync(context);

            Assert.Empty(Errors(context));
        }

        // --- A delete ---

        [Fact]
        public async Task A_delete_of_a_center_with_children_is_refused_at_its_id()
        {
            ScriptedLoader loader = new();
            DeleteContext<Center, int> context = ValidationContexts.Delete(
                loader, Caller, [12, 10], [Center(10, CenterType.Abstract, subtreeCount: 2), Center(12, CenterType.Sale)]);

            await _service.ValidateRemovalAsync(context);

            Assert.Equal([Error("Ids[1]", GlValidationCodes.HasChildren)], Errors(context));
            Assert.Equal(1, loader.Rounds);
        }

        [Fact]
        public async Task A_delete_that_names_a_whole_subtree_passes()
        {
            ScriptedLoader loader = new();
            DeleteContext<Center, int> context = ValidationContexts.Delete(
                loader,
                Caller,
                [10, 11, 12],
                [Center(10, CenterType.Abstract, subtreeCount: 3), Center(11, CenterType.BusinessUnit, parentId: 10, subtreeCount: 2), Center(12, CenterType.Sale, parentId: 11)]);

            await _service.ValidateRemovalAsync(context);

            Assert.Empty(Errors(context));
        }

        [Fact]
        public async Task A_delete_that_skips_a_level_leaves_a_child_behind()
        {
            // The grandchild is named but its parent is not, so the root would keep a child.
            ScriptedLoader loader = new();
            DeleteContext<Center, int> context = ValidationContexts.Delete(
                loader, Caller, [10, 12], [Center(10, CenterType.Abstract, subtreeCount: 3), Center(12, CenterType.Sale, parentId: 11)]);

            await _service.ValidateRemovalAsync(context);

            Assert.Equal([Error("Ids[0]", GlValidationCodes.HasChildren)], Errors(context));
        }

        [Fact]
        public async Task A_delete_with_descendants_is_never_refused_and_loads_nothing()
        {
            ScriptedLoader loader = new();
            DeleteContext<Center, int> context = ValidationContexts.Delete(
                loader, Caller, [10], [Center(10, CenterType.Abstract, subtreeCount: 7)], withDescendants: true);

            await _service.ValidateRemovalAsync(context);

            Assert.Empty(Errors(context));
            Assert.Equal(0, loader.Rounds);
        }

        [Fact]
        public void The_descendant_count_never_loops_over_stored_data_that_is_not_a_tree()
        {
            Dictionary<int, int> counts = CenterService<Center>.TargetedDescendants(
                [Center(1, CenterType.Abstract, parentId: 2), Center(2, CenterType.Abstract, parentId: 1)]);

            Assert.Equal(2, counts.Count);
        }

        /// <summary>A save's context over a scripted loader.</summary>
        /// <param name="loader">The loader.</param>
        /// <param name="rows">The payload, each with its before image; null for a new row.</param>
        /// <returns>The context.</returns>
        private static SaveContext<Center> Save(ScriptedLoader loader, params (Center Row, Center? Before)[] rows)
        {
            return ValidationContexts.Save(loader, Caller, rows);
        }

        /// <summary>An error as the assertions compare it.</summary>
        /// <param name="path">The path, in bracket form.</param>
        /// <param name="code">The code.</param>
        /// <returns>The pair.</returns>
        private static (string Path, string Code) Error(string path, string code)
        {
            return (path, code);
        }

        /// <summary>A context's errors, as the assertions compare them.</summary>
        /// <param name="errors">The errors.</param>
        /// <returns>Each error's path, in bracket form, and code.</returns>
        private static (string Path, string Code)[] Errors(ValidationErrors errors)
        {
            return [.. errors.Items.Select(static error => (error.Path.ToString(), error.Code))];
        }

        /// <summary>A save's errors.</summary>
        /// <param name="context">The context.</param>
        /// <returns>The errors.</returns>
        private static (string Path, string Code)[] Errors(SaveContext<Center> context)
        {
            return Errors(context.Errors);
        }

        /// <summary>A delete's errors.</summary>
        /// <param name="context">The context.</param>
        /// <returns>The errors.</returns>
        private static (string Path, string Code)[] Errors(DeleteContext<Center, int> context)
        {
            return Errors(context.Errors);
        }
    }
}
