using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
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
    public class SharedResourceServiceTests
    {
        private SharedResourceService CreateService(out FakeLogger<SharedResourceService> logger)
        {
            logger = new FakeLogger<SharedResourceService>();
            return new SharedResourceService(logger);
        }

        [Fact]
        public async Task WriteAsync_WithValidData_AddsItemToResource()
        {
            // Arrange
            var service = CreateService(out var logger);
            var writerId = 1;
            var data = "Test data";
            var cancellationToken = CancellationToken.None;

            // Act
            await service.WriteAsync(writerId, data, cancellationToken);
            var readData = await service.ReadAsync(1, cancellationToken);

            // Assert
            Assert.Equal(data, readData);

            // Verify logging
            var logs = logger.GetLogs();
            Assert.Contains(logs, log => log.Message.Contains("attempting to acquire write lock"));
            Assert.Contains(logs, log => log.Message.Contains("acquired write lock"));
            Assert.Contains(logs, log => log.Message.Contains("wrote data"));
            Assert.Contains(logs, log => log.Message.Contains("released write lock"));
        }

        [Fact]
        public async Task ReadAsync_OnEmptyResource_ReturnsDefault()
        {
            // Arrange
            var service = CreateService(out var logger);
            var readerId = 1;
            var cancellationToken = CancellationToken.None;

            // Act
            var result = await service.ReadAsync(readerId, cancellationToken);

            // Assert
            Assert.Equal("No data available", result);

            // Verify logging
            var logs = logger.GetLogs();
            Assert.Contains(logs, log => log.Message.Contains("attempting to acquire read lock"));
            Assert.Contains(logs, log => log.Message.Contains("acquired read lock"));
            Assert.Contains(logs, log => log.Message.Contains("read data"));
            Assert.Contains(logs, log => log.Message.Contains("released read lock"));
        }

        [Fact]
        public async Task WriterExclusivity_BlocksConcurrentReaders()
        {
            // Arrange
            var service = CreateService(out var logger);
            var writeStarted = new ManualResetEventSlim(false);
            var continueWrite = new ManualResetEventSlim(false);
            var cancellationToken = CancellationToken.None;

            // Act
            var writeTask = Task.Run(async () =>
            {
                await Task.Run(() =>
                {
                    // Custom write logic to control timing
                    var field = typeof(SharedResourceService).GetField("_lock",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var lockObj = (ReaderWriterLockSlim)field.GetValue(null);

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
                });
            });

            // Wait for write to start and acquire lock
            writeStarted.Wait();

            // Start reader tasks while writer holds the lock
            var readerTasks = new List<Task<bool>>();
            for (int i = 0; i < 3; i++)
            {
                var readerId = i;
                readerTasks.Add(Task.Run(async () =>
                {
                    var sw = Stopwatch.StartNew();
                    await service.ReadAsync(readerId, cancellationToken);
                    sw.Stop();
                    // If reader was blocked, it should take at least 100ms
                    return sw.ElapsedMilliseconds > 100;
                }));
            }

            // Give readers time to try acquiring lock (they should be blocked)
            await Task.Delay(200);

            // Release the writer
            continueWrite.Set();

            // Wait for all tasks
            await writeTask;
            var results = await Task.WhenAll(readerTasks);

            // Assert - all readers should have been blocked
            Assert.All(results, blocked => Assert.True(blocked));
        }

        [Fact]
        public async Task WriterExclusivity_BlocksConcurrentWriters()
        {
            // Arrange
            var service = CreateService(out var logger);
            var firstWriteStarted = new ManualResetEventSlim(false);
            var continueFirstWrite = new ManualResetEventSlim(false);
            var cancellationToken = CancellationToken.None;

            // Act
            var firstWriteTask = Task.Run(async () =>
            {
                await Task.Run(() =>
                {
                    var field = typeof(SharedResourceService).GetField("_lock",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    var lockObj = (ReaderWriterLockSlim)field.GetValue(null);

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
                });
            });

            // Wait for first write to start
            firstWriteStarted.Wait();

            // Start second writer while first holds the lock
            var sw = Stopwatch.StartNew();
            var secondWriteTask = service.WriteAsync(2, "Second writer data", cancellationToken);

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
            var service = CreateService(out var logger);
            var cancellationToken = CancellationToken.None;

            // Add initial data
            await service.WriteAsync(1, "Initial data", cancellationToken);

            // Act - Start multiple readers concurrently
            var sw = Stopwatch.StartNew();
            var readerTasks = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                var readerId = i;
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
            var service = CreateService(out var logger);
            var cancellationToken = CancellationToken.None;
            var writersCount = 5;
            var readersCount = 50;
            var expectedWrites = new List<string>();
            var writeLock = new object();

            // Act - Run writers and readers concurrently
            var tasks = new List<Task>();

            // Create writer tasks
            for (int i = 0; i < writersCount; i++)
            {
                var writerId = i;
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 10; j++)
                    {
                        var data = $"Writer{writerId}-Item{j}";
                        lock (writeLock)
                        {
                            expectedWrites.Add(data);
                        }
                        await service.WriteAsync(writerId, data, cancellationToken);
                    }
                }));
            }

            // Create reader tasks
            for (int i = 0; i < readersCount; i++)
            {
                var readerId = i;
                tasks.Add(Task.Run(async () =>
                {
                    for (int j = 0; j < 5; j++)
                    {
                        await service.ReadAsync(readerId, cancellationToken);
                    }
                }));
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);

            // Assert - Verify data integrity
            // Use reflection to access the internal state
            var field = typeof(SharedResourceService).GetField("_sharedData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var sharedData = (List<string>)field.GetValue(service);

            Assert.Equal(writersCount * 10, sharedData.Count);
            Assert.Equal(expectedWrites.Count, sharedData.Count);

            // Verify all expected writes are present
            foreach (var expectedWrite in expectedWrites)
            {
                Assert.Contains(expectedWrite, sharedData);
            }

            // Verify no duplicate or unexpected data
            Assert.Equal(sharedData.Count, sharedData.Distinct().Count());
        }

        [Fact]
        public async Task MultipleReadersGetSameData_WhenNoWritesOccur()
        {
            // Arrange
            var service = CreateService(out var logger);
            var cancellationToken = CancellationToken.None;
            var testData = "Test data for concurrent reads";

            // Write initial data
            await service.WriteAsync(1, testData, cancellationToken);

            // Act - Multiple readers read concurrently
            var readTasks = new List<Task<string>>();
            for (int i = 0; i < 10; i++)
            {
                readTasks.Add(service.ReadAsync(i, cancellationToken));
            }

            var results = await Task.WhenAll(readTasks);

            // Assert - All readers should get the same data
            Assert.All(results, result => Assert.Equal(testData, result));
        }

        [Fact]
        public async Task Service_HandlesRapidReadWriteAlternation()
        {
            // Arrange
            var service = CreateService(out var logger);
            var cancellationToken = CancellationToken.None;
            var iterations = 20;
            var errors = new List<Exception>();

            // Act - Rapidly alternate between reads and writes
            var tasks = new List<Task>();

            for (int i = 0; i < iterations; i++)
            {
                var iteration = i;

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
                }));

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
                }));
            }

            await Task.WhenAll(tasks);

            // Assert - No errors should occur
            Assert.Empty(errors);
        }
    }
}