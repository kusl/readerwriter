# ReaderWriter

Caution: contains LLM generated code 

write a detailed, complete, concise specification for a classic reader writer problem implemented with best practices in dotnet 9 using as few dependencies as possible, implemented as three csproj projects -- 1. a very sparse console application, 2. a class library, 3. an xunit unit test project. do not write any code yet. this exercise is to write the full specification first so I can feed the specification to an LLM. There should be no console log in the class library itself, only ilogger<T> and the application should follow all solid principles as well as domain driven development. remember, this is a classic reader writer problem but the implementation should use built in and first party dotnet stuff where necessary, no need to boil the ocean by writing my own primitives. use as few nuget projects as possible and definitely avoid any "non-free" nuget packages for example definitely avoid moq, mediatR, and so on. the class lib should not assume the number of readers and writers, all this should be driven by app settings / secrets json in the console application. the logging should be both to the console as well as to a daily rotated text file. the unit testing should be complete. Each csproj should only have one csharp file so it is easy for me to copy paste the text. 


generate this spec as markdown so it is easy for me to copy paste. 
































































































Of course. I will create a detailed specification document for a classic reader-writer problem implemented in .NET 9.

This specification outlines a three-project solution that adheres to SOLID principles and a domain-driven design approach. It emphasizes the use of built-in .NET features and minimal, free dependencies, with configuration driven by appsettings.json and comprehensive logging and unit testing.

Here is the complete specification in Markdown format.
Specification: .NET Reader-Writer Problem
Jul 10, 7:29 PM



