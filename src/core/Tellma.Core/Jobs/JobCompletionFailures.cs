// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §5.6 and spec 0015 §14.2: a failure of a statement the handler appended to its
    // completion batch is translated by §14.2's rows, at the root, since the handler's own statements
    // write unowned tables no payload maps to paths: an invariant, a distribution's guard, a tree's
    // cycle or depth, a unique index and a foreign key. A failure an enlisted group raised is the
    // frame's to translate first, against that group. The lease's own fence is never translated.
    /// <summary>Translates the failure of a completion batch's handler statements into a validation failure at the root.</summary>
    internal static class JobCompletionFailures
    {
        /// <summary>The reason a blob refused at persist time names.</summary>
        private const string PersistReason = "persist";

        /// <summary>Whether a failure is the completion's lease fence: the rows are no longer leased under the claim's token.</summary>
        /// <param name="failure">The failure.</param>
        /// <returns>True for the fence.</returns>
        public static bool IsLeaseLost(Exception failure)
        {
            return failure is BatchAssertionFailedException { Number: TellmaSqlErrors.Invariant, Code: JobErrorCodes.LeaseLost };
        }

        /// <summary>The validation failure a statement failure stands for; null when no constraint or invariant translation covers it.</summary>
        /// <param name="failure">The failure.</param>
        /// <returns>The validation failure, or null.</returns>
        public static ValidationException? Translate(DataAccessException failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            return failure switch
            {
                BatchAssertionFailedException assertion when IsLeaseLost(assertion) => null,
                BatchAssertionFailedException { Number: TellmaSqlErrors.Invariant } assertion => assertion.Code == ValidationCodes.BlobNotAttachable
                    ? AtRoot(assertion.Code, ("reason", PersistReason))
                    : AtRoot(assertion.Code),
                BatchAssertionFailedException { Number: >= TellmaSqlErrors.DistroMin and <= TellmaSqlErrors.DistroMax } assertion => AtRoot(assertion.Code),
                TreeCycleException => AtRoot(ValidationCodes.TreeCycle),
                TreeDepthExceededException => AtRoot(ValidationCodes.TreeTooDeep),
                UniqueConstraintViolationException => AtRoot(ValidationCodes.Unique),
                ForeignKeyViolationException { Conflict: ForeignKeyConflict.StillReferenced } => AtRoot(ValidationCodes.FkInUse),
                ForeignKeyViolationException => AtRoot(ValidationCodes.FkNotFound),
                _ => null,
            };
        }

        /// <summary>A validation failure at the root.</summary>
        /// <param name="code">The code.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The failure.</returns>
        private static ValidationException AtRoot(string code, params (string Name, object? Value)[] arguments)
        {
            Dictionary<string, object?> named = arguments.ToDictionary(static argument => argument.Name, static argument => argument.Value, StringComparer.Ordinal);
            return new ValidationException([new ValidationError(ValidationPath.Root, code, named)]);
        }
    }
}
