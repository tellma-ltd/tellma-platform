// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Module.Gl
{
    /// <summary>
    ///     The sample centers the GL module's provisioning step writes into a development tenant. GL
    ///     registers a default tree; a distribution built for one customer or sector replaces it from a
    ///     feature that requires <see cref="GlModule.FeatureName" /> and registers its own
    ///     implementation as a singleton.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every <see cref="SampleCenter.ParentCode" /> names another center of the list. The step
    ///         refuses a list that breaks this before it writes anything, and the save's own validation
    ///         reports the rest: duplicate codes, parent types and cycles.
    ///     </para>
    ///     <para>An empty list makes the step write nothing.</para>
    /// </remarks>
    public interface IGlSampleCenters
    {
        /// <summary>
        ///     The version of the list, from 1: the provisioning step's version. Bump it whenever the
        ///     list changes, so every development tenant writes the new list.
        /// </summary>
        int Version { get; }

        /// <summary>The centers, parents before or after their children alike.</summary>
        IReadOnlyList<SampleCenter> Centers { get; }
    }
}
