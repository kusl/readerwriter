using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ReaderWriter.Core
{
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
    public class SharedResourceService : ISharedResourceService
    {
        private readonly ILogger<SharedResourceService> _logger;
        private static readonly ReaderWriterLockSlim _lock = new();
        private readonly List<string> _sharedData = new();
        private readonly Random _random = new();

        public SharedResourceService(ILogger<SharedResourceService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> ReadAsync(int readerId, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                _logger.LogInformation("Reader {ReaderId} attempting to acquire read lock", readerId);

                try
                {
                    _lock.EnterReadLock();
                    _logger.LogInformation("Reader {ReaderId} acquired read lock", readerId);

                    // Simulate variable read duration
                    var delay = _random.Next(100, 500);
                    Thread.Sleep(delay);

                    string data = _sharedData.Count > 0
                        ? _sharedData[_sharedData.Count - 1]
                        : "No data available";

                    _logger.LogInformation("Reader {ReaderId} read data: {Data}", readerId, data);

                    return data;
                }
                finally
                {
                    _lock.ExitReadLock();
                    _logger.LogInformation("Reader {ReaderId} released read lock", readerId);
                }
            }, cancellationToken);
        }

        public async Task WriteAsync(int writerId, string data, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                _logger.LogInformation("Writer {WriterId} attempting to acquire write lock", writerId);

                try
                {
                    _lock.EnterWriteLock();
                    _logger.LogInformation("Writer {WriterId} acquired write lock", writerId);

                    // Simulate variable write duration
                    var delay = _random.Next(200, 800);
                    Thread.Sleep(delay);

                    _sharedData.Add(data);

                    _logger.LogInformation("Writer {WriterId} wrote data: {Data}. Total items: {Count}",
                        writerId, data, _sharedData.Count);
                }
                finally
                {
                    _lock.ExitWriteLock();
                    _logger.LogInformation("Writer {WriterId} released write lock", writerId);
                }
            }, cancellationToken);
        }
    }
}