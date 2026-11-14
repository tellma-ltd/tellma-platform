// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Access;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The securable registry, composed directly from contributors: the name grammar, merging by
    ///     pair, aliases, the sensitive set, ordering, lookup and the fingerprint.
    /// </summary>
    public class SecurableRegistryTests
    {
        // --- The grammar ---

        [Theory]
        [InlineData("gl.Center", true)]
        [InlineData("core.Settings.General", true)]
        [InlineData("a.b", true)]
        [InlineData("Acme2.Report9", true)]
        [InlineData("gl", false)]
        [InlineData("gl.", false)]
        [InlineData(".Center", false)]
        [InlineData("gl..Center", false)]
        [InlineData("gl.2Center", false)]
        [InlineData("gl.Cost-Center", false)]
        [InlineData("gl.Cost_Center", false)]
        [InlineData("gl.Céntre", false)]
        [InlineData("gl.*", false)]
        [InlineData("*", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void A_resource_is_two_or_more_dotted_segments_of_ascii_letters_and_digits(string? resource, bool valid)
        {
            Assert.Equal(valid, SecurableNames.IsResource(resource));
        }

        [Fact]
        public void A_resource_has_at_most_128_characters()
        {
            string longest = "a." + new string('b', 126);

            Assert.True(SecurableNames.IsResource(longest));
            Assert.False(SecurableNames.IsResource(longest + "c"));
        }

        [Theory]
        [InlineData("Read", true)]
        [InlineData("SetCreditLimit", true)]
        [InlineData("Save2", true)]
        [InlineData("READ", true)]
        [InlineData("read", false)]
        [InlineData("2Read", false)]
        [InlineData("Set-Limit", false)]
        [InlineData("Set Limit", false)]
        [InlineData("Réad", false)]
        [InlineData("*", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void An_action_is_a_pascal_case_ascii_identifier(string? action, bool valid)
        {
            Assert.Equal(valid, SecurableNames.IsAction(action));
        }

        [Fact]
        public void An_action_has_at_most_32_characters()
        {
            string longest = "A" + new string('b', 31);

            Assert.True(SecurableNames.IsAction(longest));
            Assert.False(SecurableNames.IsAction(longest + "c"));
        }

        [Fact]
        public void Malformed_names_and_the_wildcard_are_reported_once_each()
        {
            SecurableRegistry registry = Compose(("acme", static builder => builder
                .Add("*", "Read", null)
                .Add("acme.Report", "*", null)
                .Add("acme", "Read", null)
                .Add("acme", "Save", null)
                .Add("acme.Report", "read", null)));

            Assert.Equal(
                [
                    "The securable '*:Read' of the feature 'acme' names the wildcard '*' as a resource.",
                    "The securable 'acme.Report:*' of the feature 'acme' names the wildcard '*' as an action.",
                    "The securable 'acme:Read' of the feature 'acme' names the resource 'acme', which is not well formed.",
                    "The securable 'acme.Report:read' of the feature 'acme' names the action 'read', which is not well formed.",
                ],
                Problems(registry));
        }

        // --- Merging by pair ---

        [Fact]
        public void The_same_pair_registered_again_for_the_same_feature_is_one_securable_made_sensitive_by_either()
        {
            SecurableRegistry registry = Compose(
                ("gl", static builder => builder.Add("gl.Center", "Save", "gl.Center")),
                ("acme", static builder => builder.Add("GL.center", "Save", "GL.CENTER", isSensitive: true, feature: "gl")));

            SecurableDescriptor securable = Assert.Single(registry.All);
            Assert.Equal(new SecurableDescriptor("gl.Center", "Save", "gl.Center", true, "gl", null), securable);
            Assert.Equal(["The resource 'gl.Center' of the feature 'gl' is registered as 'GL.center' by the feature 'acme'."], Problems(registry));
        }

        [Fact]
        public void The_same_pair_with_another_filter_root_owner_action_spelling_or_feature_is_a_problem()
        {
            SecurableRegistry registry = Compose(
                ("gl", static builder => builder
                    .Add("gl.Center", "Read", "gl.Center")
                    .Add("gl.CenterLine", "Read", "gl.CenterLine", owner: new SecurableOwner("gl.Center", "Center"))
                    .Add("gl.Account", "Read", "gl.Account")),
                ("acme", static builder => builder
                    .Add("gl.Center", "Read", null, feature: "gl")
                    .Add("gl.CenterLine", "Read", "gl.CenterLine", owner: new SecurableOwner("gl.Center", "Parent"), feature: "gl")
                    .Add("gl.Center", "READ", "gl.Center", feature: "gl")
                    .Add("gl.Account", "Read", "gl.Account")));

            Assert.Equal(
                [
                    "The securable 'gl.Center:Read' is registered with the filter root 'gl.Center' by the feature 'gl' and with none by the feature 'acme'.",
                    "The securable 'gl.CenterLine:Read' is registered with the owner 'gl.Center' through 'Center' by the feature 'gl' and with 'gl.Center' through 'Parent' by the feature 'acme'.",
                    "The securable 'gl.Center:Read' of the feature 'gl' is registered with the action 'READ' by the feature 'acme'.",
                    "The securable 'gl.Account:Read' is registered under the feature 'gl' by the feature 'gl' and under the feature 'acme' by the feature 'acme'.",
                ],
                Problems(registry));

            // The first registration stands.
            Assert.Equal("gl.Center", registry.Find("gl.Center", "Read")!.FilterRoot);
            Assert.Equal(new SecurableOwner("gl.Center", "Center"), registry.Find("gl.CenterLine", "Read")!.Owner);
            Assert.Equal("gl", registry.Find("gl.Account", "Read")!.Feature);
        }

        [Fact]
        public void A_pair_registered_under_two_features_is_a_problem_whether_the_feature_is_named_or_implied()
        {
            // Core registers core.User:Read for itself; a pack's stack contributor naming its own
            // feature would group the pair elsewhere in the permissions editor.
            SecurableRegistry registry = Compose(
                ("core", static builder => builder.Add(CoreResources.User, AccessActions.Read, CoreResources.User)),
                ("crud", static builder => builder
                    .Add(CoreResources.User, AccessActions.Read, CoreResources.User, feature: "acme")
                    .Add(CoreResources.User, AccessActions.Read, CoreResources.User, feature: "core")),
                ("acme", static builder => builder.Add(CoreResources.User, AccessActions.Read, CoreResources.User)));

            Assert.Equal(
                [
                    "The securable 'core.User:Read' is registered under the feature 'core' by the feature 'core' and under the feature 'acme' by the feature 'crud'.",
                    "The securable 'core.User:Read' is registered under the feature 'core' by the feature 'core' and under the feature 'acme' by the feature 'acme'.",
                ],
                Problems(registry));
            Assert.Equal("core", Assert.Single(registry.All).Feature);
        }

        [Fact]
        public void A_securable_belongs_to_the_feature_it_names_else_to_its_contributor()
        {
            SecurableRegistry registry = Compose(("crud", static builder => builder
                .Add("gl.Center", "Read", "gl.Center", feature: "gl")
                .Add("acme.Report", "Read", null)));

            Assert.Equal("gl", registry.Find("gl.Center", "Read")!.Feature);
            Assert.Equal("crud", registry.Find("acme.Report", "Read")!.Feature);
        }

        // --- Aliases ---

        [Fact]
        public void An_alias_resolves_on_Find_and_FindResource_only()
        {
            SecurableRegistry registry = Compose(("gl", static builder => builder
                .Add("gl.Center", "Read", "gl.Center")
                .Alias("gl.CostCenter", "gl.Center")));

            Assert.Empty(registry.Problems);
            Assert.Same(registry.Find("gl.Center", "Read"), registry.Find("GL.costcenter", "read"));
            Assert.Null(registry.Find("gl.CostCenter", "Save"));

            // A resource resolves to its registered spelling, through one alias hop at most.
            Assert.Equal("gl.Center", registry.FindResource("GL.CENTER"));
            Assert.Equal("gl.Center", registry.FindResource("gl.costCenter"));
            Assert.Null(registry.FindResource("gl.ProfitCenter"));
            Assert.Null(registry.FindResource("*"));
            Assert.Null(registry.FindResource(null!));
            Assert.Equal(["gl.Center"], registry.Resources);
            Assert.Equal(["gl.Center:Read"], registry.All.Select(static securable => securable.ToString()));
            Assert.Empty(registry.ForResource("gl.CostCenter"));
            Assert.Equal("gl.Center", registry.Aliases["gl.costcenter"]);
        }

        [Fact]
        public void An_alias_naming_itself_a_registered_resource_or_nothing_registered_or_two_targets_is_a_problem()
        {
            SecurableRegistry registry = Compose(("gl", static builder => builder
                .Add("gl.Center", "Read", "gl.Center")
                .Add("gl.Account", "Read", "gl.Account")
                .Alias("gl.Center", "gl.center")
                .Alias("gl.Account", "gl.Center")
                .Alias("gl.CostCenter", "gl.ProfitCenter")
                .Alias("gl.Ledger", "gl.Account")
                .Alias("gl.Ledger", "gl.Account")
                .Alias("gl.Ledger", "gl.Center")
                .Alias("gl", "gl.Center")));

            Assert.Equal(
                [
                    "The alias 'gl.Center' → 'gl.center' of the feature 'gl' renames a resource to itself.",
                    "The alias 'gl.Account' → 'gl.Center' of the feature 'gl' renames 'gl.Account', which is still a registered resource.",
                    "The alias 'gl.CostCenter' → 'gl.ProfitCenter' of the feature 'gl' resolves to 'gl.ProfitCenter', which is not a registered resource.",
                    "The alias 'gl.Ledger' → 'gl.Center' of the feature 'gl' renames 'gl.Ledger', which is already an alias of 'gl.Account'.",
                    "The alias 'gl' → 'gl.Center' of the feature 'gl' names the resource 'gl', which is not well formed.",
                ],
                Problems(registry));
            Assert.Equal("gl.Account", registry.Find("gl.Ledger", "Read")!.Resource);
        }

        // --- The sensitive set ---

        [Fact]
        public void A_core_default_is_sensitive_when_registered_and_MarkNotSensitive_removes_it()
        {
            SecurableRegistry registry = Compose(
                ("core", static builder => builder
                    .Add(CoreResources.User, AccessActions.Save, CoreResources.User)
                    .Add(CoreResources.User, AccessActions.Delete, CoreResources.User)
                    .Add(CoreResources.User, AccessActions.Read, CoreResources.User)),
                ("acme", static builder => builder.MarkNotSensitive("CORE.USER", "DELETE")));

            Assert.Empty(registry.Problems);
            Assert.True(registry.Find(CoreResources.User, AccessActions.Save)!.IsSensitive);
            Assert.False(registry.Find(CoreResources.User, AccessActions.Delete)!.IsSensitive);
            Assert.False(registry.Find(CoreResources.User, AccessActions.Read)!.IsSensitive);

            // A default whose pair nobody registers changes nothing.
            Assert.Null(registry.Find(CoreResources.Schedule, AccessActions.Save));
        }

        [Fact]
        public void The_core_defaults_are_exactly_the_platforms_twelve_sensitive_pairs()
        {
            // Every pair of the user, role, general settings and schedule administration that a
            // recent sign-in guards; a dropped or misspelled pair would silently lose its step-up.
            (string Resource, string Action)[] expected =
            [
                ("core.User", "Save"),
                ("core.User", "Delete"),
                ("core.User", "Activate"),
                ("core.User", "Invite"),
                ("core.User", "Credentials"),
                ("core.Role", "Save"),
                ("core.Role", "Delete"),
                ("core.Role", "Activate"),
                ("core.Settings.General", "Save"),
                ("core.Schedule", "Save"),
                ("core.Schedule", "Delete"),
                ("core.Schedule", "Activate"),
            ];

            Assert.Equal(expected.Order(), SecurableRegistry.CoreSensitive.Order());

            // Registered, each is sensitive with no mark, and its neighbours are not.
            SecurableRegistry registry = Compose(("core", Register));

            Assert.Empty(registry.Problems);
            Assert.All(expected, pair => Assert.True(registry.Find(pair.Resource, pair.Action)!.IsSensitive, $"{pair.Resource}:{pair.Action}"));
            Assert.Equal(
                ["core.Role:Read", "core.Settings.General:Read", "core.User:Read", "core.User:Preferences"],
                registry.All.Where(static securable => !securable.IsSensitive).Select(static securable => securable.ToString()));

            // Every default pair, and four neighbours of them.
            void Register(SecurableRegistryBuilder builder)
            {
                foreach ((string resource, string action) in expected)
                {
                    _ = builder.Add(resource, action, null);
                }

                _ = builder
                    .Add("core.User", "Read", null)
                    .Add("core.Role", "Read", null)
                    .Add("core.Settings.General", "Read", null)
                    .Add("core.User", "Preferences", null);
            }
        }

        [Fact]
        public void MarkSensitive_adds_a_pair_and_MarkNotSensitive_overrides_a_registration_that_said_sensitive()
        {
            SecurableRegistry registry = Compose(
                ("acme", static builder => builder
                    .Add("acme.Payment", "Approve", null)
                    .Add("acme.Payment", "Void", null, isSensitive: true)),
                ("acme.local", static builder => builder
                    .MarkSensitive("acme.Payment", "Approve")
                    .MarkNotSensitive("acme.Payment", "Void")));

            Assert.True(registry.Find("acme.Payment", "Approve")!.IsSensitive);
            Assert.False(registry.Find("acme.Payment", "Void")!.IsSensitive);
        }

        [Fact]
        public void A_pair_marked_both_ways_is_refused_once_and_stays_sensitive()
        {
            SecurableRegistry registry = Compose(
                ("acme", static builder => builder.Add("acme.Payment", "Approve", null).MarkNotSensitive("acme.Payment", "Approve")),
                ("acme.local", static builder => builder.MarkSensitive("acme.Payment", "Approve").MarkSensitive("acme.Payment", "Approve")));

            Assert.Equal(
                ["The securable 'acme.Payment:Approve' is marked not sensitive by the feature 'acme' and sensitive by the feature 'acme.local'."],
                Problems(registry));
            Assert.True(registry.Find("acme.Payment", "Approve")!.IsSensitive);
        }

        [Fact]
        public void A_mark_of_a_pair_nobody_registers_is_a_problem()
        {
            SecurableRegistry registry = Compose(("acme", static builder => builder.MarkNotSensitive(CoreResources.Schedule, AccessActions.Save)));

            Assert.Equal(["The feature 'acme' marks 'core.Schedule:Save' not sensitive, which is not a registered securable."], Problems(registry));
        }

        // --- Order, lookup and the fingerprint ---

        [Fact]
        public void All_is_ordered_by_resource_then_the_platforms_actions_in_their_order_then_others_ignoring_case()
        {
            SecurableRegistry registry = Compose(("acme", static builder => builder
                .Add("gl.center", "Save", null)
                .Add("acme.Report", "Read", null)
                .Add("gl.center", "Delete", null)
                .Add("GL.Account", "Read", null)
                .Add("core.User", "Credentials", null)
                .Add("core.User", "Preferences", null)
                .Add("core.User", "Archive", null)
                .Add("core.User", "Invite", null)
                .Add("core.User", "Activate", null)
                .Add("core.User", "Approve", null)
                .Add("core.User", "Delete", null)
                .Add("core.User", "Save", null)
                .Add("core.User", "Read", null)));

            Assert.Equal(
                [
                    "acme.Report:Read",
                    "core.User:Read",
                    "core.User:Save",
                    "core.User:Delete",
                    "core.User:Activate",
                    "core.User:Invite",
                    "core.User:Preferences",
                    "core.User:Credentials",
                    "core.User:Approve",
                    "core.User:Archive",
                    "GL.Account:Read",
                    "gl.center:Save",
                    "gl.center:Delete",
                ],
                registry.All.Select(static securable => securable.ToString()));
            Assert.Equal(["acme.Report", "core.User", "GL.Account", "gl.center"], registry.Resources);
            Assert.Equal(["Save", "Delete"], registry.ForResource("GL.CENTER").Select(static securable => securable.Action));
            Assert.Equal("Save", registry.Find("Gl.Center", "save")!.Action);
            Assert.Null(registry.Find("gl.center", "Read"));
            Assert.Null(registry.Find("gl.center", "Activate"));
            Assert.Null(registry.Find(null!, "Read"));
            Assert.Null(registry.Find("gl.center", null!));
            Assert.Empty(registry.ForResource(null!));
        }

        [Fact]
        public void The_fingerprint_is_stable_across_registration_order_and_moves_with_the_content()
        {
            static void First(SecurableRegistryBuilder builder)
            {
                _ = builder.Add("gl.Center", "Read", "gl.Center").Add("gl.Center", "Save", "gl.Center");
            }

            static void Second(SecurableRegistryBuilder builder)
            {
                _ = builder.Add("acme.Report", "Read", null).Alias("gl.CostCenter", "gl.Center");
            }

            string fingerprint = Compose(("gl", First), ("acme", Second)).Fingerprint;

            Assert.Matches("^[0-9a-f]{64}$", fingerprint);
            Assert.Equal(fingerprint, Compose(("acme", Second), ("gl", First)).Fingerprint);
            Assert.Equal(fingerprint, Compose(("gl", static builder => builder
                .Add("gl.Center", "Save", "gl.Center")
                .Alias("gl.CostCenter", "gl.Center")
                .Add("acme.Report", "Read", null)
                .Add("gl.Center", "Read", "gl.Center"))).Fingerprint);

            // Sensitivity, a filter root and an alias each move it; so do an owner's resource and
            // navigation, and an alias's target.
            Assert.NotEqual(fingerprint, Compose(("gl", First), ("acme", Second), ("acme.local", static builder => builder.MarkSensitive("acme.Report", "Read"))).Fingerprint);
            Assert.NotEqual(fingerprint, Compose(("gl", First), ("acme", static builder => builder.Add("acme.Report", "Read", "acme.Report").Alias("gl.CostCenter", "gl.Center"))).Fingerprint);
            Assert.NotEqual(fingerprint, Compose(("gl", First), ("acme", static builder => builder.Add("acme.Report", "Read", null))).Fingerprint);

            string owned = Compose(("gl", static builder => Owned(builder, "gl.Center", "Center"))).Fingerprint;
            Assert.NotEqual(owned, Compose(("gl", static builder => Owned(builder, "gl.Center", "Parent"))).Fingerprint);
            Assert.NotEqual(owned, Compose(("gl", static builder => Owned(builder, "gl.Account", "Center"))).Fingerprint);
            Assert.NotEqual(
                Compose(("gl", static builder => builder.Add("gl.Center", "Read", null).Add("gl.Account", "Read", null).Alias("gl.CostCenter", "gl.Center"))).Fingerprint,
                Compose(("gl", static builder => builder.Add("gl.Center", "Read", null).Add("gl.Account", "Read", null).Alias("gl.CostCenter", "gl.Account"))).Fingerprint);

            // Two owners for one line, the rest unchanged.
            static void Owned(SecurableRegistryBuilder builder, string resource, string navigation)
            {
                _ = builder
                    .Add("gl.Center", "Read", "gl.Center")
                    .Add("gl.Account", "Read", "gl.Account")
                    .Add("gl.CenterLine", "Read", "gl.CenterLine", owner: new SecurableOwner(resource, navigation));
            }
        }

        [Fact]
        public void A_contributor_that_throws_is_a_problem_the_others_still_run_and_nothing_is_served()
        {
            int ran = 0;
            SecurableRegistry registry = Compose(
                ("acme", static _ => throw new FormatException("bad pair")),
                ("gl", Gl),
                ("crm", static _ => throw new InvalidOperationException("no stacks")));

            // Every failing contributor once; nothing is judged against the part that composed.
            Assert.Equal(1, ran);
            Assert.True(registry.Failed);
            Assert.Collection(
                Problems(registry),
                static problem => Assert.Matches("^The securable contributor .+ of the feature 'acme' failed with FormatException: bad pair$", problem),
                static problem => Assert.Matches("^The securable contributor .+ of the feature 'crm' failed with InvalidOperationException: no stacks$", problem));
            Assert.Empty(registry.All);
            Assert.Empty(registry.Resources);
            Assert.Null(registry.Find("gl.Center", "Read"));
            Assert.Null(registry.FindResource("gl.Center"));
            Assert.Empty(registry.ForResource("gl.Center"));
            Assert.Equal(Compose().Fingerprint, registry.Fingerprint);
            Assert.False(Compose().Failed);

            // A contributor that registers, one of them malformed, and counts its runs.
            void Gl(SecurableRegistryBuilder builder)
            {
                ran++;
                _ = builder.Add("gl.Center", "Read", "gl.Center").Add("*", "Read", null);
            }
        }

        [Fact]
        public void The_platforms_action_order_is_the_declaration_order_of_the_access_actions()
        {
            // The order the access actions are declared in, the wildcard aside.
            string[] declared =
            [
                .. typeof(AccessActions).GetFields()
                    .Where(static field => field.IsLiteral)
                    .Select(static field => (string)field.GetRawConstantValue()!)
                    .Where(static action => action != AccessActions.Wildcard),
            ];

            Assert.Equal(declared, SecurableRegistry.PlatformActionOrder);
        }

        [Fact]
        public void The_builder_closes_once_the_registry_is_composed()
        {
            SecurableRegistryBuilder? kept = null;
            _ = Compose(("acme", builder => kept = builder));

            _ = Assert.Throws<InvalidOperationException>(() => kept!.Add("acme.Report", "Read", null));
        }

        /// <summary>Composes a registry from configurations, each on behalf of its feature, in order.</summary>
        /// <param name="contributors">Each feature's name and configuration.</param>
        /// <returns>The registry.</returns>
        internal static SecurableRegistry Compose(params (string Feature, Action<SecurableRegistryBuilder> Configure)[] contributors)
        {
            return new SecurableRegistry([.. contributors.Select(static contributor =>
                ((ISecurableContributor)new FeatureSecurableContributor(contributor.Feature, contributor.Configure), contributor.Feature))]);
        }

        /// <summary>The text of the registry's problems, each checked to be the securables check's.</summary>
        /// <param name="registry">The registry.</param>
        /// <returns>The problems' text.</returns>
        private static IEnumerable<string> Problems(SecurableRegistry registry)
        {
            Assert.All(registry.Problems, static problem => Assert.Equal("core.securables", problem.Source));
            return registry.Problems.Select(static problem => problem.Problem);
        }
    }
}
