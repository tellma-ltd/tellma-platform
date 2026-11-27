// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Access;
using Tellma.Core.Blobs;
using Tellma.Core.Caching;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Composition.Realizers;
using Tellma.Core.Crud;
using Tellma.Core.Dashboards;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Excel;
using Tellma.Core.Identity;
using Tellma.Core.Jobs;
using Tellma.Core.Localization;
using Tellma.Core.Notifications;
using Tellma.Core.Outbox;
using Tellma.Core.Provisioning;
using Tellma.Core.Reports;
using Tellma.Core.Sharing;
using Tellma.Core.Tenancy;
using Tellma.Core.Users;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     One run of the composition pipeline: collect, declare, the graph gate, then realise, which
    ///     installs the realised gate.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Collect" /> runs the composition method and every feature's
    ///         <see cref="ITellmaFeature.Declare" /> and <see cref="ITellmaFeature.Contribute" />, and
    ///         returns the graph gate's problems. Contributions are data-only, so collecting them
    ///         before the order is known changes nothing, and it is what lets a nested feature join
    ///         the graph gate: its edges are validated and ordered with the rest before anything is
    ///         realised.
    ///     </para>
    ///     <para>
    ///         <see cref="Realise" /> runs only after a clean graph gate. It registers the built-in
    ///         startup checks, realises every item in topological order, binds the declared options
    ///         and returns the realizers' problems; only when there are none does it install the
    ///         realised gate.
    ///     </para>
    /// </remarks>
    /// <param name="slug">The distribution slug.</param>
    /// <param name="services">The services to realise into.</param>
    /// <param name="configuration">The host's configuration.</param>
    /// <param name="environment">The host's environment.</param>
    /// <param name="realizers">The realizers of this composition.</param>
    /// <param name="icuProbe">Reports whether the process runs ICU, for <c>core.globalization</c>.</param>
    internal sealed class CompositionPipeline(
        string slug,
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        ContributionRealizerSet realizers,
        Func<bool> icuProbe)
    {
        private readonly FeatureSet _features = new();
        private readonly TellmaCompositionRecord _record = new(slug, configuration, environment);
        private TellmaBuilder? _builder;
        private bool _graphSound;

        /// <summary>Steps 1 to 3: collect, declare and the graph gate.</summary>
        /// <param name="compose">The distribution's composition method.</param>
        /// <returns>Every graph-gate problem; empty when realisation may proceed.</returns>
        /// <exception cref="InvalidOperationException">The services already hold a Tellma composition.</exception>
        public IReadOnlyList<CompositionProblem> Collect(Action<TellmaBuilder> compose)
        {
            // A second composition would register a second gate and record over the same services,
            // with every check running twice against whichever record resolved last.
            if (services.Any(static descriptor => descriptor.ServiceType == typeof(TellmaCompositionRecord)))
            {
                throw new InvalidOperationException("AddTellma was already called on these services; a host composes Tellma once.");
            }

            List<CompositionProblem> problems = [];

            // --- Step 1: collect ---
            services.AddSingleton(_record);
            RegisterDeploymentIdentity(problems);
            services.TryAddSingleton(DeploymentVersionsReader.Read(compose));
            CheckDistributionVersion(problems);

            _ = _features.Add(new CoreFeature());
            _builder = new TellmaBuilder(services, configuration, environment, _features);
            compose(_builder);
            _builder.Seal();

            // --- Step 2: declare and collect the contributions, nested features included ---
            CollectFeatures();

            // --- Step 3: the graph gate ---
            problems.AddRange(_features.Problems);
            problems.AddRange(_builder.Problems);
            problems.AddRange(CheckGraph());

            _graphSound = problems.Count == 0;
            return problems;
        }

        /// <summary>Step 4: registers the built-in checks, realises the composition and, when that reports nothing, installs the realised gate.</summary>
        /// <returns>Every realisation problem; empty when the composition is complete.</returns>
        /// <exception cref="InvalidOperationException"><see cref="Collect" /> did not run, or reported problems.</exception>
        public IReadOnlyList<CompositionProblem> Realise()
        {
            // Realising over an unsound graph could not order the features, and would realise items
            // of a composition that is about to be refused anyway.
            if (!_graphSound || _builder is null)
            {
                throw new InvalidOperationException("Only a composition that passed the graph gate can be realised.");
            }

            IReadOnlyList<ComposedFeature> ordered = OrderFeatures();
            LanguageDeclaration languages = _builder.LanguageDeclaration;
            _record.Settle([.. ordered.Select(static feature => feature.Instance)], _builder.Leaves, languages, TransitiveDependencies());

            // --- What the distribution declared about languages, which the language catalogue reads ---
            services.AddSingleton(languages);

            // --- Built-in startup checks, ahead of every contributed one: core is realised first ---
            RegisterBuiltInChecks();

            // --- Contribution items, dependencies first ---
            RealizationContext context = new(services, _record);
            foreach (ComposedFeature feature in ordered)
            {
                foreach (FeatureContributionItem item in feature.Contribution!.Items)
                {
                    realizers.Realize(item, feature.Instance, context);
                }
            }

            // --- What no single job item shows: a key registered twice, a retired key still in use ---
            JobContributionChecks.Check(context);

            // --- The host's blob-kind transforms, after every feature's (spec 0017 §2.4) ---
            _record.BlobKindTransforms.AddRange(_builder.BlobKindTransforms);

            // --- Declared options ---
            OptionsRegistrar options = new(context, configuration);
            foreach (ComposedFeature feature in ordered)
            {
                foreach (FeatureOptionsBinding binding in feature.OptionsBindings)
                {
                    options.Bind(binding, feature.Name!);
                }
            }

            // --- String localization: every localizer in the process renders ICU (spec 0013 §1.2) ---
            LocalizationComposition.Register(services);

            // --- The tenant model, built from what the realizers recorded ---
            TellmaModelFactory.Register(services, _record);

            // --- The tenancy runtime: the catalog, the registry, connections, the write side and the request context ---
            TenancyComposition.Register(services, _record, options);

            // --- The data layer: the batch executor, the scoped tenant database and the handle factory ---
            DataComposition.Register(services, options);

            // --- Caching: the options and the version tags' instruments ---
            CachingComposition.Register(services, options);

            // --- Access: the options, the classifier, the set builder, the connection and the evaluation (spec 0014 §5.1, §6.2, §12.1) ---
            AccessRuntimeComposition.Register(services, _record, options);

            // --- The identity-server client: its options and the named HTTP client (spec 0018 §2) ---
            IdentityComposition.Register(services, options, slug, configuration, environment);

            // --- Notifications: the options, and the hub-less client event publisher and status ---
            NotificationsComposition.Register(services, options);

            // --- The email outbox: the options, the instruments and the sender check (spec 0022 §9.1, §9.2, §9.3) ---
            OutboxComposition.Register(services, _record, options);

            // --- Background jobs: the options, the handler registry, the poller wake and the queue (spec 0020 §4, §12.1) ---
            JobsComposition.Register(services, _record, options);

            // --- The provisioning step runner, which the migrator drives (spec 0011 §6.2) ---
            services.TryAddSingleton<ProvisioningStepRunner>();

            // --- Stacks: the registry, its route source, the services and components it closes, its securables (spec 0015 §2) ---
            CrudComposition.Register(services, _record, ExcelCompanions.Instance);

            // --- Excel: the options, the exporter, the export runner, the instruments and the Excel checks (spec 0019 §1.2) ---
            ExcelComposition.Register(services, _record, options);

            // --- Blobs: the options, the kind registry, the staging service and the blobs check (spec 0017 §1.3, §9) ---
            BlobsRuntimeComposition.Register(services, _record, options);

            // --- The users' and roles' stacks: the instruments, the services' collaborators and the administrator's invitation (spec 0018 §3, §4, §6.1) ---
            UsersRuntimeComposition.Register(services, _record);

            // --- Sharing: the options and the shareables check (spec 0033 §5.1, §14) ---
            SharingComposition.Register(services, _record, options);

            // --- Stored reports: the options, the dashboards' read-through and the service's collaborators (spec 0033 §2, §5.7, §14) ---
            ReportsComposition.Register(services, _record, options);

            // --- Server-built links (spec 0011 §5.10), over the stack registry's routes ---
            services.AddSingleton<ITellmaLinks, TellmaLinks>();

            // --- The realised gate, first among the hosted services ---
            // Hosted services start in registration order, and a host may have registered some
            // before composing Tellma; inserting at the head keeps the gate ahead of all of them.
            if (context.Problems.Count == 0)
            {
                services.Insert(0, ServiceDescriptor.Singleton<IHostedService, TellmaStartupGate>());
            }

            return context.Problems;
        }

        /// <summary>Registers the deployment identity, or reports the slug or environment name that cannot make one.</summary>
        /// <param name="problems">Where problems are reported.</param>
        private void RegisterDeploymentIdentity(List<CompositionProblem> problems)
        {
            if (!CompositionNames.IsValidSlug(slug))
            {
                problems.Add(new CompositionProblem(
                    CompositionNames.Source,
                    $"The distribution slug '{slug}' is not 1 to {CompositionNames.MaxSlugLength} lowercase letters, digits or hyphens starting with a letter.",
                    $"Use a slug such as 'acme': lowercase letters, digits and hyphens, starting with a letter, at most {CompositionNames.MaxSlugLength} characters."));
                return;
            }

            // A valid slug always makes a valid application name, so only the environment name can
            // fail here: one that does not compose a kebab-case deployment id.
            DeploymentIdentity identity;
            try
            {
                identity = new DeploymentIdentity(slug, environment.EnvironmentName);
            }
            catch (ArgumentException exception)
            {
                problems.Add(new CompositionProblem(
                    CompositionNames.Source,
                    $"The host environment name '{environment.EnvironmentName}' does not make a deployment identity: {exception.Message}",
                    "Name the environment with letters, digits and hyphens only, such as 'Production' or 'Staging'."));
                return;
            }

            // TryAdd, so an identity a host registered earlier survives and core.deployment-identity
            // reports it when it names another application.
            services.TryAddSingleton(identity);
        }

        /// <summary>
        ///     Refuses a distribution version the web client cannot state in its <c>Tellma-Client</c>
        ///     header, which would otherwise refuse every cookie-authenticated write of the client
        ///     after sign-in. The version checked is the one registered: a web host registers the
        ///     version of the composition it wraps before this pipeline runs.
        /// </summary>
        /// <param name="problems">The problems found so far.</param>
        private void CheckDistributionVersion(List<CompositionProblem> problems)
        {
            if (services.FirstOrDefault(static descriptor => descriptor.ServiceType == typeof(DeploymentVersions))?.ImplementationInstance is DeploymentVersions versions
                && !DeploymentVersions.IsClientVersion(versions.DistributionVersion))
            {
                problems.Add(new CompositionProblem(
                    CompositionNames.Source,
                    $"The distribution version '{versions.DistributionVersion}' is not 1 to {DeploymentVersions.MaxClientVersionLength} visible ASCII characters, so the web client cannot state it in its Tellma-Client header.",
                    $"Version the distribution's composition library with an informational version of at most {DeploymentVersions.MaxClientVersionLength} characters and no spaces, such as '1.4.0-rc.1+<sha>'."));
            }
        }

        /// <summary>Declares and collects the contribution of every feature, adding the nested features each contributes until none is new.</summary>
        private void CollectFeatures()
        {
            // Declarations of a wave run before its contributions; a nested feature found while
            // contributing starts the next wave.
            int collected = 0;
            while (collected < _features.Features.Count)
            {
                List<ComposedFeature> wave = [.. _features.Features.Skip(collected)];
                collected = _features.Features.Count;

                foreach (ComposedFeature feature in wave)
                {
                    Declare(feature);
                }

                foreach (ComposedFeature feature in wave)
                {
                    FeatureContribution contribution = new();
                    feature.Instance.Contribute(contribution);
                    feature.Contribution = contribution;

                    foreach (ITellmaFeature nested in contribution.Features)
                    {
                        ComposedFeature added = _features.Add(nested);
                        if (!feature.NestedFeatures.Contains(added))
                        {
                            feature.NestedFeatures.Add(added);
                        }
                    }
                }
            }
        }

        /// <summary>Reads a feature's name and records its requirements, from attributes and its declaration, and its options bindings.</summary>
        /// <param name="feature">The feature.</param>
        private static void Declare(ComposedFeature feature)
        {
            feature.Name = feature.Instance.Name;

            FeatureDeclaration declaration = new();
            feature.Instance.Declare(declaration);

            // Attribute edges first, in the order the compiler emits them, then the declared ones;
            // a name given both ways counts once.
            IEnumerable<string> attributed = feature.Instance.GetType()
                .GetCustomAttributes<RequiresAttribute>(inherit: true)
                .Select(static attribute => attribute.FeatureName);

            foreach (string name in attributed.Concat(declaration.RequiredFeatures))
            {
                if (!feature.RequiredNames.Contains(name, StringComparer.Ordinal))
                {
                    feature.RequiredNames.Add(name);
                }
            }

            feature.OptionsBindings.AddRange(declaration.OptionsBindings);
        }

        /// <summary>The graph gate over the collected features: names, requirements, cycles, options bindings and item kinds.</summary>
        /// <returns>Every problem found.</returns>
        private List<CompositionProblem> CheckGraph()
        {
            List<CompositionProblem> problems = [];
            IReadOnlyList<ComposedFeature> features = _features.Features;

            // --- Names: format and uniqueness ---
            Dictionary<string, ComposedFeature> byName = new(StringComparer.Ordinal);
            foreach (ComposedFeature feature in features)
            {
                if (!CompositionNames.IsValidFeatureName(feature.Name))
                {
                    problems.Add(new CompositionProblem(
                        CompositionNames.Source,
                        $"The feature {feature.Label} has the name '{feature.Name}', which is not lowercase dotted segments.",
                        "Name a feature with dot-separated segments of lowercase letters, digits and hyphens, each starting with a letter, such as 'gl' or 'acme.sales'."));
                    continue;
                }

                if (!byName.TryAdd(feature.Name!, feature))
                {
                    problems.Add(new CompositionProblem(
                        CompositionNames.Source,
                        $"The features {TypeDisplay.Qualified(byName[feature.Name!].Instance.GetType())} and {TypeDisplay.Qualified(feature.Instance.GetType())} both have the name '{feature.Name}'.",
                        "Give each feature a unique name, or add only one of these features."));
                }
            }

            // --- Requires edges ---
            List<List<int>> dependencies = [.. features.Select(static _ => new List<int>())];
            foreach (ComposedFeature feature in features)
            {
                foreach (string required in feature.RequiredNames)
                {
                    if (string.IsNullOrWhiteSpace(required))
                    {
                        problems.Add(new CompositionProblem(
                            CompositionNames.Source,
                            $"The feature {feature.Label} requires a feature without naming it.",
                            "Name the required feature by the name constant its package exposes."));
                    }
                    else if (byName.TryGetValue(required, out ComposedFeature? target))
                    {
                        dependencies[feature.Index].Add(target.Index);
                    }
                    else
                    {
                        problems.Add(new CompositionProblem(
                            CompositionNames.Source,
                            $"The feature {feature.Label} requires '{required}', but no added feature has that name.",
                            $"Add the feature named '{required}' with AddFeature<TFeature>() in the composition method, or remove the requirement from '{feature.Name}'."));
                    }
                }

                // A nesting feature builds on what it nests, so the nested feature is realised first.
                dependencies[feature.Index].AddRange(feature.NestedFeatures.Select(static nested => nested.Index));
            }

            // --- Cycles ---
            FeatureGraph graph = new([.. dependencies.Select(static list => (IReadOnlyList<int>)[.. list.Distinct()])]);
            foreach (IReadOnlyList<int> cycle in graph.FindCycles())
            {
                string path = string.Join(" -> ", cycle.Select(index => $"'{features[index].Name}'"));
                problems.Add(new CompositionProblem(
                    CompositionNames.Source,
                    $"The features {path} require each other in a cycle.",
                    "Remove one requirement or nesting from the cycle: a feature can build only on features that do not build on it."));
            }

            // --- Options bound twice ---
            foreach (IGrouping<Type, (FeatureOptionsBinding Binding, ComposedFeature Feature)> group in features
                .SelectMany(static feature => feature.OptionsBindings.Select(binding => (Binding: binding, Feature: feature)))
                .GroupBy(static pair => pair.Binding.OptionsType))
            {
                List<(FeatureOptionsBinding Binding, ComposedFeature Feature)> distinct =
                    [.. group.DistinctBy(static pair => pair.Binding.ConfigurationPath, StringComparer.OrdinalIgnoreCase)];

                if (distinct.Count > 1)
                {
                    string paths = string.Join(" and ", distinct.Select(static pair => $"'{pair.Binding.ConfigurationPath}' (by '{pair.Feature.Name}')"));
                    problems.Add(new CompositionProblem(
                        CompositionNames.Source,
                        $"The options type {TypeDisplay.Qualified(group.Key)} is bound from {paths}.",
                        $"Bind {TypeDisplay.Short(group.Key)} from one configuration path; a second use needs an options type of its own."));
                }
            }

            // --- Items no realizer handles ---
            foreach (ComposedFeature feature in features)
            {
                foreach (Type itemType in feature.Contribution!.Items.Select(static item => item.GetType()).Distinct())
                {
                    if (!realizers.Handles(itemType))
                    {
                        problems.Add(new CompositionProblem(
                            CompositionNames.Source,
                            $"The feature {feature.Label} contributes {TypeDisplay.Qualified(itemType)}, which no realizer of this Tellma.Core version handles.",
                            $"Update Tellma.Core to the version the package that defines {TypeDisplay.Short(itemType)} was built against."));
                    }
                }
            }

            // --- One service type from two unrelated features ---
            problems.AddRange(CheckServiceOverrides(features, dependencies));

            return problems;
        }

        /// <summary>
        ///     Reports every pair of features that register one non-collection service type when
        ///     neither depends on the other, directly or transitively: only a dependent feature may
        ///     replace the service of a feature it builds on, and between unrelated features the
        ///     winner would be decided by the order they happen to be added in.
        /// </summary>
        /// <param name="features">The collected features.</param>
        /// <param name="dependencies">Each feature's direct requirements and nested features, by index.</param>
        /// <returns>One problem per unrelated pair and service type.</returns>
        private static List<CompositionProblem> CheckServiceOverrides(IReadOnlyList<ComposedFeature> features, List<List<int>> dependencies)
        {
            List<CompositionProblem> problems = [];

            // Which features each feature depends on, transitively. Core is implicit under every
            // feature, so core's registrations are replaceable by anyone. A breadth-first walk per
            // feature tolerates the cycles the gate reports separately.
            List<HashSet<int>> reaches = [];
            foreach (ComposedFeature feature in features)
            {
                HashSet<int> reached = feature.Index == 0 ? [] : [0];
                Queue<int> queue = new(dependencies[feature.Index]);
                while (queue.Count > 0)
                {
                    int next = queue.Dequeue();
                    if (reached.Add(next))
                    {
                        foreach (int further in dependencies[next])
                        {
                            queue.Enqueue(further);
                        }
                    }
                }

                reaches.Add(reached);
            }

            IEnumerable<IGrouping<Type, ComposedFeature>> contributed = features
                .SelectMany(static feature => feature.Contribution!.Items
                    .OfType<ServiceContributionItem>()
                    .Where(static item => item.ServiceType is not null && !CollectionServices.Contains(item.ServiceType))
                    .Select(item => (item.ServiceType, Feature: feature)))
                .Distinct()
                .GroupBy(static pair => pair.ServiceType, static pair => pair.Feature);

            foreach (IGrouping<Type, ComposedFeature> group in contributed)
            {
                List<ComposedFeature> owners = [.. group];
                for (int i = 0; i < owners.Count; i++)
                {
                    for (int j = i + 1; j < owners.Count; j++)
                    {
                        ComposedFeature first = owners[i];
                        ComposedFeature second = owners[j];
                        if (reaches[first.Index].Contains(second.Index) || reaches[second.Index].Contains(first.Index))
                        {
                            continue;
                        }

                        string service = TypeDisplay.Short(group.Key);
                        problems.Add(new CompositionProblem(
                            CompositionNames.Source,
                            $"The features '{first.Name}' and '{second.Name}' both register the service {TypeDisplay.Qualified(group.Key)}, and neither requires the other.",
                            $"Make the feature that replaces {service} require the other ('{second.Name}' requires '{first.Name}', or the reverse), or register a different service type in one of them."));
                    }
                }
            }

            return problems;
        }

        /// <summary>Orders the features for realisation: <c>core</c> first, every feature after what it requires and nests.</summary>
        /// <returns>The features in realisation order.</returns>
        private List<ComposedFeature> OrderFeatures()
        {
            IReadOnlyList<ComposedFeature> features = _features.Features;
            return [.. new FeatureGraph(DirectDependencies()).Order().Select(index => features[index])];
        }

        /// <summary>
        ///     Whether a feature depends on another, transitively, through what it requires and nests;
        ///     every feature but <c>core</c> depends on <c>core</c>.
        /// </summary>
        /// <returns>The predicate, over the feature instances.</returns>
        private Func<ITellmaFeature, ITellmaFeature, bool> TransitiveDependencies()
        {
            IReadOnlyList<ComposedFeature> features = _features.Features;
            List<IReadOnlyList<int>> direct = DirectDependencies();

            // One walk per feature; the graph gate refused cycles, so each ends.
            Dictionary<ITellmaFeature, HashSet<ITellmaFeature>> reaches = new(ReferenceEqualityComparer.Instance);
            foreach (ComposedFeature feature in features)
            {
                HashSet<int> reached = [];
                Queue<int> queue = new(direct[feature.Index]);
                while (queue.Count > 0)
                {
                    int next = queue.Dequeue();
                    if (reached.Add(next))
                    {
                        foreach (int further in direct[next])
                        {
                            queue.Enqueue(further);
                        }
                    }
                }

                reaches[feature.Instance] = new HashSet<ITellmaFeature>(reached.Select(index => features[index].Instance), ReferenceEqualityComparer.Instance);
            }

            return (dependent, dependency) => reaches.TryGetValue(dependent, out HashSet<ITellmaFeature>? reached) && reached.Contains(dependency);
        }

        /// <summary>Each feature's direct dependencies, by index: what it requires and nests, and <c>core</c>.</summary>
        /// <returns>The dependencies of each feature.</returns>
        private List<IReadOnlyList<int>> DirectDependencies()
        {
            // The graph gate passed, so every name resolves, names are unique and there is no cycle.
            IReadOnlyList<ComposedFeature> features = _features.Features;
            var byName = features.ToDictionary(static feature => feature.Name!, static feature => feature.Index, StringComparer.Ordinal);

            List<IReadOnlyList<int>> dependencies = [];
            foreach (ComposedFeature feature in features)
            {
                // Core is implicit: every other feature follows it without declaring it.
                IEnumerable<int> edges = feature.RequiredNames.Select(name => byName[name])
                    .Concat(feature.NestedFeatures.Select(static nested => nested.Index))
                    .Concat(feature.Index == 0 ? [] : [0]);

                dependencies.Add([.. edges.Distinct()]);
            }

            return dependencies;
        }

        /// <summary>Registers the platform's built-in startup checks.</summary>
        private void RegisterBuiltInChecks()
        {
            // Registered as instances over this composition's record, in the order they report, and
            // kept on the record so the gate can still run them when a contributed check fails.
            _record.BuiltInChecks.AddRange(
            [
                new OptionsCheck(_record),
                new DeploymentIdentityCheck(_record),
                new GlobalizationCheck(icuProbe),
                new KeyVaultLiteralsCheck(_record),
                new SqlProfilesCheck(_record),
                new PublicOriginCheck(_record),
                new TaxonomyCheck(_record),
                new EntityShapesCheck(_record),
                new ModelContributorsCheck(_record),
                new EntityMetadataCheck(),
                new VersionTagsCheck(),
                new LanguagesCheck(_record, LanguagesCheck.HasCoreSatellite),
                new StringPackCheck(),
                new CalendarsCheck(),
                new ValueSetsCheck(_record),
                new SettingKeysCheck(_record),
                new NotificationTypesCheck(_record),
                new NotificationChannelsCheck(_record),
                new ClientEventsCheck(_record),
                new WidgetTypesCheck(_record),
                new SecurablesCheck(_record),
                new CacheableEntitiesCheck(),
                new TimeZonesCheck(TimeZonesCheck.HostResolves),
                new ProvisioningStepsCheck(_record),
                new JobHandlersCheck(_record),
                new JobSchedulesCheck(_record),
            ]);

            foreach (IStartupCheck check in _record.BuiltInChecks)
            {
                services.AddSingleton(check);
            }
        }
    }
}
