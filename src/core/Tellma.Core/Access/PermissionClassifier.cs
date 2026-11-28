// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Queryex;

namespace Tellma.Core.Access
{
    // The drift table is spec 0014 §9; the validation options are §3.7 rule 6 and §12.2.
    /// <summary>
    ///     Classifies one stored permission against the securable registry and a tenant's query
    ///     schema: the pair it grants, spelled as registered, or the problem that makes it grant
    ///     nothing. The permission set build, "who holds this" and the drift report classify alike.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The checks run in the drift table's order, the first failing one deciding: a public
    ///         role naming the wildcard resource; an unknown resource (aliases considered); an unknown
    ///         action; a filter where none binds (no filter root, the wildcard resource, or a resource
    ///         whose securables that take a filter do not share one root, since a filter on any
    ///         action also reaches reading); a missing or unsupported Queryex version; a filter the
    ///         engine does not validate against the tenant's schema.
    ///     </para>
    ///     <para>
    ///         The wildcard resource never names an unknown resource, and the wildcard pair
    ///         <c>*</c>/<c>*</c> never drifts but on a public role. The wildcard resource with a
    ///         concrete action needs an action some resource registers: a misspelt one would
    ///         otherwise imply reading every resource.
    ///     </para>
    /// </remarks>
    internal sealed class PermissionClassifier
    {
        /// <summary>The securable registry.</summary>
        private readonly ISecurableRegistry _registry;

        /// <summary>Validates one filter; the engine's own validation but in tests.</summary>
        private readonly Func<string, ValidationOptions, QueryexResult<ValidatedExpression>> _validate;

        /// <summary>Every registered action, ignoring case, with its registered spelling.</summary>
        private readonly Dictionary<string, string> _actions;

        /// <summary>Creates the classifier over the registry, validating with the engine.</summary>
        /// <param name="registry">The securable registry.</param>
        /// <param name="engine">The Queryex engine that validates filters.</param>
        public PermissionClassifier(ISecurableRegistry registry, QueryexEngine engine)
            : this(registry, (engine ?? throw new ArgumentNullException(nameof(engine))).Validate)
        {
        }

        /// <summary>Creates the classifier over the registry with a validation of its own, for tests.</summary>
        /// <param name="registry">The securable registry.</param>
        /// <param name="validate">Validates one filter text under the given options.</param>
        internal PermissionClassifier(ISecurableRegistry registry, Func<string, ValidationOptions, QueryexResult<ValidatedExpression>> validate)
        {
            ArgumentNullException.ThrowIfNull(registry);
            ArgumentNullException.ThrowIfNull(validate);

            _registry = registry;
            _validate = validate;
            _actions = RegisteredActions(registry);
            ActionResources = ActionResourceMap.From(registry.All);
        }

        /// <summary>The resources that register each action, frozen once from the registry and shared by every set.</summary>
        public ActionResourceMap ActionResources { get; }

