// Copyright (c) DragonFruit Network <inbox@dragonfruit.network>
// Licensed under MIT. Refer to the LICENSE file for more info

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DragonFruit.Data.Queues.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using StackExchange.Redis;

namespace DragonFruit.Data.Queues
{
    /// <summary>
    /// A lightweight job processor using redis as an intermediate storage provider.
    /// </summary>
    public class QueueProcessor<T> : BackgroundService where T : Job
    {
        private int _maxConcurrency;
        private readonly string _queueKey;
        private readonly string _queueEventsKey;

        private readonly ILogger _logger;
        private readonly IConnectionMultiplexer _redis;
        private readonly IServiceScopeFactory _scopeFactory;

        private readonly AsyncManualResetEvent _processorSignal = new();
        private readonly IDictionary<string, Type> _jobMap = new Dictionary<string, Type>();

        public QueueProcessor(ILogger logger, IServiceScopeFactory scopeFactory, IConnectionMultiplexer redis, string queueKey, int databaseId = 0)
        {
            _redis = redis;
            _logger = logger;
            _scopeFactory = scopeFactory;

            _queueKey = queueKey;
            _queueEventsKey = $"__keyspace@{databaseId}__:{queueKey}";
        }

        /// <summary>
        /// Gets or sets the max batch size the processor can consume at one time.
        /// Setting this to 0 will prevent it from retrieving queued jobs.
        /// </summary>
        public int MaxConcurrency
        {
            get => _maxConcurrency;
            set => _maxConcurrency = Math.Max(value, 0);
        }

        /// <summary>
        /// Gets or sets the lifetime of a <see cref="IServiceProvider"/> created when processing jobs.
        /// For legacy reasons, this defaults to <see cref="ScopeOptions.PerJob"/>
        /// </summary>
        public ScopeOptions ScopeLifetime { get; set; }

        /// <summary>
        /// The <see cref="JsonSerializerOptions"/> to use when serializing/deserializing queue entries
        /// </summary>
        public JsonSerializerOptions SerializerOptions { get; set; }

        /// <summary>
        /// Register jobs to be runnable on this queue
        /// </summary>
        /// <param name="assembly">The assembly to reflect to discover job types</param>
        /// <exception cref="DuplicateNameException">The job has already been registered, or a duplicate name has been found</exception>
        public void RegisterJobs(Assembly assembly)
        {
            foreach (var type in assembly.ExportedTypes.Where(x => !x.IsAbstract && !x.IsInterface && typeof(Job).IsAssignableFrom(x)))
            {
                RegisterJob(type);
            }
        }

        /// <summary>
        /// Register a single job on this queue. This can be called multiple times on different types without issue.
        /// </summary>
        /// <param name="type">The job type to register</param>
        /// <exception cref="DuplicateNameException">The job has already been registered, or a duplicate name has been found</exception>
        public void RegisterJob(Type type)
        {
            var typeId = GetJobTypeId(type);

            // ensure there are no duplicates
            if (!_jobMap.TryAdd(typeId, type))
            {
                throw new DuplicateNameException($"Duplicate key {typeId} was found");
            }
        }

        /// <summary>
        /// Queue one or more jobs to be run on the task processor
        /// </summary>
        /// <param name="jobs">The jobs to enqueue</param>
        public Task EnqueueAsync(params T[] jobs) => EnqueueAsync(jobs, null);

