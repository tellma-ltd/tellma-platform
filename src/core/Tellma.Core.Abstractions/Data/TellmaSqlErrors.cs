// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The error numbers platform and distribution SQL may <c>THROW</c>, at severity 16 and state
    ///     1, each with a stable code as its message. No number outside the platform band
    ///     (<see cref="PlatformMin" />–<see cref="PlatformMax" />) and the distribution band
    ///     (<see cref="DistroMin" />–<see cref="DistroMax" />) may be thrown.
    /// </summary>
    public static class TellmaSqlErrors
    {
        /// <summary>The lowest number of the platform band.</summary>
        public const int PlatformMin = 50400;

        /// <summary>The highest number of the platform band.</summary>
        public const int PlatformMax = 50599;

        /// <summary>The caller is no longer an active user (<c>CallerInvalid</c>).</summary>
        public const int CallerInvalid = 50401;

        /// <summary>A row is outside the caller's grant (<c>RowSecurity</c>).</summary>
        public const int RowSecurity = 50403;

        /// <summary>A row does not exist or is hidden from the caller (<c>Entity.NotFound</c>).</summary>
        public const int NotFound = 50404;

        /// <summary>A row's stamp differs from the one supplied: a concurrency conflict.</summary>
        public const int Concurrency = 50409;

        /// <summary>A cached input the batch depends on changed (<c>StaleVersionTag</c>).</summary>
        public const int StaleVersionTag = 50412;

        /// <summary>A query write selected more rows than its cap.</summary>
        public const int LimitExceeded = 50413;

        /// <summary>An invariant failed; the message is the validation code.</summary>
        public const int Invariant = 50422;

        /// <summary>A query write selected a count other than the one previewed.</summary>
        public const int CountMismatch = 50428;

        /// <summary>The database is behind the running model (<c>SchemaBehind</c>).</summary>
        public const int SchemaBehind = 50501;

        /// <summary>A reported transient failure, such as an application-lock timeout.</summary>
        public const int Transient = 50503;

        /// <summary>The lowest number of the distribution and pack band.</summary>
        public const int DistroMin = 50600;

        /// <summary>The highest number of the distribution and pack band.</summary>
        public const int DistroMax = 50699;

        /// <summary>The message of <see cref="SchemaBehind" />.</summary>
        public const string SchemaBehindCode = "SchemaBehind";

        /// <summary>Whether a number is in the platform band.</summary>
        /// <param name="number">The error number.</param>
        /// <returns>True for 50400 to 50599.</returns>
        public static bool IsPlatform(int number)
        {
            return number is >= PlatformMin and <= PlatformMax;
        }

        /// <summary>Whether a number is in the distribution and pack band.</summary>
        /// <param name="number">The error number.</param>
        /// <returns>True for 50600 to 50699.</returns>
        public static bool IsDistribution(int number)
        {
            return number is >= DistroMin and <= DistroMax;
        }
    }
}
