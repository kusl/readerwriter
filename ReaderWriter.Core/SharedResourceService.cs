using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ReaderWriter.Core;

/// <summary>
/// Defines the public contract for interacting with the shared resource.
/// </summary>
public interface ISharedResourceService
{
    /// <summary>
    /// Reads the last item from the shared resource.
    /// </summary>
    /// <param name="readerId">The unique identifier of the reader.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The data read from the resource, or a default value if empty.</returns>
    Task<string> ReadAsync(int readerId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes data to the shared resource.
    /// </summary>
    /// <param name="writerId">The unique identifier of the writer.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    Task WriteAsync(int writerId, string data, CancellationToken cancellationToken);
}

/// <summary>
/// Thread-safe implementation of ISharedResourceService using ReaderWriterLockSlim.
/// </summary>
public sealed class SharedResourceService : ISharedResourceService, IDisposable
{
    private readonly ILogger<SharedResourceService> _logger;
    private readonly ReaderWriterLockSlim _lock;
    private readonly List<string> _sharedData;
    private readonly Random _random;
    private readonly TimeSpan _lockTimeout;
    private readonly int _maxDataSize;
    private volatile bool _disposed;

    // Performance counters
    private long _totalReads;
    private long _totalWrites;
    private long _readTimeouts;
    private long _writeTimeouts;

    public SharedResourceService(ILogger<SharedResourceService> logger) 
        : this(logger, TimeSpan.FromSeconds(30), 10000)
    {
    }

    // Internal constructor for testing with configurable parameters
    internal SharedResourceService(
        ILogger<SharedResourceService> logger, 
        TimeSpan lockTimeout, 
        int maxDataSize)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        _sharedData = [];
        _random = new Random();
        _lockTimeout = lockTimeout;
        _maxDataSize = maxDataSize;
    }

    public async Task<string> ReadAsync(int readerId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        
        return await Task.Run(() =>
        {
            using Activity? activity = Activity.Current?.Source.StartActivity("ReadOperation");
            activity?.SetTag("reader.id", readerId);

            Stopwatch stopwatch = Stopwatch.StartNew();
            _logger.LogDebug("Reader {ReaderId} attempting to acquire read lock", readerId);
            
            if (!_lock.TryEnterReadLock(_lockTimeout))
            {
                Interlocked.Increment(ref _readTimeouts);
                _logger.LogWarning(
                    "Reader {ReaderId} failed to acquire read lock within {Timeout}ms", 
                    readerId, 
                    _lockTimeout.TotalMilliseconds);
                throw new TimeoutException($"Failed to acquire read lock within {_lockTimeout.TotalMilliseconds}ms");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                long lockAcquiredTime = stopwatch.ElapsedMilliseconds;
                _logger.LogDebug(
                    "Reader {ReaderId} acquired read lock after {ElapsedMs}ms. Current readers: {ReaderCount}", 
                    readerId, 
                    lockAcquiredTime,
                    _lock.CurrentReadCount);
                
                activity?.SetTag("lock.acquisition_time_ms", lockAcquiredTime);
                activity?.SetTag("lock.current_readers", _lock.CurrentReadCount);

                // Simulate variable read duration with cancellation support
                int delay = _random.Next(100, 500);
                Task delayTask = Task.Delay(delay, cancellationToken);
                delayTask.Wait(cancellationToken);

                string data = _sharedData.Count > 0
                    ? _sharedData[^1]
                    : "No data available";

                Interlocked.Increment(ref _totalReads);
                
                _logger.LogInformation(
                    "Reader {ReaderId} read data: {Data} (Length: {Length})", 
                    readerId, 
                    data.Length > 50 ? string.Concat(data.AsSpan(0, 47), "...") : data,
                    data.Length);
                
                activity?.SetTag("operation.success", true);
                activity?.SetTag("data.length", data.Length);
                
                return data;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Reader {ReaderId} operation was cancelled", readerId);
                activity?.SetTag("operation.cancelled", true);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reader {ReaderId} encountered an error", readerId);
                activity?.SetTag("operation.error", ex.Message);
                throw;
            }
            finally
            {
                _lock.ExitReadLock();
                long totalTime = stopwatch.ElapsedMilliseconds;
                _logger.LogDebug(
                    "Reader {ReaderId} released read lock. Total operation time: {ElapsedMs}ms", 
                    readerId, 
                    totalTime);
                activity?.SetTag("operation.duration_ms", totalTime);
            }
        }, cancellationToken);
    }

