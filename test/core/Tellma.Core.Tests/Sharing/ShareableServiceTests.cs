// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Reports;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Tests.Access;
using Tellma.Core.Tests.Crud.Query;

namespace Tellma.Core.Tests.Sharing
{
    /// <summary>
    ///     The shareable service's ownership rules, through the reports' stack: a new row takes the
    ///     caller as owner, or no one in a system scope; an update keeps its owner; a standard row is
    ///     read-only outside a system scope but for sharing and publishing; a lineage must be visible.
    /// </summary>
    public sealed class ShareableServiceTests : IDisposable
    {
        /// <summary>The service.</summary>
        private readonly SharingKit _kit = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        [Fact]
        public async Task A_new_row_takes_the_caller_as_its_owner_whatever_the_payload_says()
        {
            Report row = SharingKit.Row(owner: 7);

            await _kit.Hooks.PreprocessAsync(_kit.Save(system: false, (row, null)));

            Assert.Equal(SharingKit.CallerId, row.OwnerId);
        }

        [Fact]
        public async Task A_new_row_in_a_system_scope_is_a_standard_row()
        {
            Report row = SharingKit.Row(owner: 7);

            await _kit.Hooks.PreprocessAsync(_kit.Save(system: true, (row, null)));

            Assert.Null(row.OwnerId);
        }

        [Fact]
        public async Task An_update_leaves_the_owner_the_pipeline_restored_from_the_stored_row()
        {
            Report row = SharingKit.Row(id: 5, owner: 9);
            Report before = SharingKit.Row(id: 5, owner: 9);
            SaveContext<Report> context = _kit.Save(system: false, (row, before));

            await _kit.Hooks.PreprocessAsync(context);
            await _kit.Hooks.ValidateAsync(context);

            Assert.Equal(9, row.OwnerId);
            Assert.Empty(AccessRulesHarness.Errors(context.Errors));
        }

        [Fact]
        public async Task A_standard_row_refuses_a_save_outside_a_system_scope_and_saves_inside_one()
        {
            SaveContext<Report> user = _kit.Save(system: false, (SharingKit.Row(id: 5, owner: null), SharingKit.Row(id: 5, owner: null)));
            SaveContext<Report> system = _kit.Save(system: true, (SharingKit.Row(id: 5, owner: null), SharingKit.Row(id: 5, owner: null)));

            await _kit.Hooks.ValidateAsync(user);
            await _kit.Hooks.ValidateAsync(system);

            Assert.Equal([("[0]", SharingValidationCodes.StandardReadOnly)], AccessRulesHarness.Errors(user.Errors));
            Assert.Equal("core.Report", Assert.Single(user.Errors.Items).Arguments["entity"]);
            Assert.Empty(AccessRulesHarness.Errors(system.Errors));
        }

        [Fact]
        public async Task A_lineage_the_caller_cannot_read_is_refused_and_a_visible_one_is_not()
        {
            Report hidden = SharingKit.Row();
            hidden.BasedOnId = 11;
            Report shown = SharingKit.Row();
            shown.BasedOnId = 12;
            _kit.Loader.Visible.Add(SharingKit.Row(id: 12));
            SaveContext<Report> context = _kit.Save(system: false, (hidden, null), (shown, null));

            await _kit.Hooks.ValidateAsync(context);

            Assert.Equal([("[0].BasedOnId", SharingValidationCodes.BasedOnInvisible)], AccessRulesHarness.Errors(context.Errors));
            Assert.Equal("Id in (11, 12)", QueryRig.Render(Assert.Single(_kit.Loader.VisibleFilters)));
        }

        [Fact]
        public async Task A_standard_row_refuses_a_delete_outside_a_system_scope_at_its_position()
        {
            DeleteContext<Report, int> context = _kit.Delete(system: false, [3, 5], SharingKit.Row(id: 3), SharingKit.Row(id: 5, owner: null));

            await _kit.Hooks.ValidateDeleteAsync(context);

            Assert.Equal([("Ids[1]", SharingValidationCodes.StandardReadOnly)], AccessRulesHarness.Errors(context.Errors));
        }

        [Theory]
        [InlineData("activate", true)]
        [InlineData("deactivate", true)]
        [InlineData("transfer", true)]
        [InlineData("share", false)]
        [InlineData("publish", false)]
        public async Task A_standard_row_refuses_every_action_but_share_and_publish(string action, bool refused)
        {
            ActionContext<Report, int> context = _kit.Action(system: false, [5], SharingKit.Row(id: 5, owner: null));

            await _kit.Hooks.ValidateActionAsync(action, context);

            Assert.Equal(refused ? [("Ids[0]", SharingValidationCodes.StandardReadOnly)] : [], AccessRulesHarness.Errors(context.Errors));
        }

        [Fact]
        public async Task A_system_scope_activates_a_standard_row()
        {
            ActionContext<Report, int> context = _kit.Action(system: true, [5], SharingKit.Row(id: 5, owner: null));

            await _kit.Hooks.ValidateActionAsync("deactivate", context);

            Assert.Empty(AccessRulesHarness.Errors(context.Errors));
        }

        [Theory]
        [InlineData("save", true)]
        [InlineData("delete", true)]
        [InlineData("activate", true)]
        [InlineData("action:transfer", true)]
        [InlineData("action:share", false)]
        [InlineData("action:publish", false)]
        public void A_standard_rows_record_permissions_refuse_all_but_sharing_and_publishing(string ability, bool refused)
        {
            Assert.Equal(refused ? SharingValidationCodes.StandardReadOnly : null, _kit.Service.Refuse(SharingKit.Row(id: 5, owner: null), ability));
            Assert.Null(_kit.Service.Refuse(SharingKit.Row(id: 5), ability));
        }

        [Fact]
        public async Task The_sharing_actions_delegate_to_the_commands()
        {
            ActionContext<Report, int> context = _kit.Action(system: false, [5], SharingKit.Row(id: 5));
            ShareArguments share = new([new UserGrant(7, ShareAccess.Edit)], [8]);
            PublishArguments publish = new([new RoleGrant(3, ShareAccess.View)], null, Everyone: true);
            TransferArguments transfer = new(7);

            await _kit.Service.RunShareAsync(context, share);
            await _kit.Service.RunPublishAsync(context, publish);
            await _kit.Service.RunTransferAsync(context, transfer);

            Assert.Equal([("share", share), ("publish", publish), ("transfer", transfer)], _kit.Commands.Calls);
        }
    }
}