Specification: .NET Reader-Writer Problem Implementation1. OverviewThis document specifies the design and implementation of a solution to the classic reader-writer problem. The implementation will be done in .NET 9, following modern best practices, including SOLID principles, dependency injection, structured logging, and configuration-driven behavior.The goal is to create a robust and testable class library that manages concurrent access to a shared resource, a console application to simulate the readers and writers, and a comprehensive unit test suite to guarantee correctness. The solution will use built-in .NET synchronization primitives (ReaderWriterLockSlim) and will rely on as few external NuGet packages as possible.2. Solution & Project StructureThe solution will be named ReaderWriterSolution and will contain three C# projects. Each project is designed to have a single primary C# file to maintain simplicity as requested.ReaderWriter.Core: A .NET 9 class library (netstandard2.1 or net9.0) containing the core, thread-safe logic for managing the shared resource.ReaderWriter.ConsoleApp: A .NET 9 console application that acts as the composition root and host. It will configure and run the simulation.ReaderWriter.Tests: An xUnit test project for verifying the correctness of the ReaderWriter.Core library.3. Project: ReaderWriter.Core (Class Library)This project contains the domain logic, completely decoupled from any specific application concerns like console I/O.3.1. PurposeTo provide a thread-safe service that allows multiple "reader" clients to access a shared resource concurrently and "writer" clients to gain exclusive access to modify the resource.3.2. File StructureSharedResourceService.cs: Contains all interfaces and the class implementation for this project.3.3. NuGet DependenciesMicrosoft.Extensions.Logging.Abstractions3.4. Interfaces and ClassesISharedResourceService (Interface)Defines the public contract for interacting with the shared resource.public interface ISharedResourceService
{
    Task<string> ReadAsync(int readerId, CancellationToken cancellationToken);
    Task WriteAsync(int writerId, string data, CancellationToken cancellationToken);
}
SharedResourceService (Class)The concrete implementation of ISharedResourceService.Dependencies (Constructor Injected):ILogger<SharedResourceService>: For structured logging. No Console.WriteLine will be used within this class.Internal State:A private static readonly ReaderWriterLockSlim _lock = new();: The core synchronization primitive.A private readonly List<string> _sharedData = new();: The shared resource being protected.A private readonly Random _random = new();: To simulate variable work duration.Methods:public Task<string> ReadAsync(int readerId, CancellationToken cancellationToken):Logs an attempt to acquire a read lock.Enters a read lock using _lock.EnterReadLock().Logs that the read lock was acquired.Reads the last item from _sharedData (or a default value if empty).Logs the data that was read.Exits the read lock using _lock.ExitReadLock().Logs that the read lock was released.Returns the data that was read.The entire operation must be wrapped in a try...finally block to ensure ExitReadLock() is always called.public Task WriteAsync(int writerId, string data, CancellationToken cancellationToken):Logs an attempt to acquire a write lock.Enters a write lock using _lock.EnterWriteLock().Logs that the write lock was acquired.Adds the data string to the _sharedData list.Logs the data that was written and the new count of items.Exits the write lock using _lock.ExitWriteLock().Logs that the write lock was released.The entire operation must be wrapped in a try...finally block to ensure ExitWriteLock() is always called.4. Project: ReaderWriter.ConsoleApp (Console Application)This project is the executable entry point. It is responsible for configuration, dependency injection setup, and orchestration of the simulation.4.1. PurposeTo host the ReaderWriter.Core service and simulate concurrent access from a configurable number of readers and writers.4.2. File StructureProgram.cs: The single file containing all application startup and execution logic.appsettings.json: For all external configuration.4.3. NuGet DependenciesMicrosoft.Extensions.HostingSerilog.Extensions.HostingSerilog.Sinks.ConsoleSerilog.Sinks.File4.4. Configuration (appsettings.json)The application will be driven by the following configuration structure.{
  "SimulationSettings": {
    "NumberOfReaders": 10,
    "NumberOfWriters": 3,
    "SimulationDurationSeconds": 30
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "Console"
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/log-.txt",
          "rollingInterval": "Day",
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  }
}
4.5. Logic (Program.cs)SimulationSettings Class: A POCO class to bind the SimulationSettings section from appsettings.json.Main Method:Set up the Generic Host using Host.CreateDefaultBuilder(args).Use UseSerilog() to configure logging from IConfiguration.In ConfigureServices:Register SimulationSettings from the configuration file.Register ISharedResourceService with its implementation SharedResourceService as a singleton: services.AddSingleton<ISharedResourceService, SharedResourceService>();.Build and run the host.The main application logic will be implemented in a class that is started by the host (e.g., an IHostedService).SimulationHost (implements IHostedService)Inject ILogger<SimulationHost>, ISharedResourceService, and IOptions<SimulationSettings>.In StartAsync:Log the start of the simulation with the configured settings.Create a CancellationTokenSource that will be cancelled after SimulationDurationSeconds.Create NumberOfWriters tasks. Each task will run in a loop, calling WriteAsync with a unique writer ID and some generated data, until the cancellation token is triggered.Create NumberOfReaders tasks. Each task will run in a loop, calling ReadAsync with a unique reader ID, until the cancellation token is triggered.Combine all tasks into a single Task using Task.WhenAll().In StopAsync:Log the end of the simulation.5. Project: ReaderWriter.Tests (xUnit Project)This project ensures the ReaderWriter.Core library is correct, robust, and thread-safe.5.1. PurposeTo provide automated unit and integration tests for the SharedResourceService.5.2. File StructureSharedResourceServiceTests.cs: Contains all xUnit tests.5.3. NuGet Dependenciesxunitxunit.runner.visualstudioMicrosoft.NET.Test.SdkMicrosoft.Extensions.Logging.Testing (Provides FakeLogger to avoid a real logging framework).5.4. Test Scenarios (SharedResourceServiceTests.cs)Test Setup: Each test will create a new instance of SharedResourceService with a FakeLogger<SharedResourceService> to inspect log messages if needed.Tests:WriteAsync_WithValidData_AddsItemToResource: A single writer calls WriteAsync. The test verifies that ReadAsync subsequently returns the written data.ReadAsync_OnEmptyResource_ReturnsDefault: A single reader calls ReadAsync on a new, empty resource. The test verifies a default or null value is returned without error.WriterExclusivity_BlocksConcurrentReaders:Arrange: Start a WriteAsync call that is manually blocked inside the lock (e.g., using a ManualResetEventSlim).Act: While the writer is "working", start several ReadAsync calls.Assert: Verify that the reader tasks do not complete until the writer's ManualResetEventSlim is set and the write lock is released.WriterExclusivity_BlocksConcurrentWriters:Arrange: Similar to the reader test, block a WriteAsync call inside the lock.Act: While the first writer is "working", start a second WriteAsync call.Assert: Verify the second writer task does not complete until the first writer has released its lock.ReaderConcurrency_AllowsMultipleConcurrentReaders:Arrange: Have one writer add initial data.Act: Start multiple ReadAsync tasks concurrently and measure the total elapsed time.Assert: The total time should be close to the duration of a single read operation (plus some overhead), not the sum of all read durations, demonstrating that they ran in parallel.HighConcurrency_MaintainsDataIntegrity:Arrange: Set up a test to run, for example, 5 writers and 50 readers concurrently for a few seconds. Each writer will write a unique, identifiable piece of data.Act: Run all tasks to completion using Task.WhenAll.Assert: After all tasks are finished, inspect the internal state of the shared resource. Verify that the total number of items in the resource list exactly matches the total number of write operations that were successfully completed.





























































