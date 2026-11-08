// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>One generated source file for the distribution's client project, as a contributor wrote it.</summary>
    public sealed class ClientGeneratedFile
    {
        /// <summary>Creates a file; only <see cref="ClientAssetContext" /> does, after validating it.</summary>
        /// <param name="relativePath">The path relative to the generated folder.</param>
        /// <param name="content">The file's full text.</param>
        internal ClientGeneratedFile(string relativePath, string content)
        {
            RelativePath = relativePath;
            Content = content;
        }

        /// <summary>The path relative to the client project's generated folder, with forward slashes.</summary>
        public string RelativePath { get; }

        /// <summary>The file's full text.</summary>
        public string Content { get; }
    }
}
