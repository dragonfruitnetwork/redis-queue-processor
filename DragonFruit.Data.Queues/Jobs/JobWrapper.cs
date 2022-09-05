// Copyright (c) DragonFruit Network <inbox@dragonfruit.network>
// Licensed under MIT. Refer to the LICENSE file for more info

using System.Text.Json.Serialization;

namespace DragonFruit.Data.Queues
{
    internal class JobWrapper<T>
    {
        public JobWrapper(string jobTypeId, T data)
        {
            JobTypeId = jobTypeId;
            Data = data;
        }

        [JsonPropertyName("job_type_id")]
        public string JobTypeId { get; set; }

        [JsonPropertyName("job_id")]
        public string JobId { get; set; }

        [JsonPropertyName("props")]
        public T Data { get; set; }
    }
}
