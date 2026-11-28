// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Dashboards;
using Tellma.Core.Composition;

namespace Tellma.Core.Dashboards
{
    /// <summary>
    ///     The dashboard widget types of the composition, in realisation order: the keys the features
    ///     contributed, the platform's first. A singleton built once from the composition record;
    ///     immutable.
    /// </summary>
    /// <remarks>
    ///     The registry keeps a repeated key once, as its first feature registered it, so that the
    ///     startup gate, and not a failing constructor, is where such a mistake surfaces.
    /// </remarks>
    internal sealed class WidgetTypeRegistry : IWidgetTypeRegistry
    {
        /// <summary>The registered types by key.</summary>
        private readonly Dictionary<string, WidgetTypeDescriptor> _byKey = new(StringComparer.Ordinal);

        /// <summary>Builds the registry from the composition record.</summary>
        /// <param name="record">The realised composition, whose widget types passed the gate.</param>
        /// <exception cref="ArgumentNullException"><paramref name="record" /> is null.</exception>
        public WidgetTypeRegistry(TellmaCompositionRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            // --- One descriptor per key, in realisation order; only the report type has a subject ---
            List<WidgetTypeDescriptor> types = [];
            foreach (ContributedWidgetType contributed in record.WidgetTypes)
            {
                WidgetSubject subject = string.Equals(contributed.Key, WidgetTypeKeys.Report, StringComparison.Ordinal)
                    ? WidgetSubject.Report
                    : WidgetSubject.None;
                WidgetTypeDescriptor descriptor = new(contributed.Key, contributed.Owner.Name, contributed.SettingsType, subject);
                if (_byKey.TryAdd(descriptor.Key, descriptor))
                {
                    types.Add(descriptor);
                }
            }

            Types = types;
        }

        /// <inheritdoc />
        public IReadOnlyList<WidgetTypeDescriptor> Types { get; }

        /// <inheritdoc />
        public WidgetTypeDescriptor? Find(string key)
        {
            return key is not null && _byKey.TryGetValue(key, out WidgetTypeDescriptor? descriptor) ? descriptor : null;
        }
    }
}
