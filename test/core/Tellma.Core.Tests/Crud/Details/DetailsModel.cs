// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Composition;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Crud.Details
{
    /// <summary>
    ///     The details model: an order with lines that point at customers, a service that declares
    ///     extras, refuses on record and declares entity actions, a contributor with an extra of its
    ///     own, a cacheable customer list, and a tree with low ceilings.
    /// </summary>
    internal static class DetailsModel
    {
        /// <summary>The order's resource.</summary>
        public const string Orders = "dm.DmOrder";

        /// <summary>The customer's resource.</summary>
        public const string Customers = "dm.DmCustomer";

        /// <summary>The node's resource.</summary>
        public const string Nodes = "dm.DmNode";

        /// <summary>Composes the model's feature.</summary>
        /// <param name="tellma">The builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.D>("details", contribute: static contribution => contribution
                .Entity<DmOrder, DmOrderService>()
                .Entity<DmCustomer>()
                .Entity<DmNode>()
                .DetailsContributor<DmOrder, DmOrderLineage>()));
        }
    }

    /// <summary>An order: activatable, pointing at a customer, with lines; its ceilings are low so a test reaches each.</summary>
    [Table("Orders", Schema = "dm")]
    [TableType]
    [Stack(MaxIds = 3, MaxExtras = 4)]
    public class DmOrder : TopLevelEntity, IActivatable
    {
        /// <summary>The code; one starting with <c>P</c> is posted.</summary>
        [Column(TypeName = "varchar(50)")]
        public string Code { get; set; } = null!;

        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>The customer.</summary>
        [References(typeof(DmCustomer))]
        public int? CustomerId { get; set; }

        /// <inheritdoc />
        public bool IsActive { get; set; } = true;

        /// <summary>The lines.</summary>
        [NotMapped]
        public List<DmOrderLine>? Lines { get; set; }
    }

    /// <summary>An order's line, pointing at the customers it ships to and bills.</summary>
    [Table("OrderLines", Schema = "dm")]
    [TableType]
    public class DmOrderLine : ChildEntity
    {
        /// <summary>The order.</summary>
        [ParentKey]
        public int OrderId { get; set; }

        /// <summary>The customer the line ships to.</summary>
        [References(typeof(DmCustomer))]
        public int? ShipToId { get; set; }

        /// <summary>The customer the line bills.</summary>
        [References(typeof(DmCustomer))]
        public int? BillToId { get; set; }

        /// <summary>The amount.</summary>
        [Column(TypeName = "decimal(19,4)")]
        public decimal? Amount { get; set; }
    }

    /// <summary>A customer: a small cacheable list.</summary>
    [Table("Customers", Schema = "dm")]
    [TableType]
    [Cacheable(MaxRows = 50)]
    public class DmCustomer : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>A node of a tree whose ceilings are low so a test reaches each.</summary>
    [Table("Nodes", Schema = "dm")]
    [TableType]
    [Stack(MaxTake = 5, MaxIds = 3)]
    public class DmNode : TreeEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }

    /// <summary>
    ///     The order's service: three extras of each kind a service may add, a rule that refuses a
    ///     posted order's save and delete, and two entity actions, one all-or-nothing.
    /// </summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="database">The tenant's database, whose schema roots the query extra.</param>
    [DetailsExtras(Totals, Raw, Names)]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An action is an instance method by contract; the registry refuses a static one.")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The signature is what the model declares; the details tests never run an action.")]
    public sealed class DmOrderService(IEntityPipelineFactory factory, ITenantDatabase database) : EntityService<DmOrder>(factory)
    {
        /// <summary>A computed extra: the visible orders' codes.</summary>
        public const string Totals = "totals";

        /// <summary>A raw extra over the visible ids.</summary>
        public const string Raw = "raw";

        /// <summary>A query extra over the visible ids.</summary>
        public const string Names = "names";

        /// <summary>The reason a posted order refuses a save or a delete.</summary>
        public const string Posted = "posted";

        /// <summary>The filter of the query extra.</summary>
        public const string NamesFilter = "Name != 'names'";

        /// <summary>What the service's contribution does instead of adding each requested extra; null adds each.</summary>
        public Action<DetailsPlan<DmOrder, int>>? Contribution { get; set; }

        /// <summary>The plans the service was handed, in order.</summary>
        public List<DetailsPlan<DmOrder, int>> Plans { get; } = [];

        /// <summary>Posts the orders.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task.</returns>
        [EntityAction("post")]
        public Task PostAsync(ActionContext<DmOrder, int> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>Reprices the orders, all or nothing.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task.</returns>
        [EntityAction("reprice", SupportsFilter = false)]
        public Task RepriceAsync(ActionContext<DmOrder, int> context)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override void ContributeDetails(DetailsPlan<DmOrder, int> plan)
        {
            Plans.Add(plan);
            if (Contribution is not null)
            {
                Contribution(plan);
                return;
            }

            // --- Each requested extra, one of each kind ---
            if (plan.Request.Include.Contains(Totals))
            {
                plan.AddComputedExtra(Totals, static rows => rows.Select(static row => row.Code).ToArray());
            }

            if (plan.Request.Include.Contains(Raw))
            {
                plan.AddExtra(Raw, $"SELECT I.[Id] FROM {plan.IdsSource} AS I", new SqlOptions(resultSets: 1));
            }

            if (plan.Request.Include.Contains(Names))
            {
                plan.AddExtra(
                    Names,
                    new QuerySpec { Root = database.Schema.FindEntity(DetailsModel.Orders)!, Select = "Id, Name", Filter = FilterTree.Leaf(NamesFilter), Restrictions = [new KeySetRestriction("Id", plan.IdsSource.Name)] },
                    arguments: null);
            }
        }

        /// <inheritdoc />
        protected override string? RefuseOnRecord(DmOrder row, string ability)
        {
            return row.Code.StartsWith('P') && ability is "save" or "delete" ? Posted : null;
        }
    }

    /// <summary>The order's lineage: an extra a contributor adds.</summary>
    [DetailsExtras(Lineage)]
    public sealed class DmOrderLineage : IDetailsContributor<DmOrder>
    {
        /// <summary>The contributor's extra: the visible orders' ids.</summary>
        public const string Lineage = "lineage";

        /// <summary>What the contribution does instead of adding the extra; null adds it.</summary>
        public Action<DetailsPlan<DmOrder>>? Contribution { get; set; }

        /// <summary>The extras the contributor was asked for, per call.</summary>
        public List<IReadOnlySet<string>> Requested { get; } = [];

        /// <inheritdoc />
        public void Contribute(DetailsPlan<DmOrder> plan, IReadOnlySet<string> requested)
        {
            Requested.Add(requested);
            if (Contribution is not null)
            {
                Contribution(plan);
                return;
            }

            plan.AddComputedExtra(Lineage, static rows => rows.Select(static row => row.Id).ToArray());
        }
    }
}
