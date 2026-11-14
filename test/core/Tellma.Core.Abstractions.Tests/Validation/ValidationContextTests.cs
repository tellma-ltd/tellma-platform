// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tests.Crud;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Tests.Validation
{
    /// <summary>
    ///     The contexts a validation hook receives: what they answer about the payload, when the
    ///     values they load become readable, and what an action may compose before it persists.
    /// </summary>
    public class ValidationContextTests
    {
        [Fact]
        public void A_save_context_starts_at_the_first_round()
        {
            Assert.Equal(1, CrudTestItems.SaveContext([], []).Round);
        }

        [Fact]
        public void A_save_context_tells_an_insert_from_an_update_by_row_index()
        {
            SaveContext<Widget> context = CrudTestItems.SaveContext([new Widget(), new Widget()], [true, false]);

            Assert.True(context.IsNew(0));
            Assert.False(context.IsNew(1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => context.IsNew(2));
            Assert.Contains("no rows", Assert.Throws<ArgumentOutOfRangeException>(() => CrudTestItems.SaveContext([], []).IsNew(0)).Message, StringComparison.Ordinal);
            _ = Assert.Throws<InvalidOperationException>(() => CrudTestItems.SaveContext([new Widget()], null).IsNew(0));
        }

        [Fact]
        public void An_update_row_has_its_before_image_from_the_first_round_and_an_insert_never_has_one()
        {
            Widget before = new() { Name = "old" };
            SaveContext<Widget> context = CrudTestItems.SaveContext([new Widget(), new Widget { Name = "new" }], [true, false]);

            Assert.Null(context.Before(0));
            InvalidOperationException early = Assert.Throws<InvalidOperationException>(() => context.Before(1));
            Assert.Contains("LoadAsync", early.Message, StringComparison.Ordinal);

            context.BeforeImages = [null, before];

            Assert.Same(before, context.Before(1));
        }

        [Fact]
        public void A_property_changed_when_an_update_row_differs_from_its_before_image()
        {
            SaveContext<Widget> context = CrudTestItems.SaveContext(
                [new Widget { Name = "same" }, new Widget { Name = "same" }, new Widget { Name = "new" }, new Widget { Name = "gone" }],
                [true, false, false, false]);
            context.BeforeImages = [null, new Widget { Name = "same" }, new Widget { Name = "old" }, null];

            // An insert changes nothing; a row whose before image is missing changed everything.
            Assert.False(context.Changed(0, static row => row.Name));
            Assert.False(context.Changed(1, static row => row.Name));
            Assert.True(context.Changed(2, static row => row.Name));
            Assert.True(context.Changed(3, static row => row.Name));
            _ = Assert.Throws<ArgumentNullException>(() => context.Changed<string?>(1, null!));
        }

        [Fact]
        public void A_context_value_is_readable_once_loaded_and_loads_once()
        {
            ContextRef<int> pending = new();
            ContextRef<string> loaded = new("ready");

            Assert.False(pending.IsLoaded);
            Assert.Contains("LoadAsync", Assert.Throws<InvalidOperationException>(() => pending.Value).Message, StringComparison.Ordinal);

            pending.Complete(4);

            Assert.True(pending.IsLoaded);
            Assert.Equal(4, pending.Value);
            _ = Assert.Throws<InvalidOperationException>(() => pending.Complete(5));
            Assert.Equal(4, pending.Value);
            Assert.True(loaded.IsLoaded);
            Assert.Equal("ready", loaded.Value);
        }

        [Fact]
        public void The_rows_a_delete_or_an_action_targets_are_readable_from_the_first_round()
        {
            DeleteContext<Widget, int> delete = Delete();
            ActionContext<Widget, int> action = Action();
            Widget[] rows = [new()];

            _ = Assert.Throws<InvalidOperationException>(() => delete.Entities);
            _ = Assert.Throws<InvalidOperationException>(() => action.Entities);

            delete.Entities = rows;
            action.Entities = rows;

            Assert.Same(rows, delete.Entities);
            Assert.Same(rows, action.Entities);
            Assert.Equal([1], delete.Ids);
            Assert.Equal([1], action.Ids);
        }

        [Fact]
        public void An_action_composes_saves_and_updates_until_its_batch_is_assembled()
        {
            ActionContext<Widget, int> action = Action();
            Widget row = new();
            Dictionary<string, object?> update = new() { ["IsActive"] = false };

            action.Save([row]);
            action.Update(update);
            update["Name"] = "changed after the call";

            Assert.Equal([row], action.SavedRows);
            Assert.Equal(["IsActive"], Assert.Single(action.Updates).Keys);
            _ = Assert.Throws<ArgumentException>(() => action.Save([null!]));
            _ = Assert.Throws<ArgumentException>(() => action.Update(new Dictionary<string, object?>()));

            action.Close();

            InvalidOperationException late = Assert.Throws<InvalidOperationException>(() => action.Save([row]));
            Assert.Contains("already assembled", late.Message, StringComparison.Ordinal);
            _ = Assert.Throws<InvalidOperationException>(() => action.Update(new Dictionary<string, object?> { ["IsActive"] = true }));
            Assert.Single(action.SavedRows);
        }

        [Fact]
        public async Task An_action_learns_when_its_writes_have_persisted()
        {
            ActionContext<Widget, int> action = Action();

            Assert.False(action.Persisted.IsCompleted);

            action.PersistedSource.SetResult();

            await action.Persisted;
        }

        [Fact]
        public void An_action_context_refuses_notifications_and_assignments_while_the_host_registers_neither()
        {
            ActionContext<Widget, int> action = Action();

            _ = Assert.Throws<NotSupportedException>(() => action.Notify(new NotificationRequest("widget.archived", [1], null, null, null, null, null)));
            _ = Assert.Throws<NotSupportedException>(() => action.Withdraw(new WithdrawRequest("widget:1")));
            _ = Assert.Throws<NotSupportedException>(() => action.Assignments);
        }

        /// <summary>A delete context over one id, its rows not yet loaded.</summary>
        /// <returns>The context.</returns>
        private static DeleteContext<Widget, int> Delete()
        {
            return new DeleteContext<Widget, int>
            {
                Ids = [1],
                Context = CrudTestItems.Context(),
                Loader = CrudTestItems.UnusedLoader(),
                Errors = new ValidationErrors<Widget>(),
                Host = CrudTestItems.ForeignHost(),
            };
        }

        /// <summary>An action context over one id, its rows not yet loaded.</summary>
        /// <returns>The context.</returns>
        private static ActionContext<Widget, int> Action()
        {
            return new ActionContext<Widget, int>
            {
                Ids = [1],
                Context = CrudTestItems.Context(),
                Loader = CrudTestItems.UnusedLoader(),
                Errors = new ValidationErrors<Widget>(),
                Batch = CrudTestItems.UnusedBatch(),
                Host = CrudTestItems.ForeignHost(),
            };
        }
    }
}
