// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.sql-profiles</c>: a default profile, the pool bound, encryption and no password
    ///     outside Development, no Key Vault literal, the catalog connection under the same rules, and
    ///     the allowed servers.
    /// </summary>
    public class SqlProfilesCheckTests
    {
        private const string Check = "core.sql-profiles";
        private const string Secret = "hunter2-secret";

        [Fact]
        public async Task The_sound_configuration_passes()
        {
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(Check));
        }

        [Fact]
        public async Task A_missing_default_profile_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(
                ("Tellma:Sql:Profiles:default", null),
                ("Tellma:Sql:Profiles:warm", "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20"));

            Assert.Contains("No 'default' credential profile", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_profile_must_set_its_pool_bound_within_the_per_tenant_limit()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(
                ("Tellma:Sql:Profiles:default", "Authentication=Active Directory Managed Identity;Encrypt=True"),
                ("Tellma:Sql:Profiles:wide", "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=21"),
                ("Tellma:Sql:Profiles:narrow", "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=5"));

            Assert.Equal(
                [
                    "The credential profile 'default' does not set Max Pool Size, so SqlClient's default of 100 per tenant would apply.",
                    "The credential profile 'wide' sets Max Pool Size to 21, above Tellma:Sql:MaxPoolSizePerTenant (20).",
                ],
                problems.Select(static p => p.Problem));
        }

        [Fact]
        public async Task The_per_tenant_limit_is_configurable()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(
                ("Tellma:Sql:MaxPoolSizePerTenant", "40"),
                ("Tellma:Sql:Profiles:default", "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=40"));

            Assert.Empty(problems);
        }

        [Fact]
        public async Task Outside_Development_every_connection_must_encrypt()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(
                ("Tellma:Sql:Profiles:default", "Authentication=Active Directory Managed Identity;Max Pool Size=20"),
                ("Tellma:Sql:Profiles:optional", "Authentication=Active Directory Managed Identity;Encrypt=False;Max Pool Size=20"),
                ("Tellma:Sql:Profiles:strict", "Authentication=Active Directory Managed Identity;Encrypt=Strict;Max Pool Size=20"),
                ("Tellma:Catalog:ConnectionString", "Data Source=sql01.database.windows.net;Initial Catalog=catalog;Authentication=Active Directory Managed Identity"));

            Assert.Equal(
                [
                    "The credential profile 'default' does not require encryption.",
                    "The credential profile 'optional' does not require encryption.",
                    "The catalog connection (Tellma:Catalog:ConnectionString) does not require encryption.",
                ],
                problems.Select(static p => p.Problem));
        }

        [Fact]
        public async Task Outside_Development_a_password_needs_the_explicit_allowance_and_is_never_quoted()
        {
            (string, string?)[] passwords =
            [
                ("Tellma:Sql:Profiles:default", $"User Id=tellma_web;Password={Secret};Encrypt=True;Max Pool Size=20"),
                ("Tellma:Sql:Profiles:short", $"User Id=tellma_web;Pwd={Secret};Encrypt=True;Max Pool Size=20"),
                ("Tellma:Catalog:ConnectionString", $"Data Source=sql01.contoso.local;Initial Catalog=catalog;User Id=tellma_web;Password={Secret};Encrypt=True"),
            ];

            IReadOnlyList<CompositionProblem> refused = await CheckAsync(passwords);
            IReadOnlyList<CompositionProblem> allowed = await CheckAsync([.. passwords, ("Tellma:Sql:AllowSqlPassword", "true")]);

            Assert.Equal(3, refused.Count);
            Assert.All(refused, static p => Assert.Contains("carries a SQL password outside Development", p.Problem, StringComparison.Ordinal));
            Assert.All(refused, static p => Assert.DoesNotContain(Secret, p.Problem + p.Fix, StringComparison.Ordinal));
            Assert.Empty(allowed);
        }

        [Fact]
        public async Task In_Development_encryption_and_passwords_are_the_developers_choice_but_the_pool_bound_is_not()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(
                new TestEnvironment("Development"),
                ("Tellma:Sql:Profiles:default", "Data Source=(localdb)\\MSSQLLocalDB;Integrated Security=true;Max Pool Size=20"),
                ("Tellma:Sql:Profiles:sa", $"User Id=sa;Password={Secret};Max Pool Size=100"),
                ("Tellma:Sql:AllowedServers:0", null),
                ("Tellma:PublicOrigin", "https://localhost:4200"));

            Assert.Equal("The credential profile 'sa' sets Max Pool Size to 100, above Tellma:Sql:MaxPoolSizePerTenant (20).", Assert.Single(problems).Problem);
        }

        [Fact]
        public async Task An_unresolved_Key_Vault_reference_inside_a_profile_is_reported_without_its_value()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(
                ("Tellma:Sql:Profiles:default", "User Id=tellma_web;Password=@Microsoft.KeyVault(SecretUri=https://kv.vault.azure.net/secrets/sql);Encrypt=True;Max Pool Size=20"),
                ("Tellma:Sql:AllowSqlPassword", "true"));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal("The credential profile 'default' holds an unresolved Key Vault reference.", problem.Problem);
            Assert.DoesNotContain("vault.azure.net", problem.Problem + problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_profile_that_does_not_parse_or_has_an_unusable_name_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(
                ("Tellma:Sql:Profiles:broken", "this is not a connection string"),
                ("Tellma:Sql:Profiles:has space", "Encrypt=True;Max Pool Size=20"));

            Assert.Equal(
                [
                    "The credential profile 'broken' is not a valid SqlClient connection string.",
                    "The credential profile name 'has space' is not 1 to 64 letters, digits, '.', '-' or '_'.",
                ],
                problems.Select(static p => p.Problem));
        }

        [Fact]
        public async Task Outside_Development_the_allowed_servers_are_required_and_each_entry_must_be_well_formed()
        {
            IReadOnlyList<CompositionProblem> missing = await CheckAsync(("Tellma:Sql:AllowedServers:0", null));
            IReadOnlyList<CompositionProblem> malformed = await CheckAsync(
                ("Tellma:Sql:AllowedServers:1", "*.contoso.local"),
                ("Tellma:Sql:AllowedServers:2", "."));

            Assert.Contains("Tellma:Sql:AllowedServers is empty", Assert.Single(missing).Problem, StringComparison.Ordinal);
            Assert.Equal(2, malformed.Count);
            Assert.All(malformed, static p => Assert.Contains("is neither a host nor '.' followed by a host suffix", p.Problem, StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_missing_catalog_connection_is_left_to_the_options_check()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Catalog:ConnectionString", null))));

            Assert.Empty(problems.From(Check));
            Assert.Contains(problems.From("core.options"), static p => p.Problem.Contains("ConnectionString", StringComparison.Ordinal));
        }

        /// <summary>Runs the checks over the sound configuration with changes, outside Development, returning this check's problems.</summary>
        /// <param name="changes">The changes.</param>
        /// <returns>The problems.</returns>
        private static Task<IReadOnlyList<CompositionProblem>> CheckAsync(params (string Key, string? Value)[] changes)
        {
            return CheckAsync(new TestEnvironment(), changes);
        }

        /// <summary>Runs the checks over the sound configuration with changes, returning this check's problems.</summary>
        /// <param name="environment">The environment.</param>
        /// <param name="changes">The changes.</param>
        /// <returns>The problems.</returns>
        private static async Task<IReadOnlyList<CompositionProblem>> CheckAsync(TestEnvironment environment, params (string Key, string? Value)[] changes)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(changes)),
                environment);
            return problems.From(Check);
        }
    }
}
