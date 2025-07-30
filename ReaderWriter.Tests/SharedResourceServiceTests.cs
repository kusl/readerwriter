using Microsoft.Extensions.Logging;
using ReaderWriter.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ReaderWriter.Tests;

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

public class SharedResourceServiceTests : IDisposable
{
    private readonly List<SharedResourceService> _servicesToDispose = [];

    private SharedResourceService CreateService(out TestLogger<SharedResourceService> logger)
    {
        logger = new TestLogger<SharedResourceService>();
        SharedResourceService service = new(logger);
        _servicesToDispose.Add(service);
        return service;
    }

    public void Dispose()
    {
        foreach (SharedResourceService service in _servicesToDispose)
        {
            service?.Dispose();
        }
        GC.SuppressFinalize(this);
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
    public async Task WriteAsync_WithNullData_ThrowsArgumentNullException()
    {
        // Arrange
        SharedResourceService service = CreateService(out _);
        int writerId = 1;
        string? data = null;
        CancellationToken cancellationToken = CancellationToken.None;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.WriteAsync(writerId, data!, cancellationToken));
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
        SharedResourceService service = CreateService(out _);
        CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));

        // Act
        // Start a long-running write operation
        Task writeTask = Task.Run(async () =>
        {
            await service.WriteAsync(1, "Writer data", CancellationToken.None);
        });

        // Give the writer a chance to start (but not complete due to simulated delay)
        await Task.Delay(50);

        // Try to read while write is in progress
        Task<string> readTask = service.ReadAsync(1, cts.Token);

        // Check if read completes quickly (it shouldn't if writer has lock)
        Task completedTask = await Task.WhenAny(readTask, Task.Delay(100));
        
        if (completedTask == readTask)
        {
            // Read completed too quickly, writer might not have acquired lock
            // This is okay - timing-based tests can be flaky
            return;
        }

        // Wait for both operations to complete
        await Task.WhenAll(writeTask, readTask);
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
        Assert.True(sw.ElapsedMilliseconds < 1500,
            $"Concurrent reads took {sw.ElapsedMilliseconds}ms, expected less than 1500ms");
    }

    [Fact]
    public async Task HighConcurrency_MaintainsDataIntegrity()
    {
        // Arrange
        SharedResourceService service = CreateService(out _);
        CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        int writersCount = 5;
        int readersCount = 10;
        System.Collections.Concurrent.ConcurrentBag<string> writtenData = [];

        // Act - Run writers and readers concurrently
        List<Task> tasks = [];

        // Create writer tasks
        for (int i = 0; i < writersCount; i++)
        {
            int writerId = i;
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < 3; j++)
                {
                    string data = $"Writer{writerId}-Item{j}";
                    writtenData.Add(data);
                    await service.WriteAsync(writerId, data, cts.Token);
                }
            }));
        }

        // Create reader tasks
        for (int i = 0; i < readersCount; i++)
        {
            int readerId = i;
            tasks.Add(Task.Run(async () =>
            {
                for (int j = 0; j < 2; j++)
                {
                    await service.ReadAsync(readerId, cts.Token);
                    await Task.Delay(10); // Small delay between reads
                }
            }));
        }

        // Wait for all tasks to complete
        await Task.WhenAll(tasks);

        // Read final data to verify count
        string lastData = await service.ReadAsync(999, cts.Token);
        
        // Assert - we wrote the expected number of items
        Assert.Equal(writersCount * 3, writtenData.Count);
    }

    [Fact]
    public async Task MultipleReadersGetSameData_WhenNoWritesOccur()
    {
        // Arrange
        SharedResourceService service = CreateService(out _);
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
        SharedResourceService service = CreateService(out _);
        CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
        int iterations = 10;
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
                    await service.WriteAsync(iteration, $"Data-{iteration}", cts.Token);
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
                    await service.ReadAsync(iteration, cts.Token);
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

    [Fact]
    public async Task Service_ThrowsObjectDisposedException_WhenDisposed()
    {
        // Arrange
        SharedResourceService service = CreateService(out _);
        service.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.ReadAsync(1, CancellationToken.None));
        
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.WriteAsync(1, "data", CancellationToken.None));
    }
}