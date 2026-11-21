// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Access;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Provisioning;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Infrastructure;
using Tellma.Core.Users;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The access runtime as every composition registers it: the options bound and judged by the
    ///     startup gate, the set builder over the composed registry and schema, the write side's rules,
    ///     effects, bootstrapper and drift scanner, and the membership filter against the composed model.
    /// </summary>
    public class AccessRuntimeCompositionTests(AccessModel model) : IClassFixture<AccessModel>
    {
        [Fact]
        public async Task The_options_bind_from_their_section_and_the_builder_is_one_singleton()
        {
            await using ServiceProvider provider = TestComposition.Compose(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Access:MaxHolders", "7"), ("Tellma:Access:FastDenyWindow", "00:00:03"))))
                .BuildServiceProvider(validateScopes: true);

            AccessOptions options = provider.GetRequiredService<IOptions<AccessOptions>>().Value;
            Assert.Equal((7, TimeSpan.FromSeconds(3), 300), (options.MaxHolders, options.FastDenyWindow, options.MaxPermissionsPerRole));
            Assert.Same(provider.GetRequiredService<UserAccessBuilder>(), provider.GetRequiredService<UserAccessBuilder>());
        }

        [Fact]
        public async Task The_evaluation_is_scoped_with_its_witness_under_the_default_step_up_bar()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            await using AsyncServiceScope first = provider.CreateAsyncScope();
            await using AsyncServiceScope second = provider.CreateAsyncScope();

            // --- One evaluator per scope, the same instance behind its contract ---
            AccessEvaluator evaluator = first.ServiceProvider.GetRequiredService<AccessEvaluator>();
            Assert.Same(evaluator, first.ServiceProvider.GetRequiredService<IAccessEvaluator>());
            Assert.NotSame(evaluator, second.ServiceProvider.GetRequiredService<IAccessEvaluator>());
            Assert.Same(first.ServiceProvider.GetRequiredService<AccessWitness>(), first.ServiceProvider.GetRequiredService<AccessWitness>());
            Assert.NotSame(first.ServiceProvider.GetRequiredService<AccessWitness>(), second.ServiceProvider.GetRequiredService<AccessWitness>());

            // --- The default bar, the composer once, the holders resolver closed over the leaves, and the guards ---
            Assert.Same(StepUpBar.Default, provider.GetRequiredService<StepUpBar>());
            Assert.Same(provider.GetRequiredService<HoldersComposer>(), provider.GetRequiredService<HoldersComposer>());
            _ = Assert.IsType<AccessHoldersResolver<User, Role>>(first.ServiceProvider.GetRequiredService<IAccessHoldersResolver>());
            _ = Assert.IsType<AdministratorDirectory>(first.ServiceProvider.GetRequiredService<IAdministratorDirectory>());
            _ = Assert.IsType<AccessGuards>(first.ServiceProvider.GetRequiredService<IAccessGuards>());
        }

        [Fact]
        public async Task The_write_side_is_composed_over_the_leaves()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            TellmaCompositionRecord record = provider.GetRequiredService<TellmaCompositionRecord>();

            // --- The rules, the effects and the details contributor of the users' and the roles' stacks, the core feature's own ---
            Assert.Equal(
                [
                    (typeof(User), typeof(UserAccessRules<User>), ComponentKind.Validator),
                    (typeof(Role), typeof(RoleAccessRules<Role>), ComponentKind.Validator),
                    (typeof(User), typeof(UserStampsCompanion<User>), ComponentKind.PersistEffect),
                    (typeof(User), typeof(BootstrapSubjectEffect<User>), ComponentKind.PersistEffect),
                    (typeof(User), typeof(UserActivityContributor<User>), ComponentKind.DetailsContributor),
                ],
                record.Components.Where(static component => component.EntityType == typeof(User) || component.EntityType == typeof(Role))
                    .Select(static component => (component.EntityType, component.ComponentType, component.Kind)));
            Assert.Contains(record.ProvisioningSteps, static step => step.StepType == typeof(BootstrapAdministratorStep));

            // --- The bootstrapper over the user leaf; the scanner once behind both its contracts ---
            _ = Assert.IsType<TenantBootstrapper<User>>(scope.ServiceProvider.GetRequiredService<ITenantBootstrapper>());
            PermissionDriftScanner scanner = scope.ServiceProvider.GetRequiredService<PermissionDriftScanner>();
            Assert.Same(scanner, scope.ServiceProvider.GetRequiredService<IPermissionDriftScanner>());
            Assert.Same(scanner, scope.ServiceProvider.GetRequiredService<ITenantDriftReport>());
        }

        [Fact]
        public async Task The_sound_configuration_passes_the_options_check()
        {
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(CoreCheckNames.Options));
        }

        [Theory]
        [InlineData("Tellma:Access:FastDenyWindow", "03:00:00", "FastDenyWindow")]
        [InlineData("Tellma:Access:ActivityStampInterval", "00:00:10", "ActivityStampInterval")]
        [InlineData("Tellma:Access:MaxFilterLength", "4096", "MaxFilterLength")]
        [InlineData("Tellma:Access:MaxRolesPerUser", "0", "MaxRolesPerUser")]
        [InlineData("Tellma:Access:PermissionsMaxAge", "-00:00:01", "PermissionsMaxAge")]
        public async Task Options_out_of_bounds_fail_the_gate(string key, string value, string member)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound((key, value))));

            Assert.Contains(problems.From(CoreCheckNames.Options), problem => problem.Problem.Contains(member, StringComparison.Ordinal));
        }

        [Fact]
        public void The_composed_builder_resolves_rows_against_the_composed_registry_and_schema()
        {
            UserAccessBuilder builder = model.Services.GetRequiredService<UserAccessBuilder>();
            QueryexSchema schema = model.Services.GetRequiredService<IQueryexSchemaProvider>().GetSchema(MultilingualShape.All);

            UserAccess access = builder.Build(
                1,
                42,
                Guid.NewGuid(),
                Guid.NewGuid(),
                [
                    new PermissionRow(1, 10, "Clerks", false, "core.user", "read", "Id = me()", QueryexLanguage.Version, null, null),
                    new PermissionRow(2, 10, "Clerks", false, CoreResources.User, "Read", "Nothing = 1", QueryexLanguage.Version, null, null),
                    new PermissionRow(3, 10, "Clerks", false, "core.Nothing", "Read", null, null, null, null),
                ],
                schema);

            RoleAccessGrant grant = Assert.Single(access.Grants);
            Assert.Equal((CoreResources.User, AccessActions.Read, "Id = me()"), (grant.Resource, grant.Action, grant.Filter));
            Assert.Equal(
                [AccessProblemCode.FilterInvalid, AccessProblemCode.UnknownResource],
                access.Problems.Select(static problem => problem.Code));
        }

        [Fact]
        public void The_current_membership_filter_validates_against_the_composed_memberships()
        {
            QueryexSchema schema = model.Services.GetRequiredService<IQueryexSchemaProvider>().GetSchema(MultilingualShape.All);
            EntityDescriptor memberships = schema.FindEntity(model.Metadata.Get(typeof(RoleMembership)).Name)!;
            FilterTree.LeafNode leaf = Assert.IsType<FilterTree.LeafNode>(AccessFilters.CurrentMembership);

            QueryexResult<ValidatedExpression> result = new QueryexEngine().Validate(leaf.Text, new ValidationOptions
            {
                Schema = schema,
                Root = memberships,
                Mode = QueryexMode.Filter,
                QueryexVersion = QueryexLanguage.Version,
                HasUser = false,
                Parameters = [],
                Limits = QueryexProfiles.AccessLimits,
            });

            Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Select(static diagnostic => diagnostic.Code)));
            Assert.Empty(result.Diagnostics);
        }
    }
}