        /// <summary>Classifies one stored permission.</summary>
        /// <param name="resource">The stored resource: a name, a former name or the wildcard.</param>
        /// <param name="action">The stored action: a name or the wildcard.</param>
        /// <param name="filter">The stored filter; null for none.</param>
        /// <param name="version">The filter's stored Queryex version.</param>
        /// <param name="isPublic">Whether the permission's role is public.</param>
        /// <param name="schema">The tenant's current query schema.</param>
        /// <param name="validations">
        ///     The validations already made by the caller, by filter root, text and version, which
        ///     this call reads and adds to; null to validate afresh.
        /// </param>
        /// <returns>The classification.</returns>
        public PermissionClassification Classify(
            string? resource,
            string? action,
            string? filter,
            int? version,
            bool isPublic,
            QueryexSchema schema,
            Dictionary<(string Root, string Text, int Version), Validation>? validations = null)
        {
            ArgumentNullException.ThrowIfNull(schema);
            string storedResource = resource ?? string.Empty;
            string storedAction = action ?? string.Empty;

            // --- A public role reaches every user, so it never holds the wildcard resource ---
            if (isPublic && storedResource == CoreResources.Wildcard)
            {
                return PermissionClassification.Drift(storedResource, storedAction, AccessProblemCode.WildcardResourceOnPublicRole);
            }

            // --- The wildcard resource: never a filter, and an action some resource registers ---
            if (storedResource == CoreResources.Wildcard)
            {
                string? wildcardAction = storedAction == AccessActions.Wildcard
                    ? AccessActions.Wildcard
                    : _actions.GetValueOrDefault(storedAction);
                return wildcardAction is null
                    ? PermissionClassification.Drift(storedResource, storedAction, AccessProblemCode.UnknownAction)
                    : filter is not null
                        ? PermissionClassification.Drift(storedResource, wildcardAction, AccessProblemCode.FilterUnsupported)
                        : PermissionClassification.Grant(storedResource, wildcardAction, null, null);
            }

            // --- A concrete resource, registered under its name or a former name ---
            if (_registry.FindResource(storedResource) is not string registered)
            {
                return PermissionClassification.Drift(storedResource, storedAction, AccessProblemCode.UnknownResource);
            }

            // --- The action: the wildcard, or one registered for the resource ---
            string resolvedAction;
            if (storedAction == AccessActions.Wildcard)
            {
                resolvedAction = AccessActions.Wildcard;
            }
            else if (_registry.Find(registered, storedAction) is SecurableDescriptor securable)
            {
                resolvedAction = securable.Action;
            }
            else
            {
                return PermissionClassification.Drift(registered, storedAction, AccessProblemCode.UnknownAction);
            }

            // --- Without a filter the row grants the pair unrestricted; a stray stamp means nothing ---
            if (filter is null)
            {
                return PermissionClassification.Grant(registered, resolvedAction, null, null);
            }

            // --- A filter needs the one root the resource's filterable securables share: a filter
            // on any action also reaches reading, so it must bind against the same entity there ---
            if (SecurableFilterRoots.Of(_registry, registered, resolvedAction) is not string root
                || SecurableFilterRoots.Shared(_registry.ForResource(registered)) is not string shared
                || !string.Equals(root, shared, StringComparison.OrdinalIgnoreCase))
            {
                return PermissionClassification.Drift(registered, resolvedAction, AccessProblemCode.FilterUnsupported);
            }

            // --- And a version this engine compiles ---
            if (version is not int stamp || !QueryexLanguage.IsSupported(stamp))
            {
                return PermissionClassification.Drift(registered, resolvedAction, AccessProblemCode.VersionUnsupported);
            }

            // --- The filter validates against the tenant's current schema, under its own stamp ---
            Validation validation = Validate(root, filter, stamp, schema, validations);
            return validation.IsValid && !string.IsNullOrWhiteSpace(filter)
                ? PermissionClassification.Grant(registered, resolvedAction, filter, stamp)
                : PermissionClassification.Drift(registered, resolvedAction, AccessProblemCode.FilterInvalid, validation.Diagnostics, validation.Failure);
        }

        /// <summary>Every registered action, with the spelling it was first registered with.</summary>
        /// <param name="registry">The registry.</param>
        /// <returns>The actions, ignoring case.</returns>
        private static Dictionary<string, string> RegisteredActions(ISecurableRegistry registry)
        {
            Dictionary<string, string> actions = new(StringComparer.OrdinalIgnoreCase);
            foreach (SecurableDescriptor securable in registry.All)
            {
                _ = actions.TryAdd(securable.Action, securable.Action);
            }

            return actions;
        }

        /// <summary>Validates a filter once per root, text and version.</summary>
        /// <param name="root">The filter root's name.</param>
        /// <param name="filter">The filter.</param>
        /// <param name="version">Its stamp.</param>
        /// <param name="schema">The tenant's schema.</param>
        /// <param name="validations">The caller's validations, or null.</param>
        /// <returns>The validation.</returns>
        private Validation Validate(
            string root,
            string filter,
            int version,
            QueryexSchema schema,
            Dictionary<(string Root, string Text, int Version), Validation>? validations)
        {
            // --- The root as the schema names it, so one root keys one validation ---
            if (schema.FindEntity(root) is not EntityDescriptor entity)
            {
                // The startup gate checks every root against the model; a tenant schema without
                // one cannot validate anything over it.
                return new Validation(false, [], null);
            }

            (string Root, string Text, int Version) key = (entity.Name, filter, version);
            if (validations is not null && validations.TryGetValue(key, out Validation? known))
            {
                return known;
            }

            Validation validation;
            try
            {
                // The options of a stored filter: a predicate over the root, under its own stamp,
                // with a user and no parameters, within the profile of one stored filter.
                QueryexResult<ValidatedExpression> result = _validate(filter, new ValidationOptions
                {
                    Schema = schema,
                    Root = entity,
                    Mode = QueryexMode.Filter,
                    QueryexVersion = version,
                    HasUser = true,
                    Parameters = [],
                    Limits = QueryexProfiles.AccessLimits,
                });
                validation = new Validation(result.Succeeded && result.Diagnostics.Count == 0, result.Diagnostics, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The engine reports problems with input as diagnostics; an exception is a defect.
                // The row still grants nothing, and the failure goes to the drift log.
                validation = new Validation(false, [], exception);
            }

            validations?[key] = validation;
            return validation;
        }

        /// <summary>The engine's verdict on one filter.</summary>
        /// <param name="IsValid">Whether the filter validated without a diagnostic.</param>
        /// <param name="Diagnostics">The engine's diagnostics.</param>
        /// <param name="Failure">The exception the engine threw, which is a defect; null when it returned.</param>
        internal sealed record Validation(bool IsValid, IReadOnlyList<QueryexDiagnostic> Diagnostics, Exception? Failure);
    }
}
