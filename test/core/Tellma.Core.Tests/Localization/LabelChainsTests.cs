// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Localization;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>The label key chains: each kind of label's keys, most specific first.</summary>
    public sealed class LabelChainsTests
    {
        [Fact]
        public void An_entitys_chains_name_its_schema_and_entity()
        {
            Assert.Equal(["Gl_Center"], LabelChains.Entity("gl.Center"));
            Assert.Equal(["Gl_Center_Plural", "Gl_Center"], LabelChains.Plural("gl.Center"));
            Assert.Equal(["Gl_Center_New", "Entity_New"], LabelChains.New("gl.Center"));
        }

        [Fact]
        public void A_navigation_passes_through_its_foreign_key_and_a_sibling_has_none()
        {
            Assert.Equal(
                ["Gl_Center_Manager", "Gl_Center_ManagerId", "ManagerId", "Manager"],
                LabelChains.Navigation("gl.Center", "Manager", "ManagerId"));
            Assert.Equal(["Acme_Customer_Extra", "Extra"], LabelChains.Navigation("acme.Customer", "Extra", foreignKey: null));
        }

        [Fact]
        public void A_collection_falls_back_to_its_childs_plural()
        {
            Assert.Equal(
                ["Acme_Customer_Contacts", "Acme_CustomerContact_Plural", "Acme_CustomerContact"],
                LabelChains.Collection("acme.Customer", "Contacts", "acme.CustomerContact"));
            Assert.Equal(["Acme_Customer_Contacts"], LabelChains.Collection("acme.Customer", "Contacts", child: null));
        }

        [Fact]
        public void An_action_and_its_fields_take_the_entitys_keys_then_the_shared_ones()
        {
            Assert.Equal(["Acme_Customer_Action_RanksRecompute", "Action_RanksRecompute"], LabelChains.Action("acme.Customer", "ranks/recompute"));
            Assert.Equal(
                ["Acme_Customer_Action_SetCreditLimit_Comment", "Acme_Customer_Comment", "Comment"],
                LabelChains.ActionField("acme.Customer", "set-credit-limit", "Comment"));
        }

        [Fact]
        public void An_enum_value_has_a_key_per_schema_that_carries_it()
        {
            Assert.Equal(["Acme_CustomerType_Retail", "Gl_CustomerType_Retail"], LabelChains.EnumValue(["Acme", "Gl"], "CustomerType", "Retail"));
        }

        [Fact]
        public void A_fields_key_name_drops_the_id_of_an_entity_reference_only()
        {
            Assert.Equal("Assignee", LabelChains.FieldName(typeof(Fields).GetProperty(nameof(Fields.AssigneeId))!));
            Assert.Equal("ExternalId", LabelChains.FieldName(typeof(Fields).GetProperty(nameof(Fields.ExternalId))!));
            Assert.Equal("Id", LabelChains.FieldName(typeof(Fields).GetProperty(nameof(Fields.Id))!));
        }

        /// <summary>Argument members whose key names the chains derive.</summary>
        /// <param name="AssigneeId">An entity reference.</param>
        /// <param name="ExternalId">A plain identifier.</param>
        /// <param name="Id">An entity reference named only by the suffix.</param>
        private sealed record Fields(
            [property: EntityRef("core.User")] int AssigneeId,
            string ExternalId,
            [property: EntityRef("core.User")] int Id);
    }
}
