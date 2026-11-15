// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Crud;

namespace Tellma.Core.Tests.Crud
{
    /// <summary>
    ///     The name grammars of a stack: resource segments, action names, named filter names and
    ///     confirmation keys, each anchored at the very end of the text, so a trailing line break
    ///     never passes.
    /// </summary>
    public class StackNamesTests
    {
        [Theory]
        [InlineData("gadgets", true)]
        [InlineData("gadget-parts", true)]
        [InlineData("v2-items", true)]
        [InlineData("Gadgets", false)]
        [InlineData("gadget--parts", false)]
        [InlineData("gadgets-", false)]
        [InlineData("2gadgets", false)]
        [InlineData("gadgets\n", false)]
        [InlineData("", false)]
        public void A_segment_is_one_kebab_case_word_run(string segment, bool valid)
        {
            Assert.Equal(valid, StackNames.IsSegment(segment));
        }

        [Theory]
        [InlineData("invite", true)]
        [InlineData("prices/reprice", true)]
        [InlineData("prices/", false)]
        [InlineData("invite\n", false)]
        [InlineData("prices/reprice\n", false)]
        public void An_action_name_is_kebab_case_segments_joined_by_a_slash(string name, bool valid)
        {
            Assert.Equal(valid, StackNames.IsActionName(name));
        }

        [Theory]
        [InlineData("mine", true)]
        [InlineData("on-gizmo", true)]
        [InlineData("Mine", false)]
        [InlineData("mine\n", false)]
        public void A_named_filter_name_starts_with_a_letter_and_holds_lower_case_letters_digits_and_hyphens(string name, bool valid)
        {
            Assert.Equal(valid, StackNames.IsNamedFilterName(name));
        }

        [Theory]
        [InlineData("Gadget.Recost", true)]
        [InlineData("Inventory.Backdated.Recost", true)]
        [InlineData("Gadget", false)]
        [InlineData("gadget.recost", false)]
        [InlineData("Gadget.Recost\n", false)]
        public void A_confirmation_key_is_dotted_pascal_case(string key, bool valid)
        {
            Assert.Equal(valid, StackNames.IsConfirmationKey(key));
        }
    }
}
