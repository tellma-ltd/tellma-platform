// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Users
{
    // The service is spec 0018 §4's RoleService: spec 0014 §3.7's duplicate-permission collapse and
    // §4.3's two details extras, which the role page reads in the details read's one round trip. The
    // access rules of spec 0014 §8.4 are a separate component, so a distribution leaf cannot drop them.
    /// <summary>
    ///     The roles' stack service: the standard operations over the tenant's roles and their
    ///     permissions, closed over the distribution's role leaf, and two details extras for the
    ///     role's page: its members and its drifted permissions. The access rules that keep the
    ///     roles' writes safe run beside it as a component of the stack.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A save collapses each role's exact duplicate permissions (the same resource, action and
    ///         filter) to one row before they are validated, counted and stored. A leaf that overrides
    ///         <see cref="PreprocessAsync" /> calls this one to keep it.
    ///     </para>
    ///     <para>
    ///         The <c>members</c> extra lists the memberships of the visible roles that have not ended,
    ///         current and scheduled, by user name, at most <see cref="MaxMembers" /> rows, each with
    ///         its user's display columns; only the users the caller may read are listed, and a caller
    ///         who may read no user gets no extra. The <c>problems</c> extra lists each permission of
    ///         the visible roles that grants nothing, with why, computed after the read from the
    ///         permissions it loaded. A leaf that overrides <see cref="ContributeDetails" /> calls this
    ///         one to keep them.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TRole">The stack's role leaf.</typeparam>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    /// <param name="dependencies">What the service needs from the platform beyond its pipeline.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    [DetailsExtras(MembersExtra, ProblemsExtra)]
    public class RoleService<TRole>(IEntityPipelineFactory factory, RoleServiceDependencies dependencies) : EntityService<TRole>(factory)
        where TRole : Role
    {
        /// <summary>The name of the details extra that lists the roles' members.</summary>
        public const string MembersExtra = "members";

        /// <summary>The name of the details extra that lists the roles' drifted permissions.</summary>
        public const string ProblemsExtra = "problems";

        /// <summary>The most rows the members extra carries.</summary>
        public const int MaxMembers = 500;

        /// <summary>The membership's navigation to its user.</summary>
        private const string MembershipUser = "User";

        /// <summary>The user columns a member row carries when the tenant's schema declares them, in order.</summary>
        private static readonly string[] MemberUserColumns =
        [
            nameof(User.Name),
            nameof(User.Name2),
            nameof(User.Name3),
            nameof(User.Email),
            nameof(User.ImageId),
            nameof(User.IsActive),
            nameof(User.State),
        ];

        /// <summary>
        ///     The memberships that have not ended at the server's time, current or scheduled: the end
        ///     half of the window a membership grants in, the same bound the current-membership filter
        ///     tests.
        /// </summary>
        private static readonly FilterTree NotEnded = FilterTree.Leaf("EndsAt is null or EndsAt > now()");

        /// <summary>What the service needs from the platform beyond its pipeline.</summary>
        private readonly RoleServiceDependencies _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

        /// <summary>Preprocesses a save: the base's preprocessing, then each role's exact duplicate permissions collapsed to one row.</summary>
        /// <param name="context">The save's context; its rows have no final ids yet.</param>
        /// <returns>A task that completes when the payload is preprocessed.</returns>
        protected override async Task PreprocessAsync(SaveContext<TRole> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.PreprocessAsync(context).ConfigureAwait(false);
            foreach (TRole role in context.Entities)
            {
                _ = PermissionCollapse.Collapse(role);
            }
        }

        /// <summary>
        ///     Adds the requested extras: the members as one query over the visible roles, or no
        ///     extra when the caller may read no user, and the problems as a computed extra over the
        ///     permissions the read loads.
        /// </summary>
        /// <param name="plan">The details plan.</param>
        /// <exception cref="InvalidOperationException">The caller's filter on the users exceeds the compile ceilings.</exception>
        protected override void ContributeDetails(DetailsPlan<TRole, int> plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            base.ContributeDetails(plan);
            if (plan.Request.Include.Contains(MembersExtra, StringComparer.Ordinal))
            {
                AddMembers(plan);
            }

            if (plan.Request.Include.Contains(ProblemsExtra, StringComparer.Ordinal))
            {
                // --- The schema of the read's own context, captured now: the computation runs after the batch ---
                QueryexSchema schema = Map(plan.Context).Schema;
                PermissionClassifier classifier = _dependencies.Classifier;
                plan.AddComputedExtra(ProblemsExtra, roles => RoleProblems.Of(classifier, roles, schema));
            }
        }

        /// <summary>The query schema map of the read's tenant shape.</summary>
        /// <param name="context">The read's context.</param>
        /// <returns>The map.</returns>
        /// <exception cref="InvalidOperationException">The context carries no tenant settings.</exception>
        private QueryexSchemaMap Map(RequestContext context)
        {
            TenantSettings settings = context.TenantSettings
                ?? throw new InvalidOperationException("The read's context carries no tenant settings, so the roles' extras have no schema to compose under.");
            return _dependencies.Schemas.Map(settings.Shape);
        }

        /// <summary>
        ///     Adds the members extra: the unended memberships of the visible roles whose users the
        ///     caller may read, by user name, at most <see cref="MaxMembers" /> rows; or omits it when
        ///     the caller may read no user.
        /// </summary>
        /// <param name="plan">The details plan.</param>
        /// <exception cref="InvalidOperationException">The caller's filter on the users exceeds the compile ceilings.</exception>
        private void AddMembers(DetailsPlan<TRole, int> plan)
        {
            // --- The caller's read of the users, from the set the read is composed under ---
            AccessDecision users = plan.Decide(CoreResources.User, AccessActions.Read);
            if (!users.IsAllowed)
            {
                plan.OmitExtra(MembersExtra);
                return;
            }

            // --- The projection: the membership, then the user columns the tenant's shape declares ---
            QueryexSchemaMap map = Map(plan.Context);
            EntityMetadata membership = _dependencies.Metadata.Get(typeof(RoleMembership));
            HashSet<string> declared = new(map.Declared(_dependencies.Metadata.Get(typeof(User))).Select(static property => property.Name), StringComparer.Ordinal);
            List<string> columns =
            [
                nameof(RoleMembership.Id),
                nameof(RoleMembership.RoleId),
                nameof(RoleMembership.UserId),
                .. MemberUserColumns.Where(declared.Contains).Select(static column => $"{MembershipUser}.{column}"),
                nameof(RoleMembership.StartsAt),
                nameof(RoleMembership.EndsAt),
                nameof(RoleMembership.Notes),
            ];

            // --- The rows: the visible roles' unended memberships, each user under the caller's own
            // read of the users rebased through the membership's user, so no hidden user is listed ---
            FilterTree filter = users.Filter is FilterTree visible
                ? FilterTree.And([NotEnded, FilterTree.Via(MembershipUser, visible)])
                : NotEnded;
            try
            {
                plan.AddExtra(
                    MembersExtra,
                    new QuerySpec
                    {
                        Root = map.Descriptor(membership),
                        Select = string.Join(", ", columns),
                        Filter = filter,
                        OrderBy = $"{MembershipUser}.{nameof(User.Name)}, {nameof(RoleMembership.UserId)}, {nameof(RoleMembership.Id)}",
                        Take = MaxMembers,
                        Restrictions = [new KeySetRestriction(nameof(RoleMembership.RoleId), plan.IdsSource.Name)],
                    },
                    arguments: null);
            }
            catch (ArgumentException tooComplex) when (users.Outcome == AccessOutcome.Filtered)
            {
                // An administrator's configuration problem: a plain error, never a silent denial.
                string involved = string.Join(", ", users.Grants.OfType<RoleAccessGrant>().Select(static grant => grant.RoleId).Distinct().Order());
                AccessLog.FilterTooComplex(_dependencies.Logger, tooComplex, plan.Context.Tenant?.Id ?? 0, plan.Context.UserId ?? 0, users.Resource, users.Action, involved);
                throw new InvalidOperationException("The caller's filter on the users exceeded the compile ceilings, so the role's members cannot be read.", tooComplex);
            }
        }
    }
}
