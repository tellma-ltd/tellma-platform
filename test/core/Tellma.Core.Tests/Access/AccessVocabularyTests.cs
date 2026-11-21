// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Access;

namespace Tellma.Core.Tests.Access
{
    /// <summary>The access vocabulary: well-known ids in the reserved band, actions and resources in their grammars.</summary>
    public class AccessVocabularyTests
    {
        [Fact]
        public void Every_well_known_id_lies_in_the_reserved_band()
        {
            Assert.Equal(999, WellKnownIds.ReservedIdBandEnd);
            Assert.All(
                Constants<int>(typeof(WellKnownIds)).Where(static constant => constant.Name != nameof(WellKnownIds.ReservedIdBandEnd)),
                static constant => Assert.InRange(constant.Value, 1, WellKnownIds.ReservedIdBandEnd));
        }

        [Fact]
        public void Every_action_is_pascal_case_within_32_characters_or_the_wildcard()
        {
            List<(string Name, string Value)> actions = Constants<string>(typeof(AccessActions));

            Assert.Equal(14, actions.Count);
            Assert.All(actions, static action => Assert.True(
                action.Value == "*" || (action.Value == action.Name && SecurableNames.IsAction(action.Value)),
                action.Name));
        }

        [Fact]
        public void Every_resource_is_dotted_within_128_characters_or_the_wildcard()
        {
            List<(string Name, string Value)> resources = Constants<string>(typeof(CoreResources));

            Assert.All(
                resources.Where(static resource => resource.Name is not nameof(CoreResources.Wildcard) and not nameof(CoreResources.SettingsPrefix)),
                static resource => Assert.True(SecurableNames.IsResource(resource.Value), resource.Name));
            Assert.Equal("*", CoreResources.Wildcard);
            Assert.StartsWith(CoreResources.SettingsPrefix, CoreResources.SettingsGeneral, StringComparison.Ordinal);
        }

        /// <summary>The public constants of a static class, of one type.</summary>
        /// <typeparam name="T">The constants' type.</typeparam>
        /// <param name="type">The class.</param>
        /// <returns>Each constant's name and value.</returns>
        private static List<(string Name, T Value)> Constants<T>(Type type)
        {
            return [.. type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(static field => field.IsLiteral && field.FieldType == typeof(T))
                .Select(static field => (field.Name, (T)field.GetRawConstantValue()!))];
        }
    }
}
