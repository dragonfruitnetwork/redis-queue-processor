// Copyright (c) DragonFruit Network <inbox@dragonfruit.network>
// Licensed under MIT. Refer to the LICENSE file for more info

using System;

namespace DragonFruit.Data.Queues
{
    public enum ScopeOptions
    {
        /// <summary>
        /// A new <see cref="IServiceProvider"/> will be created for each job
        /// </summary>
        PerJob,

        /// <summary>
        /// A new <see cref="IServiceProvider"/> will be created for each batch
        /// </summary>
        PerBatch,

        /// <summary>
        /// A new <see cref="IServiceProvider"/> will be created at the start, and reused until there are no jobs pending.
        /// This is ideal for sharing resources but not recommended on high-volume pipelines.
        /// </summary>
        PerCycle
    }
}
