// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Testing.Crud
{
    /// <summary>One measurement a pipeline instrument recorded: the instrument, the value and the tags it carried.</summary>
    /// <param name="Instrument">The instrument's name, such as <c>tellma.crud.operations</c>.</param>
    /// <param name="Value">The value: a counter's increment or a histogram's sample.</param>
    /// <param name="Tags">The tags, by name.</param>
    public sealed record CrudMeasurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags)
    {
        /// <summary>A tag's value as text.</summary>
        /// <param name="name">The tag's name, such as <c>crud.outcome</c>.</param>
        /// <returns>The value; null when the measurement carries no such tag.</returns>
        public string? Tag(string name)
        {
            return Tags.TryGetValue(name, out object? value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            string tags = string.Join(", ", Tags.OrderBy(static tag => tag.Key, StringComparer.Ordinal).Select(static tag => FormattableString.Invariant($"{tag.Key}={tag.Value}")));
            return FormattableString.Invariant($"{Instrument} {Value} [{tags}]");
        }
    }
}
