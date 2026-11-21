// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Composition
{
    /// <summary>Source-generated log messages of the composition gates.</summary>
    internal static partial class TellmaCompositionLog
    {
        /// <summary>A startup check threw; its exception is reported as a problem, and logged here with its stack.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="check">The check's name.</param>
        /// <param name="exception">What it threw.</param>
        [LoggerMessage(Level = LogLevel.Error, Message = "The startup check {Check} threw.")]
        public static partial void CheckThrew(ILogger logger, string check, Exception exception);

        /// <summary>The registered startup checks could not be created; only the built-in ones run.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">What creating them threw.</param>
        [LoggerMessage(Level = LogLevel.Error, Message = "The startup checks could not be created; only the built-in checks run.")]
        public static partial void ChecksUnresolvable(ILogger logger, Exception exception);

        /// <summary>Every startup check passed.</summary>
        /// <param name="logger">The logger.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "The Tellma startup gate passed.")]
        public static partial void GatePassed(ILogger logger);
    }
}
