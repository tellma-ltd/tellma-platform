// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Composition;
using Tellma.Core.Localization;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The scoped server-side renderer: a notification's type resource, formatted as an ICU message
    ///     with the arguments' top-level members, in the reader's language, calendar and display zone,
    ///     followed by the type's detail line when it declares one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The resources are those of the package whose feature registered the type, so a type's
    ///         satellites ship beside it. A type no feature registers any longer renders through
    ///         Core's resources, which do not declare it, and so renders its key.
    ///     </para>
    ///     <para>
    ///         Each argument renders by its JSON kind: a string as text, an ISO 8601 date-time with an
    ///         offset as an instant in the reader's zone and calendar, an ISO 8601 date as a calendar
    ///         date, a number as a number, a boolean as <c>true</c> or <c>false</c> (so a
    ///         <c>select</c> on it matches those keys, as on the client), null as nothing, a localized
    ///         text as the reader's content slot, and anything else as its JSON text.
    ///     </para>
    /// </remarks>
    internal sealed class NotificationRenderer : INotificationRenderer
    {
        /// <summary>The prefix of a type's resource, which the type key follows with every <c>.</c> replaced by <c>_</c>.</summary>
        public const string ResourcePrefix = "Notification_";

        /// <summary>The suffix of a type's detail-line resource.</summary>
        public const string DetailSuffix = "_Detail";

        /// <summary>Creates the localizer of each feature's resources.</summary>
        private readonly IStringLocalizerFactory _localizers;

        /// <summary>The realised composition, which names the feature that registered each type.</summary>
        private readonly TellmaCompositionRecord _record;

        /// <summary>The keys every composed feature's neutral resources declare, which decide whether a detail line exists.</summary>
        private readonly INeutralStringKeys _keys;

        /// <summary>The renderer's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>The malformed arguments already reported in this scope, by type and text.</summary>
        private readonly HashSet<(string Type, string Arguments)> _reported = [];

        /// <summary>Creates the renderer for one scope.</summary>
        /// <param name="localizers">Creates the localizer of each feature's resources.</param>
        /// <param name="record">The realised composition.</param>
        /// <param name="keys">The keys the composed features' neutral resources declare.</param>
        /// <param name="loggers">Creates the renderer's logger.</param>
        public NotificationRenderer(IStringLocalizerFactory localizers, TellmaCompositionRecord record, INeutralStringKeys keys, ILoggerFactory loggers)
        {
            ArgumentNullException.ThrowIfNull(localizers);
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(keys);
            ArgumentNullException.ThrowIfNull(loggers);
            _localizers = localizers;
            _record = record;
            _keys = keys;
            _logger = loggers.CreateLogger(NotificationsLog.Category);
        }

        /// <summary>The resource of a type's text: <c>Notification_</c> and the key with every <c>.</c> replaced by <c>_</c>.</summary>
        /// <param name="type">The type key.</param>
        /// <returns>The resource key.</returns>
        public static string ResourceKey(string type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return ResourcePrefix + type.Replace('.', '_');
        }

        /// <inheritdoc />
        public string Render(string type, string? argumentsJson, string? targetResource, long? targetId, LocalizationContext localization)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(localization);

            // The target is the client's to route; the text never names it, so it is not read here.
            IStringLocalizer localizer = Localizer(type);
            IReadOnlyDictionary<string, object?>? arguments = Arguments(type, argumentsJson, localization.ContentLanguageIndex);
            string key = ResourceKey(type);
            string detailKey = key + DetailSuffix;

            // --- The title, then the detail line, both in the reader's context ---
            using (LocalizationScope.Enter(localization))
            {
                LocalizedString title = Format(localizer, key, arguments);
                if (title.ResourceNotFound)
                {
                    // A missing resource is counted by the localizer; the reader sees the type's key.
                    return type;
                }

                // The detail line is optional, so its absence is read from the declared keys rather
                // than reported as a missing resource.
                return _keys.Declares(detailKey)
                    ? string.Concat(title.Value, "\n", Format(localizer, detailKey, arguments).Value)
                    : title.Value;
            }
        }

        /// <summary>Formats one resource with the arguments, or as it is when there are none.</summary>
        /// <param name="localizer">The type's localizer.</param>
        /// <param name="key">The resource key.</param>
        /// <param name="arguments">The arguments; null for none.</param>
        /// <returns>The localized string.</returns>
        private static LocalizedString Format(IStringLocalizer localizer, string key, IReadOnlyDictionary<string, object?>? arguments)
        {
            return arguments is null ? localizer[key] : localizer[key, arguments];
        }

        /// <summary>The localizer of the resources of the feature that registered a type; Core's for a type none registers.</summary>
        /// <param name="type">The type key.</param>
        /// <returns>The localizer.</returns>
        private IStringLocalizer Localizer(string type)
        {
            // spec 0013 §10.1's convention: Resources/Strings.resx in the assembly's root namespace.
            string assembly = _record.NotificationTypes
                .FirstOrDefault(contributed => string.Equals(contributed.Descriptor.Key, type, StringComparison.Ordinal))?
                .Owner.GetType().Assembly.GetName().Name
                ?? CoreStrings.Assembly;
            return _localizers.Create($"{assembly}.Resources.Strings", assembly);
        }

        /// <summary>
        ///     The ICU arguments of a notification: its arguments' top-level members by JSON kind; null
        ///     when it has none, or when they are not a JSON object, which is reported once per type and
        ///     text in the scope.
        /// </summary>
        /// <param name="type">The type key.</param>
        /// <param name="argumentsJson">The stored arguments.</param>
        /// <param name="contentLanguageIndex">The reader's content slot.</param>
        /// <returns>The arguments, or null.</returns>
        private Dictionary<string, object?>? Arguments(string type, string? argumentsJson, int contentLanguageIndex)
        {
            if (argumentsJson is null)
            {
                return null;
            }

            // --- One JSON object, each top-level member by its kind; anything else renders without arguments ---
            Dictionary<string, object?>? arguments = IcuJsonArguments.Read(argumentsJson, contentLanguageIndex, out JsonException? failure);
            if (arguments is null)
            {
                ReportInvalid(type, argumentsJson, failure);
            }

            return arguments;
        }

        /// <summary>Reports malformed arguments once per type and text in the scope.</summary>
        /// <param name="type">The type key.</param>
        /// <param name="argumentsJson">The arguments' text.</param>
        /// <param name="exception">The parse failure; null for JSON that is not an object.</param>
        private void ReportInvalid(string type, string argumentsJson, Exception? exception)
        {
            if (_reported.Add((type, argumentsJson)))
            {
                NotificationsLog.NotificationArgumentsInvalid(_logger, exception, type);
            }
        }
    }
}
