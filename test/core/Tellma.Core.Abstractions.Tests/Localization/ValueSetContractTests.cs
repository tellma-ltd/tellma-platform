// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Tests.Localization
{
    /// <summary>The value-set contract: the attribute, the platform's set names, and the descriptor records.</summary>
    public class ValueSetContractTests
    {
        [Fact]
        public void The_platforms_set_names_are_pinned()
        {
            // The names are stored nowhere, but every surface (filters, spreadsheets, the client's
            // metadata) carries them, and a feature's entity names them in an attribute.
            Assert.Equal("core.Language", CoreValueSets.Language);
            Assert.Equal("core.Calendar", CoreValueSets.Calendar);
            Assert.Equal("core.TimeZone", CoreValueSets.TimeZone);
            Assert.Equal("core.Region", CoreValueSets.Region);
        }

        [Fact]
        public void The_attribute_names_a_set_on_a_property_once_and_a_leaf_inherits_it()
        {
            AttributeUsageAttribute usage = typeof(ValueSetAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;

            Assert.Equal(AttributeTargets.Property, usage.ValidOn);
            Assert.False(usage.AllowMultiple);
            Assert.True(usage.Inherited);
            Assert.Equal("core.TimeZone", typeof(Leaf).GetProperty(nameof(Default.Zone))!.GetCustomAttribute<ValueSetAttribute>()!.Name);
            Assert.Equal("core.Region", typeof(Leaf).GetProperty(nameof(Default.Region))!.GetCustomAttribute<ValueSetAttribute>(inherit: true)!.Name);
        }

        [Fact]
        public void A_descriptor_has_no_aliases_unless_given_and_compares_by_its_parts()
        {
            ValueSetEntry[] values = [new("gc", "Calendar_gc", null)];
            ValueSetDescriptor set = new("core.Calendar", values);

            Assert.Null(set.Aliases);
            Assert.Equal(new ValueSetDescriptor("core.Calendar", values), set);
            Assert.Equal(new ValueSetEntry("gc", "Calendar_gc", null), values[0]);
            Assert.NotEqual(new ValueSetEntry("gc", null, "Calendar_gc"), values[0]);
        }

        /// <summary>A default entity with two properties that draw from a set, one of them overridable.</summary>
        private class Default
        {
            /// <summary>A zone.</summary>
            [ValueSet(CoreValueSets.TimeZone)]
            public string? Zone { get; set; }

            /// <summary>A region.</summary>
            [ValueSet(CoreValueSets.Region)]
            public virtual string? Region { get; set; }
        }

        /// <summary>A leaf that overrides one of them.</summary>
        private sealed class Leaf : Default
        {
            /// <inheritdoc />
            public override string? Region { get; set; }
        }
    }
}