        /// <summary>
        /// Queue a collection of jobs to be run on the task processor
        /// </summary>
        /// <param name="jobs">The jobs to enqueue</param>
        /// <param name="transaction">Optional transaction to queue against</param>
        public async Task EnqueueAsync(IReadOnlyCollection<T> jobs, ITransaction transaction = null)
        {
            var index = 0;
            var convertedJobs = new SortedSetEntry[jobs.Count];

            foreach (var job in jobs)
            {
                // needs to be an object to get serialization to work
                var jobEntry = new JobWrapper<object>(GetJobTypeId(job.GetType()), job);

                if (job.AllowDuplicates)
                {
                    // setting a guid will prevent set from detecting as a duplicate
                    jobEntry.JobId = Guid.NewGuid().ToString("D");
                }

                // use unix epoch as the score
                var utf8Bytes = JsonSerializer.SerializeToUtf8Bytes<object>(jobEntry, SerializerOptions);
                convertedJobs[index++] = new SortedSetEntry(utf8Bytes.AsMemory(), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }

            if (transaction == null)
            {
                await _redis.GetDatabase().SortedSetAddAsync(_queueKey, convertedJobs, SortedSetWhen.NotExists).ConfigureAwait(false);
                _processorSignal.Set();
            }
            else
            {
                // do not await on a transaction because it'll deadlock waiting for the tx to commit
                // see https://stackexchange.github.io/StackExchange.Redis/Transactions.html#and-in-stackexchangeredis
                _ = transaction.SortedSetAddAsync(_queueKey, convertedJobs, SortedSetWhen.NotExists);
            }
        }

        /// <summary>
        /// Clears/Deletes the queue
        /// </summary>
        /// <returns>Whether the operation was successful, in an async <see cref="Task"/></returns>
        public Task<bool> Clear() => _redis.GetDatabase().KeyDeleteAsync(_queueKey);

        protected override async Task ExecuteAsync(CancellationToken cancellation)
        {
            await _redis.GetSubscriber().SubscribeAsync(_queueEventsKey, OnQueueEvent).ConfigureAwait(false);

            while (true)
            {
                // wait for processing
                await _processorSignal.WaitAsync(cancellation).ConfigureAwait(false);

                // return if we were cancelled
                if (cancellation.IsCancellationRequested)
                {
                    break;
                }

                if (MaxConcurrency == 0)
                {
                    _processorSignal.Reset();
                    continue;
                }

                var jobCycleScope = new Lazy<IServiceScope>(() => _scopeFactory.CreateScope(), LazyThreadSafetyMode.None);
                var jobBatch = Array.Empty<SortedSetEntry>();

                _logger?.Log(LogLevel.Information, "Queue processing started ({name})", _queueKey);

                do
                {
                    Lazy<List<IServiceScope>> jobScopes = null;
                    IServiceScope batchScope = null;

                    try
                    {
                        // get job and process each one
                        _logger?.Log(LogLevel.Debug, "Fetching next batch of items");
                        jobBatch = await _redis.GetDatabase().SortedSetPopAsync(_queueKey, MaxConcurrency).ConfigureAwait(false);

                        if (!jobBatch.Any())
                        {
                            continue;
                        }

                        var jobTasks = new List<Task>(jobBatch.Length);

                        // init scopes
                        batchScope = ScopeLifetime == ScopeOptions.PerBatch ? _scopeFactory.CreateScope() : null;
                        jobScopes = new Lazy<List<IServiceScope>>(() => new List<IServiceScope>(jobTasks.Capacity), LazyThreadSafetyMode.None);

                        // convert batch to tasks
                        foreach (var entry in jobBatch)
                        {
                            var jobInfo = JsonSerializer.Deserialize<JobWrapper<JsonObject>>((byte[])entry.Element, SerializerOptions);

                            if (!_jobMap.TryGetValue(jobInfo.JobTypeId, out var type))
                            {
                                _logger?.Log(LogLevel.Error, "Invalid job discovered in queue: {job}", jobInfo);
                                continue;
                            }

                            if (jobInfo.Data.Deserialize(type) is not Job job)
                            {
                                continue;
                            }

                            if (ScopeLifetime == ScopeOptions.PerCycle)
                            {
                                jobTasks.Add(job.Perform(jobCycleScope.Value.ServiceProvider));
                            }
                            else if (batchScope != null)
                            {
                                jobTasks.Add(job.Perform(batchScope.ServiceProvider));
                            }
                            else
                            {
                                var scope = _scopeFactory.CreateScope();
                                var jobTask = job.Perform(scope.ServiceProvider);

                                jobTasks.Add(jobTask);
                                jobScopes.Value.Add(scope);
                            }
                        }

                        // wait for completion
                        await Task.WhenAll(jobTasks).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        _logger?.Log(LogLevel.Error, e, "Queue processing failed ({queue})", _queueKey);
                    }
                    finally
                    {
                        // clear all PerJob scopes
                        batchScope?.Dispose();

                        if (jobScopes?.IsValueCreated == true)
                        {
                            foreach (var scope in jobScopes.Value)
                            {
                                scope.Dispose();
                            }
                        }
                    }
                } while (jobBatch.Any() && !cancellation.IsCancellationRequested);

                // dispose global scope
                if (jobCycleScope.IsValueCreated)
                {
                    jobCycleScope.Value.Dispose();
                }

                // reset processing signal
                _processorSignal.Reset();
                _logger?.Log(LogLevel.Information, "Queue processing complete ({queue})", _queueKey);
            }

            await _redis.GetSubscriber().UnsubscribeAsync(_queueEventsKey, OnQueueEvent).ConfigureAwait(false);
        }

        private void OnQueueEvent(RedisChannel channel, RedisValue action)
        {
            if (action.ToString().Equals("zadd", StringComparison.OrdinalIgnoreCase))
            {
                // get the queue processor started
                _processorSignal.Set();
            }
        }

        private static string GetJobTypeId(MemberInfo type) => type.GetCustomAttribute<JobTypeId>()?.TypeId ?? type.Name;
    }
}
