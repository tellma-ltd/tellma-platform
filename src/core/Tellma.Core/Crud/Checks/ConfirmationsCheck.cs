// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Localization;

namespace Tellma.Core.Crud.Checks
{
    /// <summary>
    ///     <c>core.confirmations</c>: every confirmation key a feature declares is a dotted PascalCase
    ///     resource key, declared once across the composition, with a neutral string resource its
    ///     warning renders from.
    /// </summary>
    /// <param name="record">The realised composition.</param>
    internal sealed class ConfirmationsCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.Confirmations;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];
            INeutralStringKeys? strings = services.GetService<INeutralStringKeys>();
            Dictionary<string, ContributedConfirmation> declared = new(StringComparer.Ordinal);
            foreach (ContributedConfirmation confirmation in record.Confirmations)
            {
                string key = confirmation.Key;
                string feature = confirmation.Owner.Name;

                // --- The key: a resource key, like a validation code ---
                if (!StackNames.IsConfirmationKey(key))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The feature '{feature}' declares the confirmation '{key}', which is not a dotted PascalCase key.",
                        "Name the confirmation like a validation code: 'Inventory.BackdatedRecost'."));
                    continue;
                }

                // --- Declared once, so confirming it means one thing ---
                if (declared.TryGetValue(key, out ContributedConfirmation? earlier))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        ReferenceEquals(earlier.Owner, confirmation.Owner)
                            ? $"The feature '{feature}' declares the confirmation '{key}' twice."
                            : $"The confirmation '{key}' is declared by both the feature '{earlier.Owner.Name}' and the feature '{feature}'.",
                        "Declare each confirmation once, in the feature whose code raises it."));
                    continue;
                }

                declared[key] = confirmation;

                // --- The message its warning renders from ---
                if (strings is not null && !strings.Declares(key))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The confirmation '{key}' of the feature '{feature}' has no neutral string resource, so its warning has no message.",
                        $"Add '{key}' to the feature's neutral Strings resource, its message written over the warning's arguments."));
                }
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
