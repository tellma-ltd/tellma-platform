// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The names of the access instruments, their tags and the closed sets of tag values, on the
    ///     platform's <see cref="MeterName" /> meter. Dashboards and alerts key on these strings, so
    ///     they never change. No tag carries a tenant, a user, a role or a permission, which are
    ///     structured log properties instead.
    /// </summary>
    public static class AccessTelemetryNames
    {
        /// <summary>The meter every Core instrument is created on.</summary>
        public const string MeterName = "Tellma.Core";

        /// <summary>Counter (<c>{decision}</c>): access decisions taken. Tagged <see cref="OutcomeTag" />.</summary>
        public const string Decisions = "tellma.access.decisions";

        /// <summary>Histogram (<c>s</c>): how long building one caller's permission set took. Untagged.</summary>
        public const string SetBuildDuration = "tellma.access.set.build.duration";

        /// <summary>Histogram (<c>{grant}</c>): the grants of each permission set built. Untagged.</summary>
        public const string SetGrants = "tellma.access.set.grants";

        /// <summary>
        ///     Counter (<c>{permission}</c>): stored permissions found to grant nothing, each once per
        ///     problem and version of the tenant's permissions. Tagged <see cref="ProblemTag" />.
        /// </summary>
        public const string PermissionsUnresolved = "tellma.access.permissions.unresolved";

        /// <summary>Counter (<c>{guard}</c>): security guards that refused a write. Tagged <see cref="GuardTag" />.</summary>
        public const string GuardsTriggered = "tellma.access.guards.triggered";

        /// <summary>Counter (<c>{response}</c>): successful responses that carried no access witness. Untagged.</summary>
        public const string WitnessMissing = "tellma.access.witness.missing";

        /// <summary>Counter (<c>{connect}</c>): connect prologue results. Tagged <see cref="ResultTag" />.</summary>
        public const string ConnectResults = "tellma.access.connect.results";

        /// <summary>Histogram (<c>s</c>): how long a connect prologue took. Tagged <see cref="VariantTag" />.</summary>
        public const string ConnectPrologueDuration = "tellma.access.connect.prologue.duration";

        /// <summary>The decision outcome tag: <see cref="OutcomeDenied" />, <see cref="OutcomeFiltered" /> or <see cref="OutcomeUnrestricted" />.</summary>
        public const string OutcomeTag = "outcome";

        /// <summary>
        ///     The problem tag: one of <see cref="ProblemUnknownResource" />,
        ///     <see cref="ProblemUnknownAction" />, <see cref="ProblemFilterUnsupported" />,
        ///     <see cref="ProblemFilterInvalid" />, <see cref="ProblemVersionUnsupported" /> and
        ///     <see cref="ProblemWildcardResourceOnPublicRole" />.
        /// </summary>
        public const string ProblemTag = "problem";

        /// <summary>The guard tag: the guard that refused a write.</summary>
        public const string GuardTag = "guard";

        /// <summary>The connect result tag.</summary>
        public const string ResultTag = "result";

        /// <summary>The connect prologue variant tag.</summary>
        public const string VariantTag = "variant";

        /// <summary>The outcome value of a denial.</summary>
        public const string OutcomeDenied = "denied";

        /// <summary>The outcome value of a filtered decision.</summary>
        public const string OutcomeFiltered = "filtered";

        /// <summary>The outcome value of an unrestricted decision.</summary>
        public const string OutcomeUnrestricted = "unrestricted";

        /// <summary>The problem value of <see cref="AccessProblemCode.UnknownResource" />.</summary>
        public const string ProblemUnknownResource = "unknown_resource";

        /// <summary>The problem value of <see cref="AccessProblemCode.UnknownAction" />.</summary>
        public const string ProblemUnknownAction = "unknown_action";

        /// <summary>The problem value of <see cref="AccessProblemCode.FilterUnsupported" />.</summary>
        public const string ProblemFilterUnsupported = "filter_unsupported";

        /// <summary>The problem value of <see cref="AccessProblemCode.FilterInvalid" />.</summary>
        public const string ProblemFilterInvalid = "filter_invalid";

        /// <summary>The problem value of <see cref="AccessProblemCode.VersionUnsupported" />.</summary>
        public const string ProblemVersionUnsupported = "version_unsupported";

        /// <summary>The problem value of <see cref="AccessProblemCode.WildcardResourceOnPublicRole" />.</summary>
        public const string ProblemWildcardResourceOnPublicRole = "wildcard_resource_on_public_role";

        /// <summary>The result value of a connection whose prologue confirmed every cached part.</summary>
        public const string ResultWarm = "warm";

        /// <summary>The result value of a connection with nothing cached for the caller.</summary>
        public const string ResultCold = "cold";

        /// <summary>The result value of a prologue that found the cached permission set stale.</summary>
        public const string ResultStalePermissions = "stale_permissions";

        /// <summary>The result value of a prologue that found the settings the batch composed under stale.</summary>
        public const string ResultStaleSettings = "stale_settings";

        /// <summary>The result value of a prologue that found the cached profile stale.</summary>
        public const string ResultStaleProfile = "stale_profile";

        /// <summary>The result value of a prologue that found no user for the caller.</summary>
        public const string ResultUnknownSubject = "unknown_subject";

        /// <summary>The result value of a prologue that found the caller's user deactivated.</summary>
        public const string ResultDeactivated = "deactivated";

        /// <summary>The result value of a call whose guard failed again after the batch was recomposed once.</summary>
        public const string ResultGuardThrash = "guard_thrash";

        /// <summary>The variant value of a prologue that resolves the caller by subject.</summary>
        public const string VariantSubject = "subject";

        /// <summary>The variant value of a prologue that resolves a user by id.</summary>
        public const string VariantUser = "user";

        /// <summary>The variant value of the platform's own prologue, which looks no user up.</summary>
        public const string VariantSystem = "system";
    }
}
