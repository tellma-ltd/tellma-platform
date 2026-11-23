// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Reports;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Sharing;
using Tellma.Core.Tests.Crud.Query;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Sharing
{
    /// <summary>
    ///     A shareable's bespoke criteria and its shared-with-me filter are constant trees: the owner
    ///     reads, saves, deletes, activates and shares; a grant that reaches the caller reads, and
    ///     saves under Edit; every criterion carries the share reason.
    /// </summary>
    public sealed class ShareableAccessCriteriaTests
    {
        /// <summary>The grants that reach the caller, as the trees render them.</summary>
        private static readonly string Grantee =
            "or(UserId is null and RoleId is null | UserId = me() | via Role(and(IsActive = true | or(IsPublic = true | exists core.RoleMembership.Role(and(UserId = me() | "
            + QueryRig.Render(AccessFilters.CurrentMembership) + "))))))";

        [Fact]
        public void The_reports_criteria_are_constant_trees_with_the_share_reason()
        {
            using ServiceProvider services = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);

            IAccessCriteriaProvider provider = Assert.Single(services.GetServices<IAccessCriteriaProvider>(), static candidate => candidate.Resource == "core.Report");

            Assert.Equal(
                [
                    (AccessActions.Read, $"or(OwnerId = me() | exists core.ReportShare.Report({Grantee}))", "share"),
                    (AccessActions.Save, $"or(OwnerId = me() | exists core.ReportShare.Report(and(Access = 'Edit' | {Grantee})))", "share"),
                    (AccessActions.Delete, "OwnerId = me()", "share"),
                    (AccessActions.Activate, "OwnerId = me()", "share"),
                    (AccessActions.Share, "OwnerId = me()", "share"),
                ],
                provider.Criteria.Select(static criterion => (criterion.Action, QueryRig.Render(criterion.Filter), criterion.Reason)));
        }

        [Fact]
        public void The_shared_with_me_filter_takes_rows_the_caller_does_not_own_that_a_grant_reaching_them_names()
        {
            using ServiceProvider services = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            SharedWithMeFilters<Report, ReportShare> filters = new(services.GetRequiredService<IEntityMetadataProvider>());

            Assert.Equal(
                $"and(or(OwnerId is null | OwnerId != me()) | exists core.ReportShare.Report({Grantee}))",
                QueryRig.Render(Assert.Single(filters.NamedFilters, static pair => pair.Key == ShareableNames.SharedWithMeFilter).Value));
        }
    }
}
