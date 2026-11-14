// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     A stack's ceilings. The same numbers bind every caller, the web surface, agents, the import
    ///     and background work alike; a surface may only lower them. Exceeding one is a
    ///     <see cref="LimitExceededException" /> before any round trip, never a silent clamp.
    /// </summary>
    /// <param name="MaxTake">The rows one query page or one get by parent ids may return.</param>
    /// <param name="CountCap">Where a query's grand-total count stops; a count above it reads as the cap plus one.</param>
    /// <param name="MaxSkipWindow">The largest skip plus take of a query.</param>
    /// <param name="MaxSaveCount">The entities one save may carry.</param>
    /// <param name="MaxRowsPerSave">The rows one save may carry at every depth.</param>
    /// <param name="MaxIds">The ids one get by ids, delete by ids, action or get by parent ids may name.</param>
    /// <param name="MaxDeleteByQueryRows">The rows one delete by query may match.</param>
    /// <param name="MaxExpandDepth">The navigation depth of the details projection.</param>
    /// <param name="MaxValidationRounds">The validation rounds one save, delete or action may dispatch.</param>
    /// <param name="MaxSearchLength">The characters of a search term.</param>
    /// <param name="MaxExtras">The extras one details request may include.</param>
    /// <exception cref="ArgumentOutOfRangeException">A ceiling is zero or negative.</exception>
    public sealed record StackLimits(
        int MaxTake,
        int CountCap,
        int MaxSkipWindow,
        int MaxSaveCount,
        int MaxRowsPerSave,
        int MaxIds,
        int MaxDeleteByQueryRows,
        int MaxExpandDepth,
        int MaxValidationRounds,
        int MaxSearchLength,
        int MaxExtras)
    {
        /// <summary>The platform's defaults, which a stack's annotation overrides ceiling by ceiling.</summary>
        public static StackLimits Default { get; } = new(
            MaxTake: 10_000,
            CountCap: 10_000,
            MaxSkipWindow: 100_000,
            MaxSaveCount: 10_000,
            MaxRowsPerSave: 100_000,
            MaxIds: 10_000,
            MaxDeleteByQueryRows: 10_000,
            MaxExpandDepth: 3,
            MaxValidationRounds: 3,
            MaxSearchLength: 200,
            MaxExtras: 16);

        // Each ceiling is re-declared so a zero or negative value fails at construction and in a
        // with expression alike: a ceiling of zero would refuse every request and read as a stack
        // that serves nothing.

        /// <inheritdoc cref="StackLimits(int, int, int, int, int, int, int, int, int, int, int)" />
        public int MaxTake { get; init => field = Positive(value, nameof(MaxTake)); } = Positive(MaxTake, nameof(MaxTake));

        /// <inheritdoc cref="StackLimits(int, int, int, int, int, int, int, int, int, int, int)" />
        public int CountCap { get; init => field = Positive(value, nameof(CountCap)); } = Positive(CountCap, nameof(CountCap));

        /// <inheritdoc cref="StackLimits(int, int, int, int, int, int, int, int, int, int, int)" />
        public int MaxSkipWindow { get; init => field = Positive(value, nameof(MaxSkipWindow)); } = Positive(MaxSkipWindow, nameof(MaxSkipWindow));

        /// <inheritdoc cref="StackLimits(int, int, int, int, int, int, int, int, int, int, int)" />
        public int MaxSaveCount { get; init => field = Positive(value, nameof(MaxSaveCount)); } = Positive(MaxSaveCount, nameof(MaxSaveCount));

        /// <inheritdoc cref="StackLimits(int, int, int, int, int, int, int, int, int, int, int)" />
        public int MaxRowsPerSave { get; init => field = Positive(value, nameof(MaxRowsPerSave)); } = Positive(MaxRowsPerSave, nameof(MaxRowsPerSave));

        /// <inheritdoc cref="StackLimits(int, int, int, int, int, int, int, int, int, int, int)" />
        public int MaxIds { get; init => field = Positive(value, nameof(MaxIds)); } = Positive(MaxIds, nameof(MaxIds));

        /// <inheritdoc cref="StackLimits(int, int, int, int, int, int, int, int, int, int, int)" />
        public int MaxDeleteByQueryRows { get; init => field = Positive(value, nameof(MaxDeleteByQueryRows)); } = Positive(MaxDeleteByQueryRows, nameof(MaxDeleteByQueryRows));

        /// <inheritdoc cref="StackLimits(int, int, int, int, int, int, int, int, int, int, int)" />
        public int MaxExpandDepth { get; init => field = Positive(value, nameof(MaxExpandDepth)); } = Positive(MaxExpandDepth, nameof(MaxExpandDepth));

        /// <inheritdoc cref="StackLimits(int, int, int, int, int, int, int, int, int, int, int)" />
        public int MaxValidationRounds { get; init => field = Positive(value, nameof(MaxValidationRounds)); } = Positive(MaxValidationRounds, nameof(MaxValidationRounds));

        /// <inheritdoc cref="StackLimits(int, int, int, int, int, int, int, int, int, int, int)" />
        public int MaxSearchLength { get; init => field = Positive(value, nameof(MaxSearchLength)); } = Positive(MaxSearchLength, nameof(MaxSearchLength));

        /// <inheritdoc cref="StackLimits(int, int, int, int, int, int, int, int, int, int, int)" />
        public int MaxExtras { get; init => field = Positive(value, nameof(MaxExtras)); } = Positive(MaxExtras, nameof(MaxExtras));

        /// <summary>Refuses a ceiling that is not positive.</summary>
        /// <param name="value">The ceiling.</param>
        /// <param name="name">The ceiling's name.</param>
        /// <returns>The ceiling.</returns>
        private static int Positive(int value, string name)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, name);
            return value;
        }
    }
}
