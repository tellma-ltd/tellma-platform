// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.CompilerServices;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §13.3: frames nest. An open frame (an API action, a job's handler, a provisioning
    // step) holds the pipeline frames of the operations it runs, and each of those holds the groups
    // enlisted with it. The chain rides the asynchronous flow, so a front-door write knows whether
    // it was called from inside a frame that is composing, and an injected IOpenWriteHost finds the
    // open frame its caller runs in.
    /// <summary>The write frames the current asynchronous flow runs in, innermost first.</summary>
    internal static class FrameFlow
    {
        /// <summary>The innermost frame of the current flow and the frames around it; null outside every frame.</summary>
        private static readonly AsyncLocal<FrameLink?> Current = new();

        /// <summary>Each frame's chain, as first entered.</summary>
        private static readonly ConditionalWeakTable<EnlistmentFrame, FrameLink> Links = [];

        /// <summary>The innermost open frame the current flow runs in; null when it runs in none.</summary>
        public static OpenWriteFrame? InnermostOpen
        {
            get
            {
                for (FrameLink? link = Current.Value; link is not null; link = link.Outer)
                {
                    if (link.Frame is OpenWriteFrame open)
                    {
                        return open;
                    }
                }

                return null;
            }
        }

        // A frame's tasks may resume on any flow of its rounds: a dispatch runs on the flow of the task
        // that parked last, which may be a group's. So a frame keeps the chain it was first entered on,
        // and every later entry restores that chain, whichever flow enters it.
        /// <summary>
        ///     Enters a frame: until the returned scope is disposed, it is the current flow's innermost,
        ///     with the frames around it that it was first entered inside.
        /// </summary>
        /// <param name="frame">The frame.</param>
        /// <returns>The scope, whose disposal restores the frames the flow ran in before.</returns>
        public static IDisposable Enter(EnlistmentFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            FrameLink? previous = Current.Value;
            Current.Value = Links.GetValue(frame, entered => new FrameLink(entered, previous));
            return new Leaving(previous);
        }

        /// <summary>Runs a task inside a frame, with the frames around it the frame was first entered inside.</summary>
        /// <param name="frame">The frame.</param>
        /// <param name="task">The task's body.</param>
        /// <returns>A task that completes when the body did.</returns>
        public static async Task RunInAsync(EnlistmentFrame frame, Func<Task> task)
        {
            ArgumentNullException.ThrowIfNull(task);
            using (Enter(frame))
            {
                await task().ConfigureAwait(false);
            }
        }

        // Spec 0015 §13.3, bypass resistance: a save, a delete or an action called through the front
        // door from inside a frame that is composing (a pipeline host between its id assignment and
        // the close of its last round, or an enlisted group's participants) would open a second
        // transaction beside the frame's one. It is an authoring bug; the write enlists instead. An
        // open frame composes nothing of its own, so an API action's method calls the front door.
        /// <summary>Refuses a front-door write called from inside a frame that is composing.</summary>
        /// <param name="resource">The stack the write was called on.</param>
        /// <param name="operation">The write's operation, such as <c>save</c>.</param>
        /// <exception cref="InvalidOperationException">A frame around the call is composing.</exception>
        public static void EnsureFrontDoor(string resource, string operation)
        {
            for (FrameLink? link = Current.Value; link is not null; link = link.Outer)
            {
                if (link.Frame.IsComposing)
                {
                    throw new InvalidOperationException(
                        $"The {operation} of {resource} was called through its service from inside the frame of {link.Frame.Operation}, which is composing, so it would run in a second transaction. Enlist the write with the frame's host instead: EnlistSaveAsync or EnlistDelete on the context's Host.");
                }
            }
        }

        /// <summary>One frame of the chain.</summary>
        /// <param name="Frame">The frame.</param>
        /// <param name="Outer">The frames around it; null for the outermost.</param>
        private sealed record FrameLink(EnlistmentFrame Frame, FrameLink? Outer);

        /// <summary>Restores the chain a frame was entered on.</summary>
        /// <param name="outer">The chain before the frame was entered.</param>
        private sealed class Leaving(FrameLink? outer) : IDisposable
        {
            /// <inheritdoc />
            public void Dispose()
            {
                Current.Value = outer;
            }
        }
    }
}
