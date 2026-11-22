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
    // who else holds it. It answers each visible row the caller's Share or Publish decision admits,
    // with its grants, and nothing for any other row; both decisions denied cost no statement. The
    // grants are one read rooted at the share entity over the visible ids, whose grantee and sharer
    // columns join the read's related entities. When neither decision is unrestricted, a key read
    // under the two decisions' filters says which visible rows are admitted, in the same batch.
    /// <summary>
    ///     Produces a shareable stack's <c>shares</c> details extra: each admitted row's grants, the
    ///     user grants, then the role grants, then the Everyone grant, each group by grant id.
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

            // --- Which visible rows the decisions admit: all of them under an unrestricted one ---
            BatchResult<RowPage>? admitted = null;
            if (!admitting.Any(static decision => decision.Outcome == AccessOutcome.Unrestricted))
            {
                FilterTree filter = admitting.Length == 1 ? admitting[0].Filter! : FilterTree.Or([.. admitting.Select(static decision => decision.Filter!)]);
                admitted = plan.AddRead(
                    new QuerySpec
                    {
                        Root = map.Descriptor(shape.Entity),
                        Select = shape.Entity.Key.Name,
                        Filter = filter,
                        Restrictions = [new KeySetRestriction(shape.Entity.Key.Name, plan.IdsSource.Name)],
                    },
                    arguments: null);
            }

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
                OrderBy = $"{row}, {nameof(Entity<>.Id)}",
                Restrictions = [new KeySetRestriction(row, plan.IdsSource.Name)],
            };
            plan.AddComputedExtra<TShare, IReadOnlyList<ShareList>>(
                ShareableNames.SharesExtra,
                grants,
                arguments: null,
                (visible, rows) => Lists(shape, visible, rows, admitted is null ? null : Keys(admitted.Value.Rows)));
        }

        /// <summary>The entries of the admitted visible rows, in request order, each with its grants in the dialog's order.</summary>
        /// <param name="shape">The shareable's shape.</param>
        /// <param name="visible">The visible rows, in request order.</param>
        /// <param name="rows">The grants of the visible rows.</param>
        /// <param name="admitted">The admitted rows' ids; null when every visible row is admitted.</param>
        /// <returns>The entries.</returns>
        internal static IReadOnlyList<ShareList> Lists(ShareableShape shape, IReadOnlyList<TEntity> visible, IReadOnlyList<TShare> rows, IReadOnlySet<int>? admitted)
        {
            ArgumentNullException.ThrowIfNull(shape);
            ArgumentNullException.ThrowIfNull(visible);
            ArgumentNullException.ThrowIfNull(rows);
            ILookup<int, TShare> byRow = rows.ToLookup(share => (int)shape.Row.ForeignKey.Getter(share)!);
            List<ShareList> lists = [];
            foreach (TEntity entity in visible)
            {
                if (admitted is not null && !admitted.Contains(entity.Id))
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

        /// <summary>The ids of a key read's first column.</summary>
        /// <param name="rows">The rows.</param>
        /// <returns>The ids.</returns>
        private static HashSet<int> Keys(QueryRowSet rows)
        {
            HashSet<int> keys = [];
            int[] buffer = (int[])rows.GetBuffer(0);
            for (int index = 0; index < rows.RowCount; index++)
            {
                if (!rows.IsNull(0, index))
                {
                    _ = keys.Add(buffer[index]);
                }
            }

            return keys;
        }
    }
}
