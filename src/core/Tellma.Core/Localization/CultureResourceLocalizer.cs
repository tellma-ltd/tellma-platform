// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The framework's resource localizer, opened up to look a resource up in a culture the caller
    ///     names rather than in the current UI culture.
    /// </summary>
    /// <remarks>
    ///     The lookup is the framework's own: the culture's parent chain to the neutral resources,
    ///     walked by the <see cref="ResourceManager" />, with a missing satellite remembered so it is
    ///     probed once.
    /// </remarks>
    /// <param name="resourceManager">The resources.</param>
    /// <param name="resourceAssembly">The assembly that holds them.</param>
    /// <param name="baseName">Their base name.</param>
    /// <param name="resourceNamesCache">The cache of each culture's resource names.</param>
    /// <param name="logger">The framework localizer's logger.</param>
    internal sealed class CultureResourceLocalizer(
        ResourceManager resourceManager,
        Assembly resourceAssembly,
        string baseName,
        IResourceNamesCache resourceNamesCache,
        ILogger logger) : ResourceManagerStringLocalizer(resourceManager, resourceAssembly, baseName, resourceNamesCache, logger)
    {
        /// <summary>The base name of the resources, as the framework resolved it.</summary>
        public string BaseName { get; } = baseName;

        /// <summary>A resource's value in a culture, or in the nearest parent that has it.</summary>
        /// <param name="name">The key.</param>
        /// <param name="culture">The culture the lookup starts from.</param>
        /// <returns>The raw value, or null when no culture of the chain, the neutral one included, has the key.</returns>
        public string? Find(string name, CultureInfo culture)
        {
            return GetStringSafely(name, culture);
        }

        /// <summary>Every resource of a culture, optionally with those its parents add.</summary>
        /// <param name="includeParentCultures">Whether the parents' resources are included.</param>
        /// <param name="culture">The culture.</param>
        /// <returns>The raw values.</returns>
        public IEnumerable<LocalizedString> FindAll(bool includeParentCultures, CultureInfo culture)
        {
            return GetAllStrings(includeParentCultures, culture);
        }
    }

    /// <summary>
    ///     The framework's resource localizer factory, making <see cref="CultureResourceLocalizer" />s.
    ///     It keeps the framework's naming of resources (a type's full name, or a base name within an
    ///     assembly) and its caching of one localizer per resource set.
    /// </summary>
    /// <param name="localizationOptions">The options; the platform passes none, so no resources path applies.</param>
    /// <param name="loggerFactory">Creates the framework localizers' loggers.</param>
    internal sealed class CultureResourceLocalizerFactory(
        Microsoft.Extensions.Options.IOptions<LocalizationOptions> localizationOptions,
        ILoggerFactory loggerFactory) : ResourceManagerStringLocalizerFactory(localizationOptions, loggerFactory)
    {
        /// <summary>One cache of resource names for every localizer this factory makes, as the framework keeps one.</summary>
        private readonly ResourceNamesCache _resourceNamesCache = new();

        /// <summary>The framework localizers' logger.</summary>
        private readonly ILogger _logger = loggerFactory.CreateLogger<ResourceManagerStringLocalizer>();

        /// <inheritdoc />
        protected override ResourceManagerStringLocalizer CreateResourceManagerStringLocalizer(Assembly assembly, string baseName)
        {
            return new CultureResourceLocalizer(new ResourceManager(baseName, assembly), assembly, baseName, _resourceNamesCache, _logger);
        }
    }
}
