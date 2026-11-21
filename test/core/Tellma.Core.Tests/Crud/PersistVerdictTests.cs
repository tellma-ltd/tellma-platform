// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Calendars;
using Tellma.Core.Crud;

namespace Tellma.Core.Tests.Crud
{
    /// <summary>
    ///     The tenant-state verdict a persist takes before its batch is composed: a frozen tenant
    ///     refuses every persist, a read-only one a mutation that does not allow the state, unless a
    ///     frame the persist is nested in admits it; an API action frame judges by its own descriptor.
    /// </summary>
    public sealed class PersistVerdictTests
    {
        /// <summary>The instant the contexts are created at.</summary>
        private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The frames a verdict may be taken under, by name.</summary>
        private static readonly Dictionary<string, WriteFrame[]> Frames = new(StringComparer.Ordinal)
        {
            ["none"] = [],
            ["read-only-safe action"] = [new WriteFrame(WriteHostKind.ApiAction, "ps.Doc:print", AdmitsReadOnly: true)],
            ["mutating action"] = [new WriteFrame(WriteHostKind.ApiAction, "ps.Doc:post", AdmitsReadOnly: false)],
            ["read-only job"] = [new WriteFrame(WriteHostKind.Job, "ps.reindex", AdmitsReadOnly: true)],
            ["job"] = [new WriteFrame(WriteHostKind.Job, "ps.recost", AdmitsReadOnly: false)],
            ["provisioning"] = [new WriteFrame(WriteHostKind.Provisioning, "tenant:seed", AdmitsReadOnly: true)],
        };

        [Theory]
        [InlineData(TenantState.Frozen, true, false, "none", TenantProblemCodes.Frozen)]
        [InlineData(TenantState.Frozen, false, true, "none", TenantProblemCodes.Frozen)]
        [InlineData(TenantState.Frozen, true, false, "provisioning", TenantProblemCodes.Frozen)]
        [InlineData(TenantState.Frozen, true, false, "read-only job", TenantProblemCodes.Frozen)]
        [InlineData(TenantState.ReadOnly, true, false, "none", TenantProblemCodes.ReadOnly)]
        [InlineData(TenantState.ReadOnly, true, true, "none", null)]
        [InlineData(TenantState.ReadOnly, false, false, "none", null)]
        [InlineData(TenantState.ReadOnly, true, false, "read-only-safe action", null)]
        [InlineData(TenantState.ReadOnly, false, true, "mutating action", TenantProblemCodes.ReadOnly)]
        [InlineData(TenantState.ReadOnly, true, false, "read-only job", null)]
        [InlineData(TenantState.ReadOnly, true, false, "job", TenantProblemCodes.ReadOnly)]
        [InlineData(TenantState.ReadOnly, true, false, "provisioning", null)]
        [InlineData(TenantState.Active, true, false, "none", null)]
        [InlineData(TenantState.Active, true, false, "mutating action", null)]
        public void A_persist_is_refused_or_admitted_by_the_tenants_state_the_operation_and_the_frames_it_is_nested_in(
            TenantState state, bool mutation, bool allowWhenReadOnly, string frames, string? refused)
        {
            RequestContext context = Context(state);

            if (refused is null)
            {
                PersistVerdict.Ensure(context, mutation, allowWhenReadOnly, Frames[frames]);
                return;
            }

            TenantUnavailableException refusal = Assert.Throws<TenantUnavailableException>(() => PersistVerdict.Ensure(context, mutation, allowWhenReadOnly, Frames[frames]));
            Assert.Equal((refused, 1, state), (refusal.Code, refusal.TenantId, refusal.State));

            // Waiting does not change a state an operator set.
            Assert.Null(refusal.RetryAfter);
        }

        [Fact]
        public void A_tenantless_scope_takes_no_verdict()
        {
            PersistVerdict.Ensure(RequestContext.Tenantless(new GregorianCalendarSystem(), Now), mutation: true, allowWhenReadOnly: false, []);
        }

        [Fact]
        public void A_frame_is_left_when_its_scope_is_disposed_and_the_pipelines_own_operation_opens_none()
        {
            WriteFrames frames = new();
            WriteFrame action = new(WriteHostKind.ApiAction, "ps.Doc:post", AdmitsReadOnly: false);
            WriteFrame job = new(WriteHostKind.Job, "ps.reindex", AdmitsReadOnly: true);

            using (frames.Enter(action))
            {
                using (frames.Enter(job))
                {
                    Assert.Equal([action, job], frames.Open);
                }

                Assert.Equal([action], frames.Open);
            }

            Assert.Empty(frames.Open);
            _ = Assert.Throws<ArgumentException>(() => frames.Enter(new WriteFrame(WriteHostKind.Pipeline, "ps.Doc:save", AdmitsReadOnly: false)));
        }

        /// <summary>A caller's context in a tenant of a state.</summary>
        /// <param name="state">The tenant's state.</param>
        /// <returns>The context.</returns>
        private static RequestContext Context(TenantState state)
        {
            return RequestContext.Tenantless(new GregorianCalendarSystem(), Now) with
            {
                Tenant = new TenantDescriptor(1, "Acme", null, null, ["en"], TenantCategory.Live, state, null),
            };
        }
    }
}
