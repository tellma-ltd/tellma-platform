// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Crud;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Sharing
{
    // Spec 0033 §5.2's shares extra. Only the share dialog asks for it, so readers of a row never see
    // who else holds it. It is one read rooted at the share entity over the visible ids, whose
    // grantee and sharer columns join the read's related entities; both decisions denied cost no
    // statement. When neither the Share nor the Publish decision is unrestricted, the read itself
    // carries their filters through the grant's row navigation, so a row they do not admit
    // contributes no grant and, above all, no grantee to the related entities. A filtered read cannot
    // tell an admitted row with no grants from a row it does not admit, so under filtered decisions
    // only the rows holding grants have entries; the share dialog reads a missing entry as no grants,
    // since its own abilities come from the permissions extra.
    /// <summary>
    ///     Produces a shareable stack's <c>shares</c> details extra: the grants of each visible row the
    ///     caller's Share or Publish decision admits (every visible row under an unrestricted decision;
    ///     otherwise each admitted row that holds a grant), the user grants, then the role grants, then
    ///     the Everyone grant, each group by grant id.
    /// </summary>
    /// <typeparam name="TEntity">The shareable entity.</typeparam>
    /// <typeparam name="TShare">The share entity.</typeparam>
    /// <param name="schemas">The model's query schemas, which the reads are rooted in.</param>
    /// <param name="metadata">The model's entity metadata.</param>
    /// <param name="stacks">The stack registry, whose related selects project the grantees and sharers.</param>
    [DetailsExtras(ShareableNames.SharesExtra)]
    internal sealed class SharesContributor<TEntity, TShare>(QueryexSchemaProvider schemas, IEntityMetadataProvider metadata, IStackRegistry stacks)
        : IDetailsContributor<TEntity>
        where TEntity : TopLevelEntity, IShareable
        where TShare : Entity<int>, IShareRow
    {
        /// <summary>Adds the extra when the read includes it.</summary>
        /// <param name="plan">The details plan.</param>
        /// <param name="requested">The extras the read includes that this contributor declares.</param>
        public void Contribute(DetailsPlan<TEntity> plan, IReadOnlySet<string> requested)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(requested);
            if (!requested.Contains(ShareableNames.SharesExtra))
            {
                return;
            }

            // --- The two decisions: both denied answer an empty list, with no statement ---
            var shape = ShareableShape.Of(metadata, typeof(TEntity), typeof(TShare));
            AccessDecision share = plan.Decide(shape.Entity.Name, AccessActions.Share);
            AccessDecision publish = plan.Decide(shape.Entity.Name, AccessActions.Publish);
            AccessDecision[] admitting = [.. new[] { share, publish }.Where(static decision => decision.Outcome != AccessOutcome.Denied)];
            if (admitting.Length == 0)
            {
                plan.AddComputedExtra<IReadOnlyList<ShareList>>(ShareableNames.SharesExtra, static _ => []);
                return;
            }

            TenantSettings settings = plan.Context.TenantSettings
                ?? throw new InvalidOperationException("The read's context carries no tenant settings, so the shares extra has no schema to compose under.");
            QueryexSchemaMap map = schemas.Map(settings.Shape);
            string row = shape.Row.ForeignKey.Name;

            // --- The admission: none under an unrestricted decision, else the decisions' filters on the grant's row ---
            bool unrestricted = admitting.Any(static decision => decision.Outcome == AccessOutcome.Unrestricted);
            FilterTree? admission = unrestricted
                ? null
                : FilterTree.Via(shape.Row.Navigation, admitting.Length == 1 ? admitting[0].Filter! : FilterTree.Or([.. admitting.Select(static decision => decision.Filter!)]));

            // --- The grants of the visible rows, with their grantees and sharers as related entities ---
            QueryexSchema schema = map.Schema;
            List<string> columns =
            [
                nameof(Entity<>.Id),
                row,
                nameof(IShareRow.UserId),
                nameof(IShareRow.RoleId),
                nameof(IShareRow.Access),
                nameof(IShareRow.SharedById),
                nameof(IShareRow.SharedAt),
                .. Related(shape.Share, ShareableShape.UserNavigation, schema),
                .. Related(shape.Share, ShareableShape.RoleNavigation, schema),
                .. Related(shape.Share, ShareableShape.SharedByNavigation, schema),
            ];
            QuerySpec grants = new()
            {
                Root = map.Descriptor(shape.Share),
                Select = string.Join(", ", columns),
                Filter = admission,
                OrderBy = $"{row}, {nameof(Entity<>.Id)}",
                Restrictions = [new KeySetRestriction(row, plan.IdsSource.Name)],
            };
            plan.AddComputedExtra<TShare, IReadOnlyList<ShareList>>(
                ShareableNames.SharesExtra,
                grants,
                arguments: null,
                (visible, rows) => Lists(shape, visible, rows, everyVisible: unrestricted));
        }

        /// <summary>The entries of the admitted visible rows, in request order, each with its grants in the dialog's order.</summary>
        /// <param name="shape">The shareable's shape.</param>
        /// <param name="visible">The visible rows, in request order.</param>
        /// <param name="rows">The admitted grants of the visible rows.</param>
        /// <param name="everyVisible">Whether every visible row is admitted, so a row with no grants still has an entry.</param>
        /// <returns>The entries.</returns>
        internal static IReadOnlyList<ShareList> Lists(ShareableShape shape, IReadOnlyList<TEntity> visible, IReadOnlyList<TShare> rows, bool everyVisible)
        {
            ArgumentNullException.ThrowIfNull(shape);
            ArgumentNullException.ThrowIfNull(visible);
            ArgumentNullException.ThrowIfNull(rows);
            ILookup<int, TShare> byRow = rows.ToLookup(share => (int)shape.Row.ForeignKey.Getter(share)!);
            List<ShareList> lists = [];
            foreach (TEntity entity in visible)
            {
                if (!everyVisible && !byRow.Contains(entity.Id))
                {
                    continue;
                }

                // Users, then roles, then Everyone; each group by grant id.
                ShareGrantView[] grants = [.. byRow[entity.Id]
                    .OrderBy(static grant => grant.UserId is not null ? 0 : grant.RoleId is not null ? 1 : 2)
                    .ThenBy(static grant => grant.Id)
                    .Select(static grant => new ShareGrantView(grant.Id, grant.UserId, grant.RoleId, grant.Access, grant.SharedById, grant.SharedAt))];
                lists.Add(new ShareList(entity.Id, grants));
            }

            return lists;
        }

        /// <summary>The related columns of one of the share's navigations, prefixed with the navigation.</summary>
        /// <param name="share">The share entity's metadata.</param>
        /// <param name="navigation">The navigation.</param>
        /// <param name="schema">The query schema the read runs under.</param>
        /// <returns>The columns.</returns>
        private IEnumerable<string> Related(EntityMetadata share, string navigation, QueryexSchema schema)
        {
            ReferenceMetadata reference = share.References.FirstOrDefault(candidate => string.Equals(candidate.Navigation, navigation, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"The share entity {share.Name} has no navigation {navigation}.");
            return RelatedProjections.Of(stacks, reference.Target, schema).Select(column => $"{navigation}.{column}");
        }
    }
}
