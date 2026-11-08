// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary><c>core.key-vault-literals</c>: no value under <c>Tellma:</c> is an unresolved Key Vault reference.</summary>
    public class KeyVaultLiteralsCheckTests
    {
        private const string Check = "core.key-vault-literals";
        private const string Reference = "@Microsoft.KeyVault(SecretUri=https://kvtellmaacme.vault.azure.net/secrets/client)";

        [Fact]
        public async Task Each_unresolved_reference_under_Tellma_is_reported_by_key_without_its_value()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(new Dictionary<string, string?>
                {
                    ["Tellma:PublicOrigin"] = "https://acme.app.tellma.com",
                    ["Tellma:Identity:ServiceClientSecret"] = Reference,
                    ["Tellma:Identity:ClientSecret"] = "  " + Reference.ToUpperInvariant(),
                    ["Tellma:Identity:Authority"] = "https://identity.tellma.com",
                    ["Azure:SignalR:ConnectionString"] = Reference,
                }));

            List<CompositionProblem> literals = problems.From(Check);
            string[] expected =
            [
                "The configuration value 'Tellma:Identity:ClientSecret' is an unresolved Key Vault reference.",
                "The configuration value 'Tellma:Identity:ServiceClientSecret' is an unresolved Key Vault reference.",
            ];
            Assert.Equal(expected, literals.Select(static p => p.Problem));
            Assert.All(literals, static p => Assert.DoesNotContain("vault.azure.net", p.Problem + p.Fix, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Configuration_without_references_passes()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static _ => { });

            Assert.Empty(problems.From(Check));
        }
    }
}
