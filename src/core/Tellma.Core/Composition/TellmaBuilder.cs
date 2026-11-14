// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Localization;

namespace Tellma.Core.Composition
{
    /// <summary>
    ///     What a distribution's composition method receives: it selects features, substitutes
    ///     entity leaves, declares the languages it offers and, for stores and other host-level
    ///     choices, registers services directly.
    /// </summary>
    /// <remarks>
    ///     Selection is explicit: there is no assembly scanning, so the composition method is the one
    ///     place that says what a distribution contains. The builder only records; the composition root
    ///     validates and realises everything after the composition method returns, and refuses further
    ///     selections from then on.
    /// </remarks>
    public sealed class TellmaBuilder
    {
        private readonly FeatureSet _features;
        private readonly Dictionary<Type, Type> _leaves = [];
        private readonly List<CompositionProblem> _problems = [];
        private readonly List<AddedLanguage> _addedLanguages = [];
        private IReadOnlyList<string>? _languageCodes;
        private bool _sealed;

        /// <summary>Creates the builder over one composition's state.</summary>
        /// <param name="services">The host's services.</param>
        /// <param name="configuration">The host's configuration.</param>
        /// <param name="environment">The host's environment.</param>
        /// <param name="features">The composition's features, <c>core</c> already added.</param>
        internal TellmaBuilder(
            IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment,
            FeatureSet features)
        {
            Services = services;
            Configuration = configuration;
            Environment = environment;
            _features = features;
        }

        /// <summary>
        ///     The host's services. Stores and other host-level choices (the blob store, the image
        ///     processor) register here; features never do.
        /// </summary>
        public IServiceCollection Services { get; }

        /// <summary>The host's configuration.</summary>
        public IConfiguration Configuration { get; }

        /// <summary>The host's environment.</summary>
        public IHostEnvironment Environment { get; }

        /// <summary>The leaf substitutions recorded by <see cref="UseEntity{TDefault, TLeaf}" />.</summary>
        internal IReadOnlyDictionary<Type, Type> Leaves => _leaves;

        /// <summary>The problems the builder's calls produced.</summary>
        internal IReadOnlyList<CompositionProblem> Problems => _problems;

        /// <summary>What <see cref="Languages(IReadOnlyList{string})" /> and <see cref="AddLanguage" /> recorded.</summary>
        internal LanguageDeclaration LanguageDeclaration => new(_languageCodes, _addedLanguages);

        /// <summary>Adds a feature of <typeparamref name="TFeature" />. Idempotent: a type already added is not added again.</summary>
        /// <typeparam name="TFeature">The feature type.</typeparam>
        /// <returns>This builder, for chaining.</returns>
        /// <exception cref="InvalidOperationException">The composition method has already returned.</exception>
        public TellmaBuilder AddFeature<TFeature>()
            where TFeature : ITellmaFeature, new()
        {
            ThrowIfSealed();
            _ = _features.Add<TFeature>();
            return this;
        }

        /// <summary>
        ///     Adds a feature instance. Adding the same instance again is a no-op; adding a second,
        ///     different instance of a feature type already added is a composition problem.
        /// </summary>
        /// <param name="feature">The feature.</param>
        /// <returns>This builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="feature" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The composition method has already returned.</exception>
        public TellmaBuilder AddFeature(ITellmaFeature feature)
        {
            ArgumentNullException.ThrowIfNull(feature);
            ThrowIfSealed();
            _ = _features.Add(feature);
            return this;
        }

