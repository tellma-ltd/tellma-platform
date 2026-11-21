// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Access;
using Tellma.Core.Queryex;
using Tellma.Core.Users;
using static Tellma.Core.Tests.Access.AccessRulesHarness;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     The roles' drifted permissions against the fixture's securables and schema: one problem per
    ///     condition that makes a stored permission grant nothing, in role then permission order, and
    ///     an invalid filter's diagnostics located as the role editor's filter check locates them.
    /// </summary>
    public class RoleProblemsTests
    {
        /// <summary>The filter that validates against the widgets.</summary>
        private const string ValidFilter = "Amount > 0";

        [Fact]
        public void Each_drift_condition_is_one_problem_in_permission_order_and_a_granting_permission_is_none()
        {
            int version = QueryexLanguage.Version;
            Role clerks = RoleOf(
                isPublic: false,
                Stored(1, Widget, AccessActions.Read),
                Stored(2, "fx.Gone", AccessActions.Read),
                Stored(3, Widget, "Fly"),
                Stored(4, Report, "Run", ValidFilter, version),
                Stored(5, Mixed, AccessActions.Read, ValidFilter, version),
                Stored(6, Widget, AccessActions.Read, ValidFilter, version: null),
                Stored(7, Widget, AccessActions.Read, "Nothing > 0", version),
                Stored(8, CoreResources.Wildcard, AccessActions.Wildcard),
                Stored(9, "fx.Gadget", AccessActions.Save, ValidFilter, version));
            Role everyone = RoleOf(
                isPublic: true,
                Stored(10, CoreResources.Wildcard, AccessActions.Wildcard),
                Stored(11, Widget, AccessActions.Read, ValidFilter, version));

            List<PermissionProblem> problems = RoleProblems.Of(new PermissionClassifier(Registry, new QueryexEngine()), [clerks, everyone], Schema);

            Assert.Equal(
                [
                    (2, AccessProblemCode.UnknownResource),
                    (3, AccessProblemCode.UnknownAction),
                    (4, AccessProblemCode.FilterUnsupported),
                    (5, AccessProblemCode.FilterUnsupported),
                    (6, AccessProblemCode.VersionUnsupported),
                    (7, AccessProblemCode.FilterInvalid),
                    (10, AccessProblemCode.WildcardResourceOnPublicRole),
                ],
                problems.Select(static problem => (problem.PermissionId, problem.Code)));

            // --- Only the invalid filter carries diagnostics, each in the one filter's clause over its text ---
            Assert.All(problems.Where(static problem => problem.Code != AccessProblemCode.FilterInvalid), static problem => Assert.Empty(problem.Diagnostics));
            QueryDiagnostic diagnostic = Assert.Single(problems.Single(static problem => problem.PermissionId == 7).Diagnostics);
            Assert.Equal("filters[0]", diagnostic.Clause);
            Assert.StartsWith("QX", diagnostic.Code, StringComparison.Ordinal);
            Assert.Equal((0, "Nothing".Length), (diagnostic.Start, diagnostic.Length));
        }

        [Fact]
        public void A_filter_refused_without_a_diagnostic_carries_the_role_save_s_code_and_each_text_is_validated_once()
        {
            // --- An engine that fails on one text, counting every validation ---
            QueryexEngine engine = new();
            int validations = 0;
            PermissionClassifier classifier = new(Registry, (text, options) =>
            {
                validations++;
                return text == "Amount > 13" ? throw new InvalidOperationException("The engine failed.") : engine.Validate(text, options);
            });
            int version = QueryexLanguage.Version;
            Role first = RoleOf(
                isPublic: false,
                Stored(1, Widget, AccessActions.Read, "Amount > 13", version),
                Stored(2, Widget, AccessActions.Save, ValidFilter, version));
            Role second = RoleOf(
                isPublic: false,
                Stored(3, Widget, AccessActions.Read, ValidFilter, version),
                Stored(4, Widget, AccessActions.Save, "Amount > 13", version));

            List<PermissionProblem> problems = RoleProblems.Of(classifier, [first, second], Schema);

            // --- Both copies of the failing text are refused with the save's own code and no span ---
            Assert.Equal([1, 4], problems.Select(static problem => problem.PermissionId));
            Assert.All(problems, static problem =>
            {
                Assert.Equal(AccessProblemCode.FilterInvalid, problem.Code);
                QueryDiagnostic only = Assert.Single(problem.Diagnostics);
                Assert.Equal(("filters[0]", AccessCodes.FilterInvalid, 0, 0), (only.Clause, only.Code, only.Start, only.Length));
                Assert.Empty(only.Arguments);
            });

            // --- One validation per root, text and version across the roles ---
            Assert.Equal(2, validations);
        }

        [Fact]
        public void A_role_without_permissions_loaded_and_no_roles_have_no_problems()
        {
            PermissionClassifier classifier = new(Registry, new QueryexEngine());

            Assert.Empty(RoleProblems.Of(classifier, [new Role { Id = 1, Name = "Empty" }], Schema));
            Assert.Empty(RoleProblems.Of<Role>(classifier, [], Schema));
        }

        /// <summary>A role with permissions.</summary>
        /// <param name="isPublic">Whether the role is public.</param>
        /// <param name="permissions">The permissions, in order.</param>
        /// <returns>The role.</returns>
        private static Role RoleOf(bool isPublic, params Permission[] permissions)
        {
            return new Role { Id = 1, Name = "Role", IsPublic = isPublic, Permissions = permissions };
        }

        /// <summary>A stored permission.</summary>
        /// <param name="id">The id.</param>
        /// <param name="resource">The stored resource.</param>
        /// <param name="action">The stored action.</param>
        /// <param name="filter">The stored filter; null for none.</param>
        /// <param name="version">The filter's stamp; null for none.</param>
        /// <returns>The permission.</returns>
        private static Permission Stored(int id, string resource, string action, string? filter = null, int? version = null)
        {
            return new Permission { Id = id, Resource = resource, Action = action, Filter = filter, FilterQueryexVersion = version };
        }
    }
}
