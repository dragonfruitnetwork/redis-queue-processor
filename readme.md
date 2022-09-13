# DragonFruit Redis Queue Processor
[![DragonFruit Discord](https://img.shields.io/discord/482528405292843018?label=Discord&style=popout)](https://discord.gg/VA26u5Z)
[![Nuget](https://img.shields.io/nuget/v/DragonFruit.Data.Queues)](https://nuget.org/packages/DragonFruit.Data.Queues)

A lightweight queue processor for C# using redis and json.

## Overview
This library aims to provide a lightweight queue processor using a redis sorted set as storage, json to perform serialization and keyspace events to enable distributed processing.
It is designed for use in ASP.NET Core projects that use the built-in dependency-injection service. Shutdown signals are respected and processing will aim to finish before shutdown.

If the program shuts down mid-way through processing, any queued tasks will not be lost and will resume once the program has re-initialised.

### How to use

1. Install the latest nuget package (see badges above)
2. Enable key**space** events for sorted sets on the redis database
   ```
       # redis.conf
       notify-keyspace-events Kz
   
       # redis cli
       CONFIG SET notify-keyspace-events Kz
   ```
3. Configure and add an `IConnectionMultiplexer` as a singleton in `Program.cs` (or `Startup.cs` on older style projects)
4. For each job type, update the class to inherit `Job`. Ideally, the class should also be decorated with `[JobTypeId("id")]` to allow for future refactoring.
5. Configure the queue processors:
    - `services.AddQueueProcessor("queue:key", 5)` will create a queue processor that will process tasks on the set `queue:key` for any type and complete jobs in batches of 5.
    - `services.AddQueueProcessor<T>("queue:key")` will process one task at a time, but only allow the specified type to be processed.
6. Access and push jobs to the queue:
   ```csharp
   public class TestController: Controller
   {
       // for generic processors, the generic type is IQueueJob
       private readonly QueueProcessor<IQueueJob> _queue;
   
       public TestController(QueueProcessor<IQueueJob> queue)
       {
           _queue = queue;
       }
   
       public async Task QueueJob()
       {
           // TestJob impliments IQueueJob and has a parameterless constructor
           var newJob = new TestJob("params");
   
           // this also accepts an array of jobs for bulk submission
           _queue.Enqueue(newJob);
       }
   }
   ```
