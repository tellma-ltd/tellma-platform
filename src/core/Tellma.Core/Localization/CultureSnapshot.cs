// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Globalization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The pinned values of one culture: every <see cref="CultureField" /> in its text form.
    ///     Read from a host culture when the data is generated, and applied to a culture when the
    ///     platform builds the culture a reader's text is formatted in.
    /// </summary>
    internal sealed class CultureSnapshot
    {
        private readonly FrozenDictionary<string, string> _values;

        /// <summary>Creates a snapshot.</summary>
        /// <param name="values">Every field's text form, by field name.</param>
        /// <exception cref="ArgumentException">A field is missing, or a name is not a field's.</exception>
        public CultureSnapshot(IReadOnlyDictionary<string, string> values)
        {
            // --- Exactly the fields, so a snapshot always sets a culture completely ---
            foreach (CultureField field in CultureField.All)
            {
                if (!values.ContainsKey(field.Name))
                {
                    throw new ArgumentException($"The culture snapshot lacks the field '{field.Name}'.", nameof(values));
                }
            }

            if (values.Count != CultureField.All.Length)
            {
                string unknown = values.Keys.First(static name => !CultureField.All.Any(field => field.Name == name));
                throw new ArgumentException($"The culture snapshot holds '{unknown}', which is not a culture field.", nameof(values));
            }

            _values = values.ToFrozenDictionary(StringComparer.Ordinal);
        }

        /// <summary>The text form of a field.</summary>
        /// <param name="field">The field's name.</param>
        /// <returns>The value's text form.</returns>
        /// <exception cref="KeyNotFoundException">No field has the name.</exception>
        public string this[string field] => _values[field];

        /// <summary>Reads every field of a culture, cleaned.</summary>
        /// <param name="culture">The culture, on the Gregorian calendar.</param>
        /// <returns>The snapshot.</returns>
        public static CultureSnapshot Read(CultureInfo culture)
        {
            return new CultureSnapshot(CultureField.All.ToDictionary(static field => field.Name, field => field.Read(culture), StringComparer.Ordinal));
        }

        /// <summary>Sets every field on a culture.</summary>
        /// <param name="culture">The culture, on the Gregorian calendar and not read-only.</param>
        /// <exception cref="FormatException">A value's text is not a value of its field.</exception>
        /// <exception cref="ArgumentException">The framework refuses a value.</exception>
        public void Apply(CultureInfo culture)
        {
            foreach (CultureField field in CultureField.All)
            {
                field.Apply(culture, _values[field.Name]);
            }
        }

        /// <summary>A snapshot with some fields replaced.</summary>
        /// <param name="replacements">The replaced fields' text forms, by field name.</param>
        /// <returns>The new snapshot.</returns>
        /// <exception cref="ArgumentException">A name is not a field's.</exception>
        public CultureSnapshot With(IReadOnlyDictionary<string, string> replacements)
        {
            Dictionary<string, string> values = new(_values, StringComparer.Ordinal);
            foreach ((string name, string value) in replacements)
            {
                values[name] = value;
            }

            return new CultureSnapshot(values);
        }
    }
}
