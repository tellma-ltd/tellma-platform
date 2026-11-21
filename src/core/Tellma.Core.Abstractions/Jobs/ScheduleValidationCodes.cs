// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     The codes the schedules' stack refuses a save, a delete or an action with, each at the
    ///     property it names, or at the row for a delete or an action. Each is a resource key the
    ///     client renders with the error's arguments.
    /// </summary>
    public static class ScheduleValidationCodes
    {
        /// <summary>The cron expression does not parse as five-field Unix cron. Argument: <c>expression</c>.</summary>
        public const string CronInvalid = "Schedules.CronInvalid";

        /// <summary>The cron expression parses but has no further occurrence, so the schedule would never fire. Argument: <c>expression</c>.</summary>
        public const string CronNeverOccurs = "Schedules.CronNeverOccurs";

        /// <summary>The time zone is outside the time-zone value set; an alias of a zone is outside it too. Argument: <c>timeZone</c>.</summary>
        public const string TimeZoneUnknown = "Schedules.TimeZoneUnknown";

        /// <summary>No handler registers the handler key, or a feature retired it. Argument: <c>handlerKey</c>.</summary>
        public const string HandlerUnknown = "Schedules.HandlerUnknown";

        /// <summary>The handler is registered but does not allow user-created schedules. Argument: <c>handlerKey</c>.</summary>
        public const string HandlerNotSchedulable = "Schedules.HandlerNotSchedulable";

        /// <summary>
        ///     Every missed occurrence is replayed, which only a schedule whose runs may overlap allows:
        ///     replayed occurrences are independent jobs that may run at once.
        /// </summary>
        public const string ReplayAllRequiresAllow = "Schedules.ReplayAllRequiresAllow";

        /// <summary>The arguments are larger than a job may carry. Argument: <c>maxBytes</c>, in UTF-8 bytes.</summary>
        public const string ArgumentsTooLarge = "Schedules.ArgumentsTooLarge";

        /// <summary>
        ///     The save changes what another user's schedule runs (its handler, arguments, expression or
        ///     zone); the caller takes the schedule over first.
        /// </summary>
        public const string OwnedByAnotherUser = "Schedules.OwnedByAnotherUser";

        /// <summary>The schedule runs as a user who is no longer active; the caller takes it over first.</summary>
        public const string OwnerInactive = "Schedules.OwnerInactive";

        /// <summary>
        ///     The schedule is built in: only its names, expression and zone change, it is never
        ///     deleted, and it always runs as the platform.
        /// </summary>
        public const string BuiltInImmutable = "Schedules.BuiltInImmutable";

        /// <summary>The schedule is built in and is never deactivated.</summary>
        public const string BuiltInAlwaysActive = "Schedules.BuiltInAlwaysActive";

        /// <summary>A job of the schedule has not ended, and the schedule's runs may not overlap.</summary>
        public const string RunInProgress = "Schedules.RunInProgress";
    }
}
