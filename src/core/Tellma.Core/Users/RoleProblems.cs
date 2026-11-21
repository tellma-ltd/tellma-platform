// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Access;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;

namespace Tellma.Core.Users
{
    // Spec 0018 §4.3's problems extra: spec 0014 §9's drift table, run by the classifier the
    // permission set build runs, so a permission is listed here exactly when it grants nothing there.
    // Its diagnostics are located as the Queryex endpoint's permission profile locates a filter being
    // typed (spec 0029 §9), so the role editor shows a stored problem and a live one alike.
    /// <summary>
    ///     The drifted permissions of roles: each stored permission that grants nothing against the
    ///     deployment's securables and a tenant's current schema, and why, in role order then
    ///     permission order.
    /// </summary>
    internal static class RoleProblems
    {
        /// <summary>Where a stored filter's diagnostics land: the one filter the permission profile sends.</summary>
        private static readonly CompositionMap FilterMap = new([new Conjunct(ConjunctKind.UserFilter, 0)], SelectSent: false, OrderingSource.Sent);

        /// <summary>The drifted permissions of the roles.</summary>
        /// <typeparam name="TRole">The role leaf.</typeparam>
        /// <param name="classifier">The permission classifier.</param>
        /// <param name="roles">The roles, each with its permissions loaded.</param>
        /// <param name="schema">The tenant's current query schema.</param>
        /// <returns>One problem per drifted permission, in role order then permission order.</returns>
        public static List<PermissionProblem> Of<TRole>(PermissionClassifier classifier, IReadOnlyList<TRole> roles, QueryexSchema schema)
            where TRole : Role
        {
            ArgumentNullException.ThrowIfNull(classifier);
            ArgumentNullException.ThrowIfNull(roles);
            ArgumentNullException.ThrowIfNull(schema);

            // --- Each permission through the drift table; one validation per root, text and version ---
            Dictionary<(string Root, string Text, int Version), PermissionClassifier.Validation> validations = [];
            List<PermissionProblem> problems = [];
            foreach (TRole role in roles)
            {
                foreach (Permission permission in role.Permissions ?? [])
                {
                    PermissionClassification classification = classifier.Classify(
                        permission.Resource, permission.Action, permission.Filter, permission.FilterQueryexVersion, role.IsPublic, schema, validations);
                    if (classification.Problem is AccessProblemCode code)
                    {
                        problems.Add(new PermissionProblem(permission.Id, code, code == AccessProblemCode.FilterInvalid ? Diagnostics(classification) : []));
                    }
                }
            }

            return problems;
        }

        /// <summary>The wire diagnostics of an invalid filter, located in its text; one with no span when the engine reported none.</summary>
        /// <param name="classification">The classification.</param>
        /// <returns>The diagnostics, never empty.</returns>
        private static List<QueryDiagnostic> Diagnostics(PermissionClassification classification)
        {
            List<QueryDiagnostic> diagnostics = [.. classification.Diagnostics.Select(static diagnostic => QueryWireDiagnostics.ToWire(diagnostic, FilterMap))];
            if (diagnostics.Count == 0)
            {
                // Refused with no diagnostic to show (an engine failure, or a root the schema lacks),
                // as the role save and the permission profile refuse it.
                diagnostics.Add(new QueryDiagnostic(
                    QueryDiagnostics.UserFilter(0), AccessCodes.FilterInvalid, 0, 0, Location: null, new Dictionary<string, object?>(StringComparer.Ordinal)));
            }

            return diagnostics;
        }
    }
}
