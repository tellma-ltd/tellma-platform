// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The query schemas of the composed model: one per multilingual shape, built lazily and kept
    ///     for the process. A singleton.
    /// </summary>
    public interface IQueryexSchemaProvider
    {
        /// <summary>
        ///     A fingerprint that changes with the model; part of the query engine's cache identity.
        /// </summary>
        string Fingerprint { get; }

        /// <summary>The schema for a multilingual shape.</summary>
        /// <param name="shape">The tenant's multilingual shape.</param>
        /// <returns>The schema.</returns>
        QueryexSchema GetSchema(MultilingualShape shape);
    }
}
