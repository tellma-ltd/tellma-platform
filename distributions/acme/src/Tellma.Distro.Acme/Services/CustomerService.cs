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
    // Spec 0018 §1.4: set-credit-limit is the reference action with arguments. It writes through
    // ActionContext.Update (spec 0015 §11.2), so the update runs under the caller's read filter and
    // the action grant's filter, all or nothing, with stamping.
    /// <summary>
    ///     The customers' stack service: the standard operations, the <c>set-credit-limit</c> action,
    ///     and the rule that no credit limit is negative.
    /// </summary>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    public sealed class CustomerService(IEntityPipelineFactory factory) : EntityService<Customer>(factory)
    {
        /// <summary>The action's name, its route under the stack's segment.</summary>
        public const string SetCreditLimitAction = "set-credit-limit";

        /// <summary>The root property the arguments' errors are reported under.</summary>
        private const string ArgumentsProperty = "Arguments";

        /// <summary>
        ///     Sets the credit limit of the target customers, in each customer's own currency. Requires
        ///     the <c>SetCreditLimit</c> action on the customers; a limit below zero is refused with
        ///     <see cref="AcmeValidationCodes.NegativeCreditLimit" />.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The new limit.</param>
        /// <returns>A task that completes when the update is composed.</returns>
        [EntityAction(SetCreditLimitAction, Description = "Sets the credit limit of customers.")]
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An action is an instance method by contract; the registry refuses a static one.")]
        public Task SetCreditLimitAsync(ActionContext<Customer, int> context, SetCreditLimitArguments arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);

            // The hook refuses a negative limit before the update is sent; composing it costs nothing.
            context.Update(new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(Customer.CreditLimit)] = arguments.CreditLimit });
            return Task.CompletedTask;
        }

        /// <summary>Validates a save: no customer's credit limit is negative.</summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the rule ran.</returns>
        protected override async Task ValidateAsync(SaveContext<Customer> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateAsync(context).ConfigureAwait(false);
            for (int index = 0; index < context.Entities.Count; index++)
            {
                if (context.Entities[index].CreditLimit < 0)
                {
                    context.Errors.Add(index, static customer => customer.CreditLimit, AcmeValidationCodes.NegativeCreditLimit);
                }
            }
        }

        /// <summary>Validates an action: the limit <c>set-credit-limit</c> sets is not negative.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the rule ran.</returns>
        protected override async Task ValidateActionAsync(string action, ActionContext<Customer, int> context)
        {
            ArgumentNullException.ThrowIfNull(action);
            ArgumentNullException.ThrowIfNull(context);
            await base.ValidateActionAsync(action, context).ConfigureAwait(false);

            // The arguments' own presence is the binding's; a null record never reaches the hook.
            if (string.Equals(action, SetCreditLimitAction, StringComparison.Ordinal)
                && context.Arguments is SetCreditLimitArguments { CreditLimit: < 0 })
            {
                context.Errors.Add(
                    ValidationPath.Root.Property(ArgumentsProperty).Property(nameof(SetCreditLimitArguments.CreditLimit)),
                    AcmeValidationCodes.NegativeCreditLimit);
            }
        }
    }
}
