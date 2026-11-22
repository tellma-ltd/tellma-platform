// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Queryex;
using Tellma.Distro.Acme.Entities;

namespace Tellma.Distro.Acme.Services
{
    // Spec 0018 §1.4's related-documents extra, as spec 0031's invoice page shows it in its aside. The
    // extra reads invoices, so it reads them under the caller's own Read on invoices, decided from the
    // set the details read is composed under: the listed rows, and the targets whose customers they
    // share, are rows the caller may read. A key-set restriction binds the root only, so the targets
    // reach the semi-join through the requested ids as parameters, each admitted again by the read
    // filter; an invisible target therefore contributes nothing.
    /// <summary>
    ///     Produces the sales invoices' <c>related-documents</c> details extra: the other invoices of the
    ///     customers of the invoices read, latest first (by posting date, then by id), at most
    ///     <see cref="MaxRows" />, each with its id, serial number, posting date and customer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The invoices read are never listed, and neither is an invoice the caller may not read;
    ///         a read of several invoices answers one list of at most <see cref="MaxRows" /> rows over
    ///         all their customers, which each row's customer tells apart.
    ///     </para>
    ///     <para>
    ///         The extra is omitted when the caller may not read invoices at all, and when the read
    ///         names more than <see cref="MaxTargets" /> invoices.
    ///     </para>
    /// </remarks>
    /// <param name="schemas">The model's query schemas, which the extra's query is rooted in.</param>
    [DetailsExtras(RelatedDocumentsExtra)]
    public sealed class RelatedDocumentsContributor(IQueryexSchemaProvider schemas) : IDetailsContributor<SalesInvoice>
    {
        /// <summary>The name of the extra.</summary>
        public const string RelatedDocumentsExtra = "related-documents";

        /// <summary>The most rows the extra lists.</summary>
        public const int MaxRows = 10;

        /// <summary>The most invoices a read may name and still carry the extra: one list of parameters, well within the query language's ceilings.</summary>
        public const int MaxTargets = 128;

        /// <summary>The invoices' query name, the extra's root and the semi-join's entity.</summary>
        private const string Invoices = "acme.SalesInvoice";

        /// <summary>The invoice's navigation to its customer, which the semi-join runs through.</summary>
        private const string CustomerNavigation = "Customer";

        /// <summary>Adds the extra when the read includes it: one query in the details batch, writing nothing.</summary>
        /// <param name="plan">The details plan.</param>
        /// <param name="requested">The extras the read includes that this contributor declares.</param>
        /// <exception cref="InvalidOperationException">
        ///     The plan carries no requested ids or no tenant settings, or the caller's filter on the
        ///     invoices exceeds the query language's ceilings.
        /// </exception>
        public void Contribute(DetailsPlan<SalesInvoice> plan, IReadOnlySet<string> requested)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(requested);
            if (!requested.Contains(RelatedDocumentsExtra))
            {
                return;
            }

            // --- The caller's read of the invoices, from the set the read is composed under ---
            AccessDecision read = plan.Decide(Invoices, AccessActions.Read);
            IReadOnlyList<int> targets = plan is DetailsPlan<SalesInvoice, int> keyed
                ? keyed.Ids
                : throw new InvalidOperationException("The sales invoices' details plan carries no requested ids, so their related documents cannot be found.");
            if (!read.IsAllowed || targets.Count > MaxTargets)
            {
                plan.OmitExtra(RelatedDocumentsExtra);
                return;
            }

            // --- The targets as parameters: an empty read names one id no row has, so the extra is empty ---
            QueryArguments arguments = new();
            int[] ids = targets.Count == 0 ? [0] : [.. targets.Distinct()];
            StringBuilder list = new();
            for (int index = 0; index < ids.Length; index++)
            {
                string name = "t" + index.ToString(CultureInfo.InvariantCulture);
                _ = list.Append(index == 0 ? "Id in (@" : ", @").Append(name);
                _ = arguments.Add(new QueryexParameterDeclaration(name, QueryexType.QxNumeric, IsNotNull: true), (decimal)ids[index]);
            }

            var isTarget = FilterTree.Leaf(list.Append(')').ToString());

            // --- The rows: readable invoices, not the targets, whose customer has a readable target ---
            FilterTree? visible = read.Filter;
            var sharesCustomer = FilterTree.Via(
                CustomerNavigation,
                FilterTree.Exists(Invoices, CustomerNavigation, visible is null ? isTarget : FilterTree.And([isTarget, visible])));
            var filter = FilterTree.And(visible is null ? [FilterTree.Not(isTarget), sharesCustomer] : [visible, FilterTree.Not(isTarget), sharesCustomer]);
            try
            {
                plan.AddExtra(
                    RelatedDocumentsExtra,
                    new QuerySpec
                    {
                        Root = Root(plan),
                        Select = $"{nameof(SalesInvoice.Id)}, {nameof(SalesInvoice.SerialNumber)}, {nameof(SalesInvoice.PostingDate)}, {nameof(SalesInvoice.CustomerId)}",
                        Filter = filter,
                        OrderBy = $"{nameof(SalesInvoice.PostingDate)} desc, {nameof(SalesInvoice.Id)} desc",
                        Take = MaxRows,
                    },
                    arguments);
            }
            catch (ArgumentException tooComplex) when (read.Outcome == AccessOutcome.Filtered)
            {
                // An administrator's configuration problem: a plain error, never a silent denial.
                throw new InvalidOperationException("The caller's filter on the sales invoices exceeded the query language's ceilings, so their related documents cannot be read.", tooComplex);
            }
        }

        /// <summary>The invoices' entity in the query schema of the read's tenant shape.</summary>
        /// <param name="plan">The details plan.</param>
        /// <returns>The entity.</returns>
        /// <exception cref="InvalidOperationException">The read's context carries no tenant settings, or the schema has no invoices.</exception>
        private EntityDescriptor Root(DetailsPlan<SalesInvoice> plan)
        {
            TenantSettings settings = plan.Context.TenantSettings
                ?? throw new InvalidOperationException("The read's context carries no tenant settings, so the related documents have no schema to compose under.");
            return schemas.GetSchema(settings.Shape).FindEntity(Invoices)
                ?? throw new InvalidOperationException($"The query schema has no entity '{Invoices}', which the related documents are read from.");
        }
    }
}
