// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Mcp
{
    /// <summary>
    ///     The tenant MCP server's feature, named <c>mcp</c>: it binds <see cref="McpOptions" /> from
    ///     <c>Tellma:Mcp</c>, validated by the startup gate's options check. It contributes no entity,
    ///     securable or table, so the migrator, which never composes it, sees the same model.
    /// </summary>
    /// <remarks>Added by <see cref="McpTellmaBuilderExtensions.AddMcp" />, which registers the server itself.</remarks>
    public sealed class McpFeature : ITellmaFeature
    {
        /// <summary>The feature's name.</summary>
        public const string FeatureName = "mcp";

        /// <inheritdoc />
        public string Name => FeatureName;

        /// <inheritdoc />
        public void Declare(FeatureDeclaration declaration)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            _ = declaration.Options<McpOptions>(McpOptions.SectionPath);
        }

        /// <inheritdoc />
        public void Contribute(FeatureContribution contribution)
        {
            // The server's services are the web host's alone and are registered by AddMcp, beside
            // the SDK's, rather than through the composition the migrator shares.
            ArgumentNullException.ThrowIfNull(contribution);
        }
    }
}
