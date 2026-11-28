// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Access
{
    // The registry is spec 0014 §4.1, §4.3 and §4.4.
    /// <summary>
    ///     The securable registry: composed once, on first resolution, from every
    ///     <see cref="ISecurableContributor" /> in registration order, then frozen.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Composition never throws. A conflict (the same pair with two filter roots, two owners
    ///         or two features, a resource or action spelled two ways, a pair marked both sensitive and
    ///         not sensitive, an alias or a mark naming nothing registered) or a malformed name is kept
    ///         in <see cref="Problems" />, which the startup gate reports; the registry keeps the first
    ///         registration of a pair.
    ///     </para>
    ///     <para>
    ///         A contributor that throws is a problem too, and the registry then serves no securable
    ///         at all (<see cref="Failed" />): a partial registry would judge endpoints and grants
    ///         against registrations that are missing. Every other contributor still runs, so each
    ///         failing one is reported, once, since the registry is composed once.
    ///     </para>
    ///     <para>
    ///         <see cref="All" /> is ordered by resource, ignoring case, then by action: the platform's
    ///         actions first, in the order of <see cref="PlatformActionOrder" />, then any other
    ///         action, ignoring case. The client lists a resource's actions in this order.
    ///     </para>
    ///     <para>
    ///         A pair is sensitive when any registration says so, when it is one of the platform's
    ///         defaults, or when a contributor marks it sensitive, unless a contributor marks it not
    ///         sensitive. A pair marked both ways stays sensitive, besides being a problem.
    ///     </para>
    ///     <para>
    ///         Composition waits for the first resolution, not for the composition root, because a
    ///         contributor is a service and may read other services, as the stacks' contributor reads
    ///         the stack registry.
    ///     </para>
    /// </remarks>
    internal sealed class SecurableRegistry : ISecurableRegistry
    {
        /// <summary>The platform's sensitive securables, sensitive whenever registered unless a contributor marks them otherwise.</summary>
        internal static readonly IReadOnlySet<(string Resource, string Action)> CoreSensitive = new HashSet<(string Resource, string Action)>(
            [
                (CoreResources.User, AccessActions.Save),
                (CoreResources.User, AccessActions.Delete),
                (CoreResources.User, AccessActions.Activate),
                (CoreResources.User, AccessActions.Invite),
                (CoreResources.User, AccessActions.Credentials),
                (CoreResources.Role, AccessActions.Save),
                (CoreResources.Role, AccessActions.Delete),
                (CoreResources.Role, AccessActions.Activate),
                (CoreResources.SettingsGeneral, AccessActions.Save),
                (CoreResources.Schedule, AccessActions.Save),
                (CoreResources.Schedule, AccessActions.Delete),
                (CoreResources.Schedule, AccessActions.Activate),
            ],
            PairComparer.Instance);

        /// <summary>
        ///     The platform's actions in the order a resource's actions are listed: reading, then
        ///     writing, then the lifecycle and the rest. An action not listed follows them all.
        /// </summary>
        internal static readonly IReadOnlyList<string> PlatformActionOrder =
        [
            AccessActions.Read,
            AccessActions.Save,
            AccessActions.Delete,
            AccessActions.Activate,
            AccessActions.Invite,
            AccessActions.Preferences,
            AccessActions.Credentials,
            AccessActions.Retry,
            AccessActions.Cancel,
            AccessActions.Resume,
            AccessActions.Share,
            AccessActions.Publish,
            AccessActions.Transfer,
        ];

        private readonly Dictionary<(string Resource, string Action), SecurableDescriptor> _byPair = new(PairComparer.Instance);
        private readonly Dictionary<string, IReadOnlyList<SecurableDescriptor>> _byResource = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<CompositionProblem> _problems = [];

        /// <summary>Composes the registry from the registered contributors, each attributed to the feature that registered it.</summary>
        /// <param name="contributors">The contributors, in registration order.</param>
        /// <param name="record">The realised composition, which says which feature registered each contributor's type.</param>
        public SecurableRegistry(IEnumerable<ISecurableContributor> contributors, TellmaCompositionRecord record)
            : this(Attribute(contributors, record))
        {
        }

        /// <summary>Composes the registry from contributors already attributed to their features.</summary>
        /// <param name="contributors">The contributors, in order, each with the name of its feature.</param>
        internal SecurableRegistry(IEnumerable<(ISecurableContributor Contributor, string Feature)> contributors)
        {
            ArgumentNullException.ThrowIfNull(contributors);

            // --- Every contributor once, on behalf of its feature; then the builder closes for good ---
            SecurableRegistryBuilder builder = new();
            foreach ((ISecurableContributor contributor, string feature) in contributors)
            {
                builder.Contributor = feature;
                try
                {
                    contributor.Contribute(builder);
                }
                catch (Exception exception)
                {
                    // A contributor that throws is a bug in its feature. The rest still run, so every
                    // failing contributor is reported at once; what they registered is not served.
                    _problems.Add(Problem(
                        $"The securable contributor {TypeDisplay.Qualified(contributor.GetType())} of the feature '{feature}' failed with {exception.GetType().Name}: {exception.Message}",
                        "Fix the contributor so it registers its securables without throwing; until it does, no securable is registered."));
                }
            }

            builder.Freeze();

            // --- A failed composition serves nothing: no grant or endpoint is judged against a part ---
            if (_problems.Count > 0)
            {
                Failed = true;
                All = [];
                Resources = [];
                Fingerprint = ComputeFingerprint();
                return;
            }

            // --- The securables, merged by pair; then the aliases and the sensitivity marks ---
            Dictionary<(string Resource, string Action), Entry> entries = Merge(builder.Securables);
            ComposeAliases(builder.Aliases, entries);
            (HashSet<(string Resource, string Action)> sensitive, HashSet<(string Resource, string Action)> notSensitive) = ComposeMarks(builder.Marks, entries);

            // --- The descriptors, by resource ignoring case, then the platform's actions in their
            // order and any other action ignoring case; registration order never matters ---
            All =
            [
                .. entries.Values
                    .Select(entry => entry.Describe(
                        (entry.IsSensitive || CoreSensitive.Contains(entry.Pair) || sensitive.Contains(entry.Pair)) && (!notSensitive.Contains(entry.Pair) || sensitive.Contains(entry.Pair))))
                    .OrderBy(static descriptor => descriptor.Resource, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static descriptor => ActionRank(descriptor.Action))
                    .ThenBy(static descriptor => descriptor.Action, StringComparer.OrdinalIgnoreCase),
            ];

            foreach (SecurableDescriptor descriptor in All)
            {
                _byPair.Add((descriptor.Resource, descriptor.Action), descriptor);
            }

            foreach (IGrouping<string, SecurableDescriptor> resource in All.GroupBy(static descriptor => descriptor.Resource, StringComparer.OrdinalIgnoreCase))
            {
                _byResource.Add(resource.Key, [.. resource]);
            }

            Resources = [.. _byResource.Keys];
            Fingerprint = ComputeFingerprint();
        }

        /// <inheritdoc />
        public IReadOnlyList<SecurableDescriptor> All { get; }

        /// <inheritdoc />
        public IReadOnlyList<string> Resources { get; }

        /// <inheritdoc />
        public string Fingerprint { get; }

        /// <summary>What composition found wrong with the registrations, for the startup gate.</summary>
        internal IReadOnlyList<CompositionProblem> Problems => _problems;

        /// <summary>Whether a contributor threw, so the registry serves no securable and nothing can be judged against it.</summary>
        internal bool Failed { get; }

        /// <summary>The aliases of renamed resources: each former name with the registered resource it now is.</summary>
        internal IReadOnlyDictionary<string, string> Aliases => _aliases;

        /// <inheritdoc />
        public SecurableDescriptor? Find(string resource, string action)
        {
            if (resource is null || action is null)
            {
                return null;
            }

            // A registered name wins; a former name resolves one hop, to a registered resource.
            return _byPair.TryGetValue((resource, action), out SecurableDescriptor? found)
                || (_aliases.TryGetValue(resource, out string? renamed) && _byPair.TryGetValue((renamed, action), out found))
                ? found
                : null;
        }

        /// <inheritdoc />
        public string? FindResource(string resource)
        {
            if (resource is null)
            {
                return null;
            }

            // A registered name wins and comes back as registered; a former name resolves one hop.
            return _byResource.TryGetValue(resource, out IReadOnlyList<SecurableDescriptor>? securables)
                || (_aliases.TryGetValue(resource, out string? renamed) && _byResource.TryGetValue(renamed, out securables))
                ? securables[0].Resource
                : null;
        }

        /// <inheritdoc />
        public IReadOnlyList<SecurableDescriptor> ForResource(string resource)
        {
            return resource is not null && _byResource.TryGetValue(resource, out IReadOnlyList<SecurableDescriptor>? securables) ? securables : [];
        }

        /// <summary>
        ///     The registry a startup check judges against: the composed one, or none, with whether
        ///     composing it failed. The securables check reports the failure; every other check skips
        ///     what needs the registry and points to that report.
        /// </summary>
        /// <param name="services">The application's services.</param>
        /// <returns>
        ///     The registry, null when none is registered or when composing it failed; and whether it
        ///     failed, its construction having thrown or a contributor having thrown.
        /// </returns>
        internal static (ISecurableRegistry? Registry, bool Failed) ForChecks(IServiceProvider services)
        {
            ArgumentNullException.ThrowIfNull(services);
            ISecurableRegistry? registry;
            try
            {
                registry = services.GetService<ISecurableRegistry>();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A contributor the container could not build: the securables check reports why.
                return (null, true);
            }

            return registry is SecurableRegistry { Failed: true } ? (null, true) : (registry, false);
        }

        /// <summary>An action's rank in a resource's list: its place among the platform's actions, else after them all.</summary>
        /// <param name="action">The action.</param>
        /// <returns>The rank.</returns>
        private static int ActionRank(string action)
        {
            for (int index = 0; index < PlatformActionOrder.Count; index++)
            {
                if (string.Equals(PlatformActionOrder[index], action, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return PlatformActionOrder.Count;
        }

        /// <summary>
        ///     Pairs each contributor with its feature: the feature of a contribution item, else the
        ///     feature that registered the contributor's type, else <c>core</c>.
        /// </summary>
        /// <param name="contributors">The contributors, in registration order.</param>
        /// <param name="record">The realised composition.</param>
        /// <returns>The contributors with their features.</returns>
        private static IEnumerable<(ISecurableContributor Contributor, string Feature)> Attribute(
            IEnumerable<ISecurableContributor> contributors, TellmaCompositionRecord record)
        {
            ArgumentNullException.ThrowIfNull(contributors);
            ArgumentNullException.ThrowIfNull(record);

            // A type registered by several features is attributed to the first of them; a factory or a
            // host registration names no type, and its securables name their feature or are core's.
            Dictionary<Type, string> owners = [];
            foreach (ContributedService service in record.Services)
            {
                if (service.ServiceType == typeof(ISecurableContributor) && service.ImplementationType is Type type)
                {
                    _ = owners.TryAdd(type, service.Owner.Name);
                }
            }

            return contributors.Select(contributor => (contributor, contributor is FeatureSecurableContributor item
                ? item.Feature
                : owners.GetValueOrDefault(contributor.GetType(), CoreFeature.FeatureName)));
        }

        /// <summary>Merges the registered securables by pair, reporting malformed names and conflicting registrations.</summary>
        /// <param name="declarations">The registrations, in order.</param>
        /// <returns>One entry per pair, the first registration's.</returns>
        private Dictionary<(string Resource, string Action), Entry> Merge(IReadOnlyList<SecurableDeclaration> declarations)
        {
            Dictionary<(string Resource, string Action), Entry> entries = new(PairComparer.Instance);
            Dictionary<string, (string Spelling, string Contributor)> resources = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> judged = new(StringComparer.Ordinal);
            HashSet<string> respelled = new(StringComparer.Ordinal);

            foreach (SecurableDeclaration declaration in declarations)
            {
                string described = $"The securable '{declaration.Resource}:{declaration.Action}' of the feature '{declaration.Contributor}'";
                CheckResource(declaration.Resource, described, judged);
                CheckAction(declaration.Action, described, judged);

                // --- One spelling per resource, whichever actions name it ---
                if (!resources.TryGetValue(declaration.Resource, out (string Spelling, string Contributor) first))
                {
                    first = (declaration.Resource, declaration.Contributor);
                    resources.Add(declaration.Resource, first);
                }
                else if (!string.Equals(first.Spelling, declaration.Resource, StringComparison.Ordinal) && respelled.Add(declaration.Resource))
                {
                    _problems.Add(Problem(
                        $"The resource '{first.Spelling}' of the feature '{first.Contributor}' is registered as '{declaration.Resource}' by the feature '{declaration.Contributor}'.",
                        $"Spell the resource '{first.Spelling}' everywhere: resources compare ignoring case, and the client shows the spelling registered."));
                }

                // --- A pair registered again is the same securable: same action spelling, filter root and owner ---
                string feature = declaration.Feature ?? declaration.Contributor;
                if (!entries.TryGetValue((declaration.Resource, declaration.Action), out Entry? entry))
                {
                    entries.Add((declaration.Resource, declaration.Action), new Entry(first.Spelling, declaration, feature));
                    continue;
                }

                string pair = $"'{entry.Resource}:{entry.Action}'";
                if (!string.Equals(entry.Action, declaration.Action, StringComparison.Ordinal) && respelled.Add($"{declaration.Resource}:{declaration.Action}"))
                {
                    _problems.Add(Problem(
                        $"The securable {pair} of the feature '{entry.Contributor}' is registered with the action '{declaration.Action}' by the feature '{declaration.Contributor}'.",
                        $"Spell the action '{entry.Action}' everywhere: actions compare ignoring case, and the client shows the spelling registered."));
                }

                if (!string.Equals(entry.FilterRoot, declaration.FilterRoot, StringComparison.OrdinalIgnoreCase))
                {
                    _problems.Add(Problem(
                        $"The securable {pair} is registered with the filter root {Quote(entry.FilterRoot)} by the feature '{entry.Contributor}' and with {Quote(declaration.FilterRoot)} by the feature '{declaration.Contributor}'.",
                        "Register each securable with one filter root: an entity's securables filter on the entity itself, and a securable that takes no filter has none."));
                }

                if (!SameOwner(entry.Owner, declaration.Owner))
                {
                    _problems.Add(Problem(
                        $"The securable {pair} is registered with the owner {Describe(entry.Owner)} by the feature '{entry.Contributor}' and with {Describe(declaration.Owner)} by the feature '{declaration.Contributor}'.",
                        "Register each securable with one owner, or none."));
                }

                // The feature is part of the securable: the editor groups the pair under it.
                if (!string.Equals(entry.Feature, feature, StringComparison.Ordinal))
                {
                    _problems.Add(Problem(
                        $"The securable {pair} is registered under the feature '{entry.Feature}' by the feature '{entry.Contributor}' and under the feature '{feature}' by the feature '{declaration.Contributor}'.",
                        "Register each securable under one feature, the one whose code it guards: the permissions editor groups it there."));
                }

                // Any registration may make the pair sensitive.
                entry.IsSensitive |= declaration.IsSensitive;
            }

            return entries;
        }

        /// <summary>Composes the aliases, reporting those that name no registered resource or rename one still registered.</summary>
        /// <param name="declarations">The aliases, in order.</param>
        /// <param name="entries">The merged securables.</param>
        private void ComposeAliases(IReadOnlyList<SecurableAliasDeclaration> declarations, Dictionary<(string Resource, string Action), Entry> entries)
        {
            HashSet<string> registered = new(entries.Keys.Select(static pair => pair.Resource), StringComparer.OrdinalIgnoreCase);
            HashSet<string> judged = new(StringComparer.Ordinal);
            foreach (SecurableAliasDeclaration alias in declarations)
            {
                string described = $"The alias '{alias.OldResource}' → '{alias.NewResource}' of the feature '{alias.Contributor}'";
                bool wellFormed = CheckResource(alias.OldResource, described, judged) & CheckResource(alias.NewResource, described, judged);
                if (!wellFormed)
                {
                    continue;
                }

                // --- The former name is no longer registered, and the new one is ---
                if (string.Equals(alias.OldResource, alias.NewResource, StringComparison.OrdinalIgnoreCase))
                {
                    _problems.Add(Problem($"{described} renames a resource to itself.", "Remove the alias."));
                    continue;
                }

                if (registered.Contains(alias.OldResource))
                {
                    _problems.Add(Problem(
                        $"{described} renames '{alias.OldResource}', which is still a registered resource.",
                        "Alias a resource's former name only, once no feature registers it under that name."));
                    continue;
                }

                if (!registered.Contains(alias.NewResource))
                {
                    _problems.Add(Problem(
                        $"{described} resolves to '{alias.NewResource}', which is not a registered resource.",
                        "Alias the former name to the registered resource it became; an alias resolves one step, never to another alias."));
                    continue;
                }

                // --- One target per former name; the same alias again is the same alias ---
                if (_aliases.TryGetValue(alias.OldResource, out string? target))
                {
                    if (!string.Equals(target, alias.NewResource, StringComparison.OrdinalIgnoreCase))
                    {
                        _problems.Add(Problem(
                            $"{described} renames '{alias.OldResource}', which is already an alias of '{target}'.",
                            "Alias each former name to one registered resource."));
                    }

                    continue;
                }

                _aliases.Add(alias.OldResource, alias.NewResource);
            }
        }

        /// <summary>Composes the sensitivity marks, reporting marks of unregistered pairs and pairs marked both ways.</summary>
        /// <param name="declarations">The marks, in order.</param>
        /// <param name="entries">The merged securables.</param>
        /// <returns>The pairs marked sensitive, and the pairs marked not sensitive.</returns>
        private (HashSet<(string Resource, string Action)> Sensitive, HashSet<(string Resource, string Action)> NotSensitive) ComposeMarks(
            IReadOnlyList<SecurableMarkDeclaration> declarations, Dictionary<(string Resource, string Action), Entry> entries)
        {
            HashSet<(string Resource, string Action)> sensitive = new(PairComparer.Instance);
            HashSet<(string Resource, string Action)> notSensitive = new(PairComparer.Instance);
            Dictionary<(string Resource, string Action), string> markers = new(PairComparer.Instance);
            HashSet<string> judged = new(StringComparer.Ordinal);
            HashSet<(string Resource, string Action)> conflicting = new(PairComparer.Instance);

            foreach (SecurableMarkDeclaration mark in declarations)
            {
                string kind = mark.Sensitive ? "sensitive" : "not sensitive";
                string described = $"The feature '{mark.Contributor}' marks '{mark.Resource}:{mark.Action}' {kind}";
                if (!(CheckResource(mark.Resource, described, judged) & CheckAction(mark.Action, described, judged)))
                {
                    continue;
                }

                // --- A mark changes a registered securable; it registers nothing ---
                (string Resource, string Action) pair = (mark.Resource, mark.Action);
                if (!entries.ContainsKey(pair))
                {
                    _problems.Add(Problem(
                        $"{described}, which is not a registered securable.",
                        "Mark a pair some feature registers, spelled as registered, or remove the mark."));
                    continue;
                }

                // --- Both ways is a contradiction, reported once per pair ---
                HashSet<(string Resource, string Action)> marked = mark.Sensitive ? sensitive : notSensitive;
                HashSet<(string Resource, string Action)> opposite = mark.Sensitive ? notSensitive : sensitive;
                _ = marked.Add(pair);
                if (opposite.Contains(pair) && conflicting.Add(pair))
                {
                    _problems.Add(Problem(
                        $"The securable '{mark.Resource}:{mark.Action}' is marked {(mark.Sensitive ? "not sensitive" : "sensitive")} by the feature '{markers[pair]}' and {kind} by the feature '{mark.Contributor}'.",
                        "Mark each pair one way: MarkSensitive adds it to the sensitive set, MarkNotSensitive removes it, the platform's defaults included."));
                }

                _ = markers.TryAdd(pair, mark.Contributor);
            }

            return (sensitive, notSensitive);
        }

        /// <summary>Reports a malformed resource once per spelling.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="described">What names it, for the problem.</param>
        /// <param name="judged">The names already reported.</param>
        /// <returns>True when the resource is well formed.</returns>
        private bool CheckResource(string resource, string described, HashSet<string> judged)
        {
            if (SecurableNames.IsResource(resource))
            {
                return true;
            }

            if (judged.Add(resource))
            {
                _problems.Add(Problem(
                    SecurableNames.IsWildcard(resource)
                        ? $"{described} names the wildcard '*' as a resource."
                        : $"{described} names the resource '{resource}', which is not well formed.",
                    $"Name a resource with two or more dot-separated segments, each an ASCII letter followed by letters or digits, at most {SecurableNames.MaxResourceLength} characters, such as 'gl.Center'; '*' is the wildcard of stored permissions, never a registered name."));
            }

            return false;
        }

        /// <summary>Reports a malformed action once per spelling.</summary>
        /// <param name="action">The action.</param>
        /// <param name="described">What names it, for the problem.</param>
        /// <param name="judged">The names already reported.</param>
        /// <returns>True when the action is well formed.</returns>
        private bool CheckAction(string action, string described, HashSet<string> judged)
        {
            if (SecurableNames.IsAction(action))
            {
                return true;
            }

            // The key keeps an action apart from a resource of the same spelling in one judged set.
            if (judged.Add("action:" + action))
            {
                _problems.Add(Problem(
                    SecurableNames.IsWildcard(action)
                        ? $"{described} names the wildcard '*' as an action."
                        : $"{described} names the action '{action}', which is not well formed.",
                    $"Name an action with a PascalCase identifier of ASCII letters and digits, at most {SecurableNames.MaxActionLength} characters, such as 'Save'; '*' is the wildcard of stored permissions, never a registered name."));
            }

            return false;
        }

        /// <summary>
        ///     The hex SHA-256 of every securable's line and every alias's line, sorted ordinally, so the
        ///     order of registration never changes it.
        /// </summary>
        /// <returns>The fingerprint.</returns>
        private string ComputeFingerprint()
        {
            // An owner and an alias change what a grant reaches as much as a filter root does, so
            // their lines join the securables'. A resource has a dot and "alias" none, so the two
            // kinds of line never collide.
            List<string> lines =
            [
                .. All.Select(static descriptor => string.Join(
                    '|',
                    descriptor.Resource,
                    descriptor.Action,
                    descriptor.FilterRoot ?? string.Empty,
                    descriptor.IsSensitive ? "true" : "false",
                    descriptor.Owner?.Resource ?? string.Empty,
                    descriptor.Owner?.Navigation ?? string.Empty)),
                .. _aliases.Select(static alias => $"alias|{alias.Key}|{alias.Value}"),
            ];
            lines.Sort(StringComparer.Ordinal);
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
        }

        /// <summary>Whether two owners are the same, ignoring case.</summary>
        /// <param name="left">One owner.</param>
        /// <param name="right">The other.</param>
        /// <returns>True when both are null or both name the same resource and navigation.</returns>
        private static bool SameOwner(SecurableOwner? left, SecurableOwner? right)
        {
            return left is null
                ? right is null
                : right is not null
                    && string.Equals(left.Resource, right.Resource, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(left.Navigation, right.Navigation, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A filter root as a problem names it.</summary>
        /// <param name="filterRoot">The filter root.</param>
        /// <returns>The quoted root, or "none".</returns>
        private static string Quote(string? filterRoot)
        {
            return filterRoot is null ? "none" : $"'{filterRoot}'";
        }

        /// <summary>An owner as a problem names it.</summary>
        /// <param name="owner">The owner.</param>
        /// <returns>The owner's resource and navigation, or "none".</returns>
        private static string Describe(SecurableOwner? owner)
        {
            return owner is null ? "none" : $"'{owner.Resource}' through '{owner.Navigation}'";
        }

        /// <summary>A problem of the securables check.</summary>
        /// <param name="problem">What is wrong.</param>
        /// <param name="fix">How to fix it.</param>
        /// <returns>The problem.</returns>
        private static CompositionProblem Problem(string problem, string fix)
        {
            return new CompositionProblem(CoreCheckNames.Securables, problem, fix);
        }

        /// <summary>Compares securable pairs as the registry does: ordinal, ignoring case.</summary>
        internal sealed class PairComparer : IEqualityComparer<(string Resource, string Action)>
        {
            /// <summary>The one instance.</summary>
            public static readonly PairComparer Instance = new();

            /// <inheritdoc />
            public bool Equals((string Resource, string Action) x, (string Resource, string Action) y)
            {
                return string.Equals(x.Resource, y.Resource, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Action, y.Action, StringComparison.OrdinalIgnoreCase);
            }

            /// <inheritdoc />
            public int GetHashCode((string Resource, string Action) obj)
            {
                return HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Resource),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Action));
            }
        }

        /// <summary>A pair while the registrations merge: the first registration, made sensitive by any.</summary>
        /// <param name="resource">The resource, as first spelled.</param>
        /// <param name="first">The first registration.</param>
        /// <param name="feature">The feature the first registration attributes it to.</param>
        private sealed class Entry(string resource, SecurableDeclaration first, string feature)
        {
            /// <summary>The resource, as first spelled.</summary>
            public string Resource { get; } = resource;

            /// <summary>The action, as first spelled.</summary>
            public string Action { get; } = first.Action;

            /// <summary>The pair.</summary>
            public (string Resource, string Action) Pair => (Resource, Action);

            /// <summary>The filter root.</summary>
            public string? FilterRoot { get; } = first.FilterRoot;

            /// <summary>The owner.</summary>
            public SecurableOwner? Owner { get; } = first.Owner;

            /// <summary>The name of the feature whose contributor registered it first.</summary>
            public string Contributor { get; } = first.Contributor;

            /// <summary>The feature the securable belongs to: the one its first registration names, else its contributor's.</summary>
            public string Feature { get; } = feature;

            /// <summary>Whether any registration made it sensitive.</summary>
            public bool IsSensitive { get; set; } = first.IsSensitive;

            /// <summary>The descriptor.</summary>
            /// <param name="isSensitive">Whether the pair ends up sensitive.</param>
            /// <returns>The descriptor.</returns>
            public SecurableDescriptor Describe(bool isSensitive)
            {
                return new SecurableDescriptor(Resource, Action, FilterRoot, isSensitive, Feature, Owner);
            }
        }
    }
}
