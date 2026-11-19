// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Access
{
    // Spec 0014 §8.3-§8.4: every guard that refuses a write is counted on guards.triggered and
    // logged, the validators' self-protection and escalation rules included.
    /// <summary>
    ///     What the users' and roles' access rules share: where an error on a delete's or an action's
    ///     target lands, and how a refusal by one of their guards is counted and logged.
    /// </summary>
    internal static class AccessRefusals
    {
        /// <summary>The root property a delete's or an action's target ids are reported under.</summary>
        public const string IdsProperty = "Ids";

        /// <summary>
        ///     The path of a delete's or an action's target: <c>Ids[i]</c>, at the first position the
        ///     request names the id, else at the target's own position.
        /// </summary>
        /// <param name="ids">The ids as the request named them; null when the context does not carry them.</param>
        /// <param name="id">The target's id.</param>
        /// <param name="position">The target's position among the loaded targets.</param>
        /// <returns>The path.</returns>
        public static ValidationPath Target(IReadOnlyList<int>? ids, int id, int position)
        {
            int index = ids is null ? -1 : IndexOf(ids, id);
            return ValidationPath.Root.Property(IdsProperty).Index(index < 0 ? position : index);
        }

        /// <summary>Counts and logs a validator's self-protection refusal.</summary>
        /// <param name="metrics">The access instruments.</param>
        /// <param name="logger">The access logger.</param>
        /// <param name="context">Who wrote.</param>
        /// <param name="guard">The guard's tag value.</param>
        /// <param name="code">The code of the refusal.</param>
        public static void Guard(AccessMetrics metrics, ILogger logger, RequestContext context, string guard, string code)
        {
            metrics.CountGuard(guard);
            AccessLog.GuardTriggered(logger, context.Tenant?.Id ?? 0, context.UserId, guard, code);
        }

        /// <summary>Counts and logs an escalation refusal.</summary>
        /// <param name="metrics">The access instruments.</param>
        /// <param name="logger">The access logger.</param>
        /// <param name="context">Who wrote.</param>
        /// <param name="roleId">The role saved, or the role a membership grants; 0 for a role not yet saved.</param>
        /// <param name="resource">The permission's resource.</param>
        /// <param name="action">The permission's action.</param>
        /// <param name="code">The code of the refusal.</param>
        public static void Escalation(AccessMetrics metrics, ILogger logger, RequestContext context, int roleId, string resource, string action, string code)
        {
            metrics.CountGuard(AccessTelemetryNames.GuardEscalation);
            AccessLog.EscalationRejected(logger, context.Tenant?.Id ?? 0, context.UserId ?? 0, roleId, resource, action, code);
        }

        /// <summary>The arguments of an error, by name.</summary>
        /// <param name="pairs">The arguments.</param>
        /// <returns>The dictionary.</returns>
        public static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] pairs)
        {
            Dictionary<string, object?> arguments = new(StringComparer.Ordinal);
            foreach ((string name, object? value) in pairs)
            {
                arguments[name] = value;
            }

            return arguments;
        }

        /// <summary>The first position of an id in a list.</summary>
        /// <param name="ids">The list.</param>
        /// <param name="id">The id.</param>
        /// <returns>The position, or -1.</returns>
        private static int IndexOf(IReadOnlyList<int> ids, int id)
        {
            for (int index = 0; index < ids.Count; index++)
            {
                if (ids[index] == id)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
