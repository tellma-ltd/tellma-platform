// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Localization;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Resources;
using System.Text.Json;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §9: a progress or error key is rendered for the log in English, in the invariant
    // culture, through the ICU localizers: from the handler's assembly's Strings first, then Core's,
    // which holds the platform's Jobs.* keys. A key neither holds is logged as it is. The keys each
    // assembly holds are read once, so a key a handler never declared is no missing-resource count.
    /// <summary>Renders a job's progress and error keys in English for the log.</summary>
    internal sealed class JobLogText
    {
        /// <summary>The context the log renders in: English, the Gregorian calendar, UTC.</summary>
        private static readonly LocalizationContext English = new(
            CultureInfo.InvariantCulture, new GregorianCalendarSystem(), TimeZoneInfo.Utc, 1, [], "en", "en");

        /// <summary>Creates the localizer of each assembly's resources.</summary>
        private readonly IStringLocalizerFactory _localizers;

        /// <summary>Each assembly's localizer and the keys its neutral resources hold, read once.</summary>
        private readonly ConcurrentDictionary<string, (IStringLocalizer Localizer, FrozenSet<string> Keys)> _assemblies = new(StringComparer.Ordinal);

        /// <summary>Creates the renderer.</summary>
        /// <param name="localizers">Creates the localizer of each assembly's resources.</param>
        public JobLogText(IStringLocalizerFactory localizers)
        {
            ArgumentNullException.ThrowIfNull(localizers);
            _localizers = localizers;
        }

        /// <summary>The resource a key renders from: the key with every <c>.</c> replaced by <c>_</c>.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The resource name.</returns>
        public static string ResourceName(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return key.Replace('.', '_');
        }

        /// <summary>Renders a key with arguments stored as JSON.</summary>
        /// <param name="handlerType">The handler whose assembly's resources are searched first; null for Core's alone.</param>
        /// <param name="key">The key.</param>
        /// <param name="argumentsJson">The arguments as JSON; null when none.</param>
        /// <returns>The text; the key itself when no resource holds it.</returns>
        public string Render(Type? handlerType, string key, string? argumentsJson)
        {
            ArgumentNullException.ThrowIfNull(key);
            Dictionary<string, object?>? arguments = argumentsJson is null ? null : IcuJsonArguments.Read(argumentsJson, English.ContentLanguageIndex, out JsonException? _);
            return Render(handlerType, key, arguments);
        }

        /// <summary>Renders a key with its arguments.</summary>
        /// <param name="handlerType">The handler whose assembly's resources are searched first; null for Core's alone.</param>
        /// <param name="key">The key.</param>
        /// <param name="arguments">The arguments; null when none.</param>
        /// <returns>The text; the key itself when no resource holds it, or when the resource cannot be formatted with the arguments.</returns>
        public string Render(Type? handlerType, string key, IReadOnlyDictionary<string, object?>? arguments)
        {
            ArgumentNullException.ThrowIfNull(key);
            string name = ResourceName(key);

            // --- The first assembly that holds the key renders it ---
            using IDisposable english = LocalizationScope.Enter(English);
            foreach (string assembly in Assemblies(handlerType))
            {
                (IStringLocalizer localizer, FrozenSet<string> keys) = Resources(assembly);
                if (!keys.Contains(name))
                {
                    continue;
                }

                // A log line never fails over its text: arguments the message cannot format leave the key.
                try
                {
                    LocalizedString text = arguments is null || arguments.Count == 0 ? localizer[name] : localizer[name, arguments];
                    return text.ResourceNotFound ? key : text.Value;
                }
                catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
                {
                    return key;
                }
            }

            return key;
        }

        /// <summary>The assemblies searched, in order: the handler's, then Core's.</summary>
        /// <param name="handlerType">The handler; null for Core's alone.</param>
        /// <returns>The assembly names.</returns>
        private static IEnumerable<string> Assemblies(Type? handlerType)
        {
            string? own = handlerType?.Assembly.GetName().Name;
            if (own is not null && !string.Equals(own, CoreStrings.Assembly, StringComparison.Ordinal))
            {
                yield return own;
            }

            yield return CoreStrings.Assembly;
        }

        /// <summary>An assembly's localizer and the keys its neutral resources hold.</summary>
        /// <param name="assembly">The assembly's name.</param>
        /// <returns>The localizer and the keys.</returns>
        private (IStringLocalizer Localizer, FrozenSet<string> Keys) Resources(string assembly)
        {
            return _assemblies.GetOrAdd(assembly, name =>
            {
                // spec 0013 §10.1's convention: Resources/Strings.resx in the assembly's root namespace.
                IStringLocalizer localizer = _localizers.Create($"{name}.Resources.Strings", name);
                FrozenSet<string> keys;
                try
                {
                    keys = localizer.GetAllStrings(includeParentCultures: true).Select(static text => text.Name).ToFrozenSet(StringComparer.Ordinal);
                }
                catch (MissingManifestResourceException)
                {
                    // An assembly that ships no resources holds no keys.
                    keys = [];
                }

                return (localizer, keys);
            });
        }
    }
}