    public async Task WriteAsync(int writerId, string data, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        ArgumentNullException.ThrowIfNull(data);

        if (data.Length > _maxDataSize)
            throw new ArgumentException($"Data size {data.Length} exceeds maximum allowed size {_maxDataSize}", nameof(data));

        await Task.Run(() =>
        {
            using Activity? activity = Activity.Current?.Source.StartActivity("WriteOperation");
            activity?.SetTag("writer.id", writerId);
            activity?.SetTag("data.length", data.Length);

            Stopwatch stopwatch = Stopwatch.StartNew();
            _logger.LogDebug("Writer {WriterId} attempting to acquire write lock", writerId);

            if (!_lock.TryEnterWriteLock(_lockTimeout))
            {
                Interlocked.Increment(ref _writeTimeouts);
                _logger.LogWarning(
                    "Writer {WriterId} failed to acquire write lock within {Timeout}ms", 
                    writerId, 
                    _lockTimeout.TotalMilliseconds);
                throw new TimeoutException($"Failed to acquire write lock within {_lockTimeout.TotalMilliseconds}ms");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                long lockAcquiredTime = stopwatch.ElapsedMilliseconds;
                _logger.LogDebug(
                    "Writer {WriterId} acquired write lock after {ElapsedMs}ms", 
                    writerId, 
                    lockAcquiredTime);
                
                activity?.SetTag("lock.acquisition_time_ms", lockAcquiredTime);

                // Check if we need to trim old data to prevent unbounded growth
                if (_sharedData.Count >= _maxDataSize)
                {
                    int itemsToRemove = _sharedData.Count - _maxDataSize + 1;
                    _sharedData.RemoveRange(0, itemsToRemove);
                    _logger.LogDebug(
                        "Writer {WriterId} trimmed {Count} old items from shared data", 
                        writerId, 
                        itemsToRemove);
                }

                // Simulate variable write duration with cancellation support
                int delay = _random.Next(200, 800);
                Task delayTask = Task.Delay(delay, cancellationToken);
                delayTask.Wait(cancellationToken);

                _sharedData.Add(data);
                Interlocked.Increment(ref _totalWrites);

                _logger.LogInformation(
                    "Writer {WriterId} wrote data: {Data} (Length: {Length}). Total items: {Count}",
                    writerId, 
                    data.Length > 50 ? string.Concat(data.AsSpan(0, 47), "...") : data,
                    data.Length,
                    _sharedData.Count);
                
                activity?.SetTag("operation.success", true);
                activity?.SetTag("data.total_items", _sharedData.Count);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Writer {WriterId} operation was cancelled", writerId);
                activity?.SetTag("operation.cancelled", true);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Writer {WriterId} encountered an error", writerId);
                activity?.SetTag("operation.error", ex.Message);
                throw;
            }
            finally
            {
                _lock.ExitWriteLock();
                long totalTime = stopwatch.ElapsedMilliseconds;
                _logger.LogDebug(
                    "Writer {WriterId} released write lock. Total operation time: {ElapsedMs}ms", 
                    writerId, 
                    totalTime);
                activity?.SetTag("operation.duration_ms", totalTime);
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _lock?.Dispose();
            _logger.LogInformation(
                "SharedResourceService disposed. Total reads: {Reads}, Total writes: {Writes}, " +
                "Read timeouts: {ReadTimeouts}, Write timeouts: {WriteTimeouts}",
                _totalReads, _totalWrites, _readTimeouts, _writeTimeouts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disposal of SharedResourceService");
        }
    }    

    private void ThrowIfDisposed()
    {
        //if (_disposed)
        //    throw new ObjectDisposedException(nameof(SharedResourceService));
        ObjectDisposedException.ThrowIf(_disposed, nameof(SharedResourceService));
    }
}

/// <summary>
/// Extension methods for registering the service with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSharedResourceService(
        this IServiceCollection services,
        TimeSpan? lockTimeout = null,
        int? maxDataSize = null)
    {
        services.AddSingleton<ISharedResourceService>(provider =>
        {
            ILogger<SharedResourceService> logger = provider.GetRequiredService<ILogger<SharedResourceService>>();
            return new SharedResourceService(
                logger,
                lockTimeout ?? TimeSpan.FromSeconds(30),
                maxDataSize ?? 10000);
        });

        return services;
    }
}