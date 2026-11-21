// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.key-vault-literals</c>: no configuration value under <c>Tellma:</c> is a Key Vault
    ///     reference App Service failed to resolve. App Service leaves the reference text in place
    ///     when it cannot read the secret, so the host would otherwise run on the literal
    ///     <c>@Microsoft.KeyVault(…)</c> as a secret.
    /// </summary>
    /// <param name="record">The realised composition.</param>
    internal sealed class KeyVaultLiteralsCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        private const string Section = "Tellma";
        private const string ReferencePrefix = "@Microsoft.KeyVault(";

        /// <inheritdoc />
        public string Name => CoreCheckNames.KeyVaultLiterals;

        /// <inheritdoc />
        public bool RequiresInfrastructure => true;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            // The key is reported, never the value: a reference names a vault and a secret, which
            // has no place in a log line.
            List<CompositionProblem> problems = [.. record.Configuration.GetSection(Section)
                .AsEnumerable()
                .Where(static pair => pair.Value is not null
                    && pair.Value.TrimStart().StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase))
                .Select(static pair => pair.Key)
                .Order(StringComparer.Ordinal)
                .Select(key => new CompositionProblem(
                    Name,
                    $"The configuration value '{key}' is an unresolved Key Vault reference.",
                    "Grant the app's managed identity read access to the secret (the Key Vault Secrets User role) and check that the reference names an existing secret, so App Service resolves it before the host starts."))];

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
