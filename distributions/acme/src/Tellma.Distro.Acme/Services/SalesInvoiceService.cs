// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Validation;
using Tellma.Distro.Acme.Entities;

namespace Tellma.Distro.Acme.Services
{
    // Spec 0018 §1.4: close is the reference action without arguments. The invoices' assign and post
    // actions join with assignments (spec 0028 §3.5).
    /// <summary>The sales invoices' stack service: the standard operations and the <c>close</c> action.</summary>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    public sealed class SalesInvoiceService(IEntityPipelineFactory factory) : EntityService<SalesInvoice>(factory)
    {
        /// <summary>The action's name, its route under the stack's segment.</summary>
        public const string CloseAction = "close";

        /// <summary>
        ///     Closes the target invoices. Requires the <c>Close</c> action on the invoices; closing a
        ///     closed invoice leaves it closed.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the update is composed.</returns>
        [EntityAction(CloseAction, Description = "Closes sales invoices.")]
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An action is an instance method by contract; the registry refuses a static one.")]
        public Task CloseAsync(ActionContext<SalesInvoice, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Update(new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(SalesInvoice.IsClosed)] = true });
            return Task.CompletedTask;
        }
    }
}
