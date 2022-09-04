// Copyright (c) DragonFruit Network <inbox@dragonfruit.network>
// Licensed under MIT. Refer to the LICENSE file for more info

using System;

namespace DragonFruit.Data.Queues.Jobs
{
    /// <summary>
    /// Marks a class as being able to be inserted into a generic queue
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class JobTypeId : Attribute
    {
        public JobTypeId(string typeId)
        {
            TypeId = typeId;
        }

        /// <summary>
        /// The unique id of the job type
        /// </summary>
        public string TypeId { get; }
    }
}
