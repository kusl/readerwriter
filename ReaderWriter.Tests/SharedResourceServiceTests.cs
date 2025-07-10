using Microsoft.Extensions.Logging;
using ReaderWriter.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ReaderWriter.Tests
{
    // Simple test logger implementation
    public class TestLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _logs = [];

        public class LogEntry
        {
            public LogLevel LogLevel { get; set; }
            public string Message { get; set; } = string.Empty;
            public Exception? Exception { get; set; }
        }

        public IReadOnlyList<LogEntry> Logs => _logs;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _logs.Add(new LogEntry
            {
                LogLevel = logLevel,
                Message = formatter(state, exception),
                Exception = exception
            });
        }
    }

    public class SharedResourceServiceTests
    {
        private static SharedResourceService CreateService(out TestLogger<SharedResourceService> logger)
        {
            logger = new TestLogger<SharedResourceService>();
            return new SharedResourceService(logger);
        }

        [Fact]
        public async Task WriteAsync_WithValidData_AddsItemToResource()
        {
            // Arrange
            SharedResourceService service = CreateService(out TestLogger<SharedResourceService>? logger);
            int writerId = 1;
            string data = "Test data";
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            await service.WriteAsync(writerId, data, cancellationToken);
            string readData = await service.ReadAsync(1, cancellationToken);

            // Assert
            Assert.Equal(data, readData);

            // Verify logging
            Assert.Contains(logger.Logs, log => log.Message.Contains("attempting to acquire write lock"));
            Assert.Contains(logger.Logs, log => log.Message.Contains("acquired write lock"));
            Assert.Contains(logger.Logs, log => log.Message.Contains("wrote data"));
            Assert.Contains(logger.Logs, log => log.Message.Contains("released write lock"));
        }

        [Fact]
        public async Task ReadAsync_OnEmptyResource_ReturnsDefault()
        {
            // Arrange
            SharedResourceService service = CreateService(out TestLogger<SharedResourceService>? logger);
            int readerId = 1;
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            string result = await service.ReadAsync(readerId, cancellationToken);

            // Assert
            Assert.Equal("No data available", result);

            // Verify logging
            Assert.Contains(logger.Logs, log => log.Message.Contains("attempting to acquire read lock"));
            Assert.Contains(logger.Logs, log => log.Message.Contains("acquired read lock"));
            Assert.Contains(logger.Logs, log => log.Message.Contains("read data"));
            Assert.Contains(logger.Logs, log => log.Message.Contains("released read lock"));
        }

        [Fact]
        public async Task WriterExclusivity_BlocksConcurrentReaders()
        {
            // Arrange
            SharedResourceService service = CreateService(out TestLogger<SharedResourceService>? logger);
            ManualResetEventSlim writeStarted = new(false);
            ManualResetEventSlim continueWrite = new(false);
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task writeTask = Task.Run(async () =>
            {
                await Task.Run(() =>
                {
                    // Custom write logic to control timing
                    System.Reflection.FieldInfo? field = typeof(SharedResourceService).GetField("_lock",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (field?.GetValue(null) is ReaderWriterLockSlim lockObj)
                    {
                        lockObj.EnterWriteLock();
                        try
                        {
                            writeStarted.Set();
                            continueWrite.Wait(); // Block until test signals to continue
                        }
                        finally
                        {
                            lockObj.ExitWriteLock();
                        }
                    }
                }, cancellationToken);
            }, cancellationToken);

            // Wait for write to start and acquire lock
            writeStarted.Wait();

            // Start reader tasks while writer holds the lock
            List<Task<bool>> readerTasks = [];
            for (int i = 0; i < 3; i++)
            {
                int readerId = i;
                readerTasks.Add(Task.Run(async () =>
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    await service.ReadAsync(readerId, cancellationToken);
                    sw.Stop();
                    // If reader was blocked, it should take at least 100ms
                    return sw.ElapsedMilliseconds > 100;
                }, cancellationToken));
            }

            // Give readers time to try acquiring lock (they should be blocked)
            await Task.Delay(200);

            // Release the writer
            continueWrite.Set();

            // Wait for all tasks
            await writeTask;
            bool[] results = await Task.WhenAll(readerTasks);

            // Assert - all readers should have been blocked
            Assert.All(results, blocked => Assert.True(blocked));
        }

        [Fact]
        public async Task WriterExclusivity_BlocksConcurrentWriters()
        {
            // Arrange
            SharedResourceService service = CreateService(out TestLogger<SharedResourceService>? logger);
            ManualResetEventSlim firstWriteStarted = new(false);
            ManualResetEventSlim continueFirstWrite = new(false);
            CancellationToken cancellationToken = CancellationToken.None;

            // Act
            Task firstWriteTask = Task.Run(async () =>
            {
                await Task.Run(() =>
                {
                    System.Reflection.FieldInfo? field = typeof(SharedResourceService).GetField("_lock",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (field?.GetValue(null) is ReaderWriterLockSlim lockObj)
                    {
                        lockObj.EnterWriteLock();
                        try
                        {
                            firstWriteStarted.Set();
                            continueFirstWrite.Wait();
                        }
                        finally
                        {
                            lockObj.ExitWriteLock();
                        }
                    }
                }, cancellationToken);
            }, cancellationToken);

            // Wait for first write to start
            firstWriteStarted.Wait();

            // Start second writer while first holds the lock
            Stopwatch sw = Stopwatch.StartNew();
            Task secondWriteTask = service.WriteAsync(2, "Second writer data", cancellationToken);

            // Give second writer time to try acquiring lock
            await Task.Delay(200);

            // Release first writer
            continueFirstWrite.Set();

            // Wait for both writes to complete
            await firstWriteTask;
            await secondWriteTask;
            sw.Stop();

            // Assert - second writer should have been blocked
            Assert.True(sw.ElapsedMilliseconds > 150);
        }

        [Fact]
        public async Task ReaderConcurrency_AllowsMultipleConcurrentReaders()
        {
            // Arrange
            SharedResourceService service = CreateService(out _);
            CancellationToken cancellationToken = CancellationToken.None;

            // Add initial data
            await service.WriteAsync(1, "Initial data", cancellationToken);

            // Act - Start multiple readers concurrently
            Stopwatch sw = Stopwatch.StartNew();
            List<Task> readerTasks = [];
            for (int i = 0; i < 5; i++)
            {
                int readerId = i;
                readerTasks.Add(service.ReadAsync(readerId, cancellationToken));
            }

            await Task.WhenAll(readerTasks);
            sw.Stop();

            // Assert - Total time should be much less than sequential reads would take
            // With delays of 100-500ms per read, 5 sequential reads would take at least 500ms
            // Concurrent reads should complete in roughly the time of one read
            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"Concurrent reads took {sw.ElapsedMilliseconds}ms, expected less than 1000ms");
        }

        [Fact]
        public async Task HighConcurrency_MaintainsDataIntegrity()
        {
            // Arrange
            SharedResourceService service = CreateService(out TestLogger<SharedResourceService>? logger);
            CancellationToken cancellationToken = CancellationToken.None;
            int writersCount = 5;
            int readersCount = 50;
            List<string> expectedWrites = [];
            object writeLock = new();

            // Act - Run writers and readers concurrently
            List<Task> tasks = [];

            // Create writer tasks
            for (int i = 0; i < writersCount; i++)
            {
                int writerId = i;
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 10; j++)
                    {
                        string data = $"Writer{writerId}-Item{j}";
                        lock (writeLock)
                        {
                            expectedWrites.Add(data);
                        }
                        await service.WriteAsync(writerId, data, cancellationToken);
                    }
                }, cancellationToken));
            }

            // Create reader tasks
            for (int i = 0; i < readersCount; i++)
            {
                int readerId = i;
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 5; j++)
                    {
                        await service.ReadAsync(readerId, cancellationToken);
                    }
                }, cancellationToken));
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);

            // Assert - Verify data integrity
            // Use reflection to access the internal state
            System.Reflection.FieldInfo? field = typeof(SharedResourceService).GetField("_sharedData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(service) is List<string> sharedData)
            {
                Assert.Equal(writersCount * 10, sharedData.Count);
                Assert.Equal(expectedWrites.Count, sharedData.Count);

                // Verify all expected writes are present
                foreach (string expectedWrite in expectedWrites)
                {
                    Assert.Contains(expectedWrite, sharedData);
                }

                // Verify no duplicate or unexpected data
                Assert.Equal(sharedData.Count, sharedData.Distinct().Count());
            }
            else
            {
                Assert.Fail("Could not access internal shared data");
            }
        }

        [Fact]
        public async Task MultipleReadersGetSameData_WhenNoWritesOccur()
        {
            // Arrange
            SharedResourceService service = CreateService(out TestLogger<SharedResourceService>? logger);
            CancellationToken cancellationToken = CancellationToken.None;
            string testData = "Test data for concurrent reads";

            // Write initial data
            await service.WriteAsync(1, testData, cancellationToken);

            // Act - Multiple readers read concurrently
            List<Task<string>> readTasks = [];
            for (int i = 0; i < 10; i++)
            {
                readTasks.Add(service.ReadAsync(i, cancellationToken));
            }

            string[] results = await Task.WhenAll(readTasks);

            // Assert - All readers should get the same data
            Assert.All(results, result => Assert.Equal(testData, result));
        }

        [Fact]
        public async Task Service_HandlesRapidReadWriteAlternation()
        {
            // Arrange
            SharedResourceService service = CreateService(out TestLogger<SharedResourceService>? logger);
            CancellationToken cancellationToken = CancellationToken.None;
            int iterations = 20;
            List<Exception> errors = [];

            // Act - Rapidly alternate between reads and writes
            List<Task> tasks = [];

            for (int i = 0; i < iterations; i++)
            {
                int iteration = i;

                // Writer task
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await service.WriteAsync(iteration, $"Data-{iteration}", cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                        {
                            errors.Add(ex);
                        }
                    }
                }, cancellationToken));

                // Reader task
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await service.ReadAsync(iteration, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                        {
                            errors.Add(ex);
                        }
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);

            // Assert - No errors should occur
            Assert.Empty(errors);
        }
    }
}