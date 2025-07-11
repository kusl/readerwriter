using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReaderWriter.Core;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ReaderWriter.ConsoleApp
{
    /// <summary>
    /// Configuration settings for the simulation.
    /// </summary>
    public class SimulationSettings
    {
        public int NumberOfReaders { get; set; }
        public int NumberOfWriters { get; set; }
        public int SimulationDurationSeconds { get; set; }
    }

    /// <summary>
    /// Main entry point for the application.
    /// </summary>
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    // Add command line configuration
                    config.AddCommandLine(args, new Dictionary<string, string>
                    {
                        { "--readers", "SimulationSettings:NumberOfReaders" },
                        { "-r", "SimulationSettings:NumberOfReaders" },
                        { "--writers", "SimulationSettings:NumberOfWriters" },
                        { "-w", "SimulationSettings:NumberOfWriters" },
                        { "--duration", "SimulationSettings:SimulationDurationSeconds" },
                        { "-d", "SimulationSettings:SimulationDurationSeconds" },
                        { "--loglevel", "Serilog:MinimumLevel" },
                        { "-l", "Serilog:MinimumLevel" }
                    });
                })
                .UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services))
                .ConfigureServices((context, services) =>
                {
                    // Register configuration
                    services.Configure<SimulationSettings>(
                        context.Configuration.GetSection("SimulationSettings"));

                    // Register shared resource service as singleton
                    services.AddSingleton<ISharedResourceService, SharedResourceService>();

                    // Register hosted service
                    services.AddHostedService<SimulationHost>();
                })
                .Build();

            await host.RunAsync();
        }
    }

    /// <summary>
    /// Hosted service that orchestrates the reader-writer simulation.
    /// </summary>
    public class SimulationHost : IHostedService
    {
        private readonly ILogger<SimulationHost> _logger;
        private readonly ISharedResourceService _sharedResourceService;
        private readonly SimulationSettings _settings;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private CancellationTokenSource? _cancellationTokenSource;

        public SimulationHost(
            ILogger<SimulationHost> logger,
            ISharedResourceService sharedResourceService,
            IOptions<SimulationSettings> settings,
            IHostApplicationLifetime applicationLifetime)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sharedResourceService = sharedResourceService ?? throw new ArgumentNullException(nameof(sharedResourceService));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _applicationLifetime = applicationLifetime ?? throw new ArgumentNullException(nameof(applicationLifetime));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Starting simulation with {Readers} readers, {Writers} writers for {Duration} seconds",
                _settings.NumberOfReaders,
                _settings.NumberOfWriters,
                _settings.SimulationDurationSeconds);

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(_settings.SimulationDurationSeconds));

            var tasks = new List<Task>();

            // Create writer tasks
            for (int i = 1; i <= _settings.NumberOfWriters; i++)
            {
                var writerId = i;
                tasks.Add(Task.Run(async () =>
                {
                    var random = new Random(writerId);
                    var writeCount = 0;

                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var data = $"Data from Writer {writerId} - Item {++writeCount} - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}";
                            await _sharedResourceService.WriteAsync(writerId, data, _cancellationTokenSource.Token);

                            // Wait before next write
                            await Task.Delay(random.Next(500, 2000), _cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected when cancellation is requested
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in writer {WriterId}", writerId);
                        }
                    }

                    _logger.LogInformation("Writer {WriterId} completed {WriteCount} writes", writerId, writeCount);
                }, _cancellationTokenSource.Token));
            }

            // Create reader tasks
            for (int i = 1; i <= _settings.NumberOfReaders; i++)
            {
                var readerId = i;
                tasks.Add(Task.Run(async () =>
                {
                    var random = new Random(readerId);
                    var readCount = 0;

                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var data = await _sharedResourceService.ReadAsync(readerId, _cancellationTokenSource.Token);
                            readCount++;

                            // Wait before next read
                            await Task.Delay(random.Next(100, 1000), _cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected when cancellation is requested
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in reader {ReaderId}", readerId);
                        }
                    }

                    _logger.LogInformation("Reader {ReaderId} completed {ReadCount} reads", readerId, readCount);
                }, _cancellationTokenSource.Token));
            }

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);

            _logger.LogInformation("Simulation completed successfully");

            // Stop the application after simulation completes
            _applicationLifetime.StopApplication();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping simulation");
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            return Task.CompletedTask;
        }
    }
}