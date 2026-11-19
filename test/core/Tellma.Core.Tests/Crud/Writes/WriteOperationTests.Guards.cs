// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tests.Crud.Save;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Crud.Writes
{
    // The access guards on the delete's and the action's persists: decided after every contribution,
    // over the stack's tables and every statement's declared writes, and never in a system scope.
    /// <content>The access guards' place in the delete's and the action's persist batches.</content>
    public sealed partial class WriteOperationTests
    {
        /// <summary>The table a participant's raw statement declares it writes: a security table.</summary>
        private static readonly TableName SecurityTable = new("core", "Roles");

        [Fact]
        public async Task A_delete_whose_participant_declares_a_write_of_a_security_table_takes_the_guards_after_every_contribution()
        {
            _hooks.Contribute = static context =>
            {
                _ = context.Batch.Sql($"SELECT {"raw"};", new SqlOptions(new HashSet<TableName> { SecurityTable }));
                return Task.CompletedTask;
            };

            _ = await TicketsAsync(tickets => tickets.DeleteByIdsAsync([2], null, Token));

            // --- The delete, the participant's raw write, then the lock and the invariants ---
            ScriptedBatch persist = Assert.Single(_rig.Script.Batches, static batch => batch.Purpose == BatchPurpose.Persist);
            AssertGuardedAfter(persist, Assert.Single(persist.Of(ScriptedKind.Delete)).Ordinal);
        }

        [Fact]
        public async Task An_action_whose_method_declares_a_write_of_a_security_table_takes_the_guards_after_every_contribution()
        {
            _hooks.Close = static context =>
            {
                _ = context.Batch.Sql($"SELECT {"raw"};", new SqlOptions(new HashSet<TableName> { SecurityTable }));
                return Task.CompletedTask;
            };
            _hooks.Contribute = static context =>
            {
                _ = context.Batch.Sql($"SELECT {"contributed"};", SqlOptions.None);
                return Task.CompletedTask;
            };

            _ = await TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [2], null, new ActionOptions(), Token));

            // --- The method's raw write and the persist hook's statement, then the lock and the invariants ---
            ScriptedBatch persist = Assert.Single(_rig.Script.Batches, static batch => batch.Purpose == BatchPurpose.Persist);
            AssertGuardedAfter(persist, SqlOrdinal(persist, "contributed"));
        }

        [Fact]
        public async Task Neither_a_delete_nor_an_action_takes_the_guards_in_a_system_scope_or_without_a_write_of_a_security_table()
        {
            // --- A system scope's persists, each declaring a security write ---
            _hooks.Close = static context =>
            {
                _ = context.Batch.Sql($"SELECT {"raw"};", new SqlOptions(new HashSet<TableName> { SecurityTable }));
                return Task.CompletedTask;
            };
            _hooks.Contribute = static context =>
            {
                _ = context.Batch.Sql($"SELECT {"raw"};", new SqlOptions(new HashSet<TableName> { SecurityTable }));
                return Task.CompletedTask;
            };
            _rig.Kind = PrincipalKind.System;
            _ = await TicketsAsync(tickets => tickets.DeleteByIdsAsync([2], null, Token));
            _ = await TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [3], null, new ActionOptions(), Token));
            _rig.Kind = PrincipalKind.User;

            // --- A caller's persists that declare no write of a security table ---
            _hooks.Close = null;
            _hooks.Contribute = null;
            _ = await TicketsAsync(tickets => tickets.DeleteByIdsAsync([4], null, Token));
            _ = await TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [5], null, new ActionOptions(), Token));

            Assert.Equal(4, _rig.Script.Batches.Count(static batch => batch.Purpose == BatchPurpose.Persist && batch.IsExecuted));
            Assert.Empty(_guards.Calls);
        }

        /// <summary>
        ///     Asserts that the guards were called once each, lock first, and that their statements
        ///     follow the raw write and the last contribution, in that order.
        /// </summary>
        /// <param name="persist">The persist batch.</param>
        /// <param name="last">The ordinal of the last contribution the guards follow.</param>
        private void AssertGuardedAfter(ScriptedBatch persist, int last)
        {
            int raw = SqlOrdinal(persist, "raw");
            int locked = SqlOrdinal(persist, PsGuards.Lock);
            int invariants = SqlOrdinal(persist, PsGuards.Invariants);
            Assert.True(raw < locked && last < locked && locked < invariants, $"raw {raw}, last {last}, lock {locked}, invariants {invariants}");
            Assert.Equal([PsGuards.Lock, PsGuards.Invariants], _guards.Calls);
        }

        /// <summary>The ordinal of the one raw statement of a batch with the given text argument.</summary>
        /// <param name="persist">The batch.</param>
        /// <param name="text">The statement's text argument.</param>
        /// <returns>The ordinal.</returns>
        private static int SqlOrdinal(ScriptedBatch persist, string text)
        {
            return Assert.Single(persist.Of(ScriptedKind.Sql), statement => (string)statement.Text.GetArgument(0)! == text).Ordinal;
        }
    }
}
