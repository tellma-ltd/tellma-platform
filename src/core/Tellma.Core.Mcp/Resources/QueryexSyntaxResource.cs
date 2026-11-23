// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;

namespace Tellma.Core.Mcp.Resources
{
    /// <summary>
    ///     The resource <c>tellma://queryex/syntax</c>: the Queryex reference in Markdown, its
    ///     grammar, operators, functions and worked filters, which the query tools' descriptions and
    ///     <c>tellma_describe</c> point at. Static text, the same for every tenant and caller.
    /// </summary>
    internal sealed class QueryexSyntaxResource : McpServerResource
    {
        /// <summary>The resource's URI.</summary>
        public const string ResourceUri = "tellma://queryex/syntax";

        /// <summary>The resource's media type.</summary>
        public const string MimeType = "text/markdown";

        /// <summary>The embedded file's logical name.</summary>
        private const string ManifestName = "Tellma.Core.Mcp.queryex-syntax.md";

        /// <summary>The resource's name.</summary>
        private const string ResourceName = "queryex-syntax";

        /// <summary>The resource's title.</summary>
        private const string ResourceTitle = "Queryex syntax";

        /// <summary>The resource's description.</summary>
        private const string ResourceDescription =
            "The expression language of select, filter, having and orderBy: paths, literals, parameters, operators, functions and worked filters.";

        /// <summary>The reference's text, read once from the assembly.</summary>
        private static readonly Lazy<string> Content = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <inheritdoc />
        public override ResourceTemplate ProtocolResourceTemplate { get; } = new()
        {
            UriTemplate = ResourceUri,
            Name = ResourceName,
            Title = ResourceTitle,
            Description = ResourceDescription,
            MimeType = MimeType,
        };

        /// <inheritdoc />
        public override Resource ProtocolResource { get; } = new()
        {
            Uri = ResourceUri,
            Name = ResourceName,
            Title = ResourceTitle,
            Description = ResourceDescription,
            MimeType = MimeType,
        };

        /// <inheritdoc />
        public override IReadOnlyList<object> Metadata { get; } = [];

        /// <summary>The reference's Markdown text.</summary>
        internal static string Text => Content.Value;

        /// <inheritdoc />
        public override bool IsMatch(string uri)
        {
            ArgumentNullException.ThrowIfNull(uri);
            return string.Equals(uri, ResourceUri, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return ValueTask.FromResult(new ReadResourceResult
            {
                Contents = [new TextResourceContents { Uri = ResourceUri, MimeType = MimeType, Text = Text }],
            });
        }

        /// <summary>Reads the embedded reference.</summary>
        /// <returns>The text.</returns>
        private static string Load()
        {
            using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ManifestName)
                ?? throw new InvalidOperationException($"The assembly carries no embedded resource {ManifestName}.");
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
    }
}
