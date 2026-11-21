// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The process's <see cref="IStringLocalizerFactory" />: the framework's resource lookup, with
    ///     every value rendered as an ICU message. <c>AddTellma</c> installs it in place of the
    ///     framework's factory, so <c>IStringLocalizer&lt;T&gt;</c> and
    ///     <see cref="Create(string, string)" /> alike render plurals, selects, ordinals, nested
    ///     messages, and dates and times in the reader's calendar and display zone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Resources follow the folder-and-file convention with no resources path: a marker type
    ///         <c>Strings</c> in an assembly's <c>Resources</c> namespace names
    ///         <c>Resources/Strings.resx</c> and its satellites. A host's own resources path is not
    ///         applied, because it would move every package's resources; an assembly that keeps its
    ///         resources elsewhere says so with <see cref="ResourceLocationAttribute" />.
    ///     </para>
    ///     <para>
    ///         A localizer looks a resource up in the culture of the flow's
    ///         <see cref="LocalizationScope" /> when one is entered, else in the current UI culture,
    ///         through that culture's parent chain to the neutral English resources (<c>ar-SA</c>,
    ///         <c>ar</c>, neutral). A key no culture holds renders as the key itself and is counted on
    ///         <c>tellma.localization.missing</c> once per key and culture.
    ///     </para>
    ///     <para>
    ///         Arguments are named: the indexer takes at most one object, a dictionary of named
    ///         arguments or an object whose public properties name them (an anonymous type). A second
    ///         object throws <see cref="ArgumentException" />.
    ///     </para>
    /// </remarks>
    public sealed class IcuStringLocalizerFactory : IStringLocalizerFactory
    {
        /// <summary>The framework factory whose lookup this decorates.</summary>
        private readonly CultureResourceLocalizerFactory _resources;

        /// <summary>The ICU renderer every localizer of the process shares.</summary>
        private readonly IcuMessageRenderer _renderer;

        /// <summary>The tracker of missing keys every localizer of the process shares.</summary>
        private readonly MissingResources _missing;

        /// <summary>The localizers made so far, one per framework localizer.</summary>
        private readonly ConcurrentDictionary<CultureResourceLocalizer, IcuStringLocalizer> _localizers = new();

        /// <summary>Creates the factory.</summary>
        /// <param name="languages">The language catalogue, whose offered languages tag the missing-key counter.</param>
        /// <param name="loggerFactory">Creates the loggers of the lookup and of the events.</param>
        /// <param name="meterFactory">Creates the meter of the missing-key counter.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public IcuStringLocalizerFactory(ILanguageCatalog languages, ILoggerFactory loggerFactory, IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(languages);
            ArgumentNullException.ThrowIfNull(loggerFactory);
            ArgumentNullException.ThrowIfNull(meterFactory);

            // The framework's options are deliberately not read: a resources path would move the
            // resources of every package that follows the convention.
            _resources = new CultureResourceLocalizerFactory(Options.Create(new LocalizationOptions()), loggerFactory);

            ILogger logger = loggerFactory.CreateLogger<IcuStringLocalizerFactory>();
            _renderer = new IcuMessageRenderer(logger);
            _missing = new MissingResources(meterFactory, languages, logger);
        }

        /// <inheritdoc />
        public IStringLocalizer Create(Type resourceSource)
        {
            ArgumentNullException.ThrowIfNull(resourceSource);
            return Wrap(_resources.Create(resourceSource));
        }

        /// <inheritdoc />
        public IStringLocalizer Create(string baseName, string location)
        {
            ArgumentNullException.ThrowIfNull(baseName);
            ArgumentNullException.ThrowIfNull(location);
            return Wrap(_resources.Create(baseName, location));
        }

        /// <summary>The ICU localizer over a framework localizer, made once.</summary>
        /// <param name="inner">The framework localizer, which the factory this decorates caches per resource set.</param>
        /// <returns>The localizer.</returns>
        private IcuStringLocalizer Wrap(IStringLocalizer inner)
        {
            // The decorated factory makes only this kind of localizer.
            var resources = (CultureResourceLocalizer)inner;
            return _localizers.GetOrAdd(resources, r => new IcuStringLocalizer(r, _renderer, _missing));
        }
    }
}
