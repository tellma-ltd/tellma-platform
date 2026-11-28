// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Tellma.Core.Imaging.Tests.Infrastructure
{
    /// <summary>
    ///     A host environment of a given name, for the suites that compose the processor as a host
    ///     does: the environment decides whether a host on Windows or macOS renders PDF thumbnails.
    /// </summary>
    /// <param name="name">The environment's name.</param>
    internal sealed class TestHostEnvironment(string name) : IHostEnvironment
    {
        /// <summary>A Development host, which renders PDF thumbnails on every operating system the suites run on.</summary>
        public static TestHostEnvironment Development => new(Environments.Development);

        /// <summary>A Production host, which renders PDF thumbnails on Linux only.</summary>
        public static TestHostEnvironment Production => new(Environments.Production);

        /// <inheritdoc />
        public string EnvironmentName { get; set; } = name;

        /// <inheritdoc />
        public string ApplicationName { get; set; } = "Tellma.Core.Imaging.Tests";

        /// <inheritdoc />
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        /// <inheritdoc />
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