        /// <summary>
        ///     Substitutes the distribution's leaf <typeparamref name="TLeaf" /> for the default entity
        ///     <typeparamref name="TDefault" /> a feature ships. The model, the services and the
        ///     securables follow the leaf. A default is substituted at most once: the same pair again is
        ///     a no-op, and a second leaf for the same default is a composition problem.
        /// </summary>
        /// <typeparam name="TDefault">The default entity a feature ships.</typeparam>
        /// <typeparam name="TLeaf">The distribution's leaf, derived from it.</typeparam>
        /// <returns>This builder, for chaining.</returns>
        /// <exception cref="InvalidOperationException">The composition method has already returned.</exception>
        public TellmaBuilder UseEntity<TDefault, TLeaf>()
            where TDefault : class
            where TLeaf : class, TDefault
        {
            ThrowIfSealed();

            if (!_leaves.TryGetValue(typeof(TDefault), out Type? existing))
            {
                _leaves.Add(typeof(TDefault), typeof(TLeaf));
            }
            else if (existing != typeof(TLeaf))
            {
                _problems.Add(new CompositionProblem(
                    CompositionNames.Source,
                    $"The entity {TypeDisplay.Qualified(typeof(TDefault))} was given two leaves, {TypeDisplay.Qualified(existing)} and {TypeDisplay.Qualified(typeof(TLeaf))}.",
                    $"Call UseEntity<{TypeDisplay.Short(typeof(TDefault))}, TLeaf>() once, with the one leaf that should replace it."));
            }

            return this;
        }

        /// <summary>
        ///     Declares the languages the distribution offers, once. The first is the distribution
        ///     default: the language of a reader whose request, preference and tenant name none that
        ///     is offered. A distribution that never calls this offers English.
        /// </summary>
        /// <remarks>
        ///     Each code is a language of the catalogue or one added with <see cref="AddLanguage" />,
        ///     optionally followed by the region its users get by default: <c>["en-GB", "ar-SA"]</c>
        ///     offers <c>en</c> and <c>ar</c>. Only the language is offered and stored. An unknown
        ///     language, a region the language does not have, or a language whose platform strings
        ///     do not ship fails startup with the remedy.
        /// </remarks>
        /// <param name="codes">The offered codes, the default first.</param>
        /// <returns>This builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="codes" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The composition method has already returned.</exception>
        public TellmaBuilder Languages(IReadOnlyList<string> codes)
        {
            ArgumentNullException.ThrowIfNull(codes);
            ThrowIfSealed();

            // The offered set is declared once: a second, different list would leave the default
            // language to whichever call happened to run last.
            if (_languageCodes is null)
            {
                _languageCodes = [.. codes];
            }
            else if (!_languageCodes.SequenceEqual(codes, StringComparer.Ordinal))
            {
                _problems.Add(new CompositionProblem(
                    CompositionNames.Source,
                    $"Languages was called twice, with [{string.Join(", ", _languageCodes)}] and with [{string.Join(", ", codes)}].",
                    "Call tellma.Languages(codes) once, with every language the distribution offers, the default first."));
            }

            return this;
        }

        /// <summary>
        ///     Adds a language the catalogue does not describe, with its formats: its regions, its
        ///     default region and its native digits. Adding a language does not offer it; name it
        ///     in <see cref="Languages(IReadOnlyList{string})" /> as well.
        /// </summary>
        /// <param name="info">The language: its code, its name in itself, its symbol and its direction.</param>
        /// <param name="formats">The language's formats, under the same code.</param>
        /// <returns>This builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="info" /> or <paramref name="formats" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The composition method has already returned.</exception>
        public TellmaBuilder AddLanguage(LanguageInfo info, LanguageFormats formats)
        {
            ArgumentNullException.ThrowIfNull(info);
            ArgumentNullException.ThrowIfNull(formats);
            ThrowIfSealed();

            // Recorded as given; the startup gate judges it against the catalogue.
            _addedLanguages.Add(new AddedLanguage(info, formats));
            return this;
        }

        /// <summary>Refuses further selections; called when the composition method returns.</summary>
        internal void Seal()
        {
            _sealed = true;
        }

        /// <summary>Throws once the composition method has returned.</summary>
        private void ThrowIfSealed()
        {
            // A selection after the composition method returned would be silently ignored, which is
            // worse than failing: the composition has already been validated and realised.
            if (_sealed)
            {
                throw new InvalidOperationException(
                    "Features, leaf substitutions and languages can only be declared while the composition method runs.");
            }
        }
    }
}
