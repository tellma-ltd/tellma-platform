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
using Tellma.Core.Composition.Checks;
using Tellma.Core.Composition.Realizers;

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
            _record.Settle([.. ordered.Select(static feature => feature.Instance)], _builder.Leaves);

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

            // --- Declared options ---
            OptionsRegistrar options = new(services, configuration, _record);
            foreach (ComposedFeature feature in ordered)
            {
                foreach (FeatureOptionsBinding binding in feature.OptionsBindings)
                {
                    options.Bind(binding, feature.Name!);
                }
            }

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

            return problems;
        }

        /// <summary>Orders the features for realisation: <c>core</c> first, every feature after what it requires and nests.</summary>
        /// <returns>The features in realisation order.</returns>
        private List<ComposedFeature> OrderFeatures()
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

            return [.. new FeatureGraph(dependencies).Order().Select(index => features[index])];
        }

        /// <summary>Registers the platform's built-in startup checks.</summary>
        private void RegisterBuiltInChecks()
        {
            // Registered as instances over this composition's record, in the order they report.
            services.AddSingleton<IStartupCheck>(new OptionsCheck(_record));
            services.AddSingleton<IStartupCheck>(new DeploymentIdentityCheck(_record));
            services.AddSingleton<IStartupCheck>(new GlobalizationCheck(icuProbe));
            services.AddSingleton<IStartupCheck>(new KeyVaultLiteralsCheck(_record));
            services.AddSingleton<IStartupCheck>(new PublicOriginCheck(_record));
            services.AddSingleton<IStartupCheck>(new TaxonomyCheck(_record));
            services.AddSingleton<IStartupCheck>(new EntityShapesCheck(_record));
        }
    }
}
