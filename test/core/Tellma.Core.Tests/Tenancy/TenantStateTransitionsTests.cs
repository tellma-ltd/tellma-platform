// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>The tenant state table, every pair of states, the illegal ones included.</summary>
    public class TenantStateTransitionsTests
    {
        /// <summary>The legal transitions, and only those.</summary>
        private static readonly HashSet<(TenantState From, TenantState To)> Legal =
        [
            (TenantState.Active, TenantState.ReadOnly),
            (TenantState.ReadOnly, TenantState.Active),
            (TenantState.Active, TenantState.Frozen),
            (TenantState.ReadOnly, TenantState.Frozen),
            (TenantState.Suspended, TenantState.Frozen),
            (TenantState.Frozen, TenantState.Active),
            (TenantState.Frozen, TenantState.ReadOnly),
            (TenantState.Frozen, TenantState.Suspended),
            (TenantState.Active, TenantState.Suspended),
            (TenantState.ReadOnly, TenantState.Suspended),
            (TenantState.Suspended, TenantState.Active),
            (TenantState.Provisioning, TenantState.Active),
            (TenantState.Provisioning, TenantState.Retired),
            (TenantState.Active, TenantState.Retired),
            (TenantState.ReadOnly, TenantState.Retired),
            (TenantState.Frozen, TenantState.Retired),
            (TenantState.Suspended, TenantState.Retired),
        ];

        /// <summary>Every ordered pair of states.</summary>
        public static TheoryData<TenantState, TenantState> Pairs
        {
            get
            {
                TheoryData<TenantState, TenantState> pairs = [];
                foreach (TenantState from in Enum.GetValues<TenantState>())
                {
                    foreach (TenantState to in Enum.GetValues<TenantState>())
                    {
                        pairs.Add(from, to);
                    }
                }

                return pairs;
            }
        }

        [Theory]
        [MemberData(nameof(Pairs))]
        public void A_transition_is_legal_exactly_when_the_state_table_lists_it(TenantState from, TenantState to)
        {
            Assert.Equal(Legal.Contains((from, to)), TenantStateTransitions.IsLegal(from, to));
        }

        [Fact]
        public void Retired_is_terminal_and_no_state_moves_to_itself()
        {
            Assert.All(Enum.GetValues<TenantState>(), static state =>
            {
                Assert.False(TenantStateTransitions.IsLegal(TenantState.Retired, state));
                Assert.False(TenantStateTransitions.IsLegal(state, state));
            });
        }

        [Fact]
        public void Provisioning_is_left_only_for_active_or_retired_and_never_reentered()
        {
            Assert.All(Enum.GetValues<TenantState>(), static state =>
                Assert.False(TenantStateTransitions.IsLegal(state, TenantState.Provisioning)));
            Assert.False(TenantStateTransitions.IsLegal(TenantState.Provisioning, TenantState.Frozen));
            Assert.False(TenantStateTransitions.IsLegal(TenantState.Provisioning, TenantState.ReadOnly));
            Assert.False(TenantStateTransitions.IsLegal(TenantState.Suspended, TenantState.ReadOnly));
        }
    }
}
