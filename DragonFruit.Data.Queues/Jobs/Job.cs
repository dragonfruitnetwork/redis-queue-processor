// Copyright (c) DragonFruit Network <inbox@dragonfruit.network>
// Licensed under MIT. Refer to the LICENSE file for more info

using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace DragonFruit.Data.Queues.Jobs
{
    public abstract class Job
    {
        /// <summary>
        /// Performs the current job as an asynchronous task
        /// </summary>
        public abstract Task Perform(IServiceScope scope);
    }
}
