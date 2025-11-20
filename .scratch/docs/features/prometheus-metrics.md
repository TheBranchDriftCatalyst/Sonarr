# Prometheus Metrics for Sonarr

## Overview

This document provides a comprehensive Prometheus metrics implementation for Sonarr, covering:
1. Endpoint implementation using `prometheus-net` library
2. Comprehensive metric definitions derived from core data models, DTOs, events, and domain entities
3. DRY implementation patterns leveraging Sonarr's automatic dependency injection

The goal is to provide observability into Sonarr's operation using industry-standard Prometheus metric types while maintaining DRY principles.

## 🔍 Architecture Analysis & Dry Run Results

After analyzing Sonarr's actual codebase (`src/NzbDrone.Host/Bootstrap.cs`, `Startup.cs`, `EventAggregator.cs`), key architectural discoveries revealed a **much simpler** implementation than originally planned:

### Key Discoveries

#### 1. DI Container: DryIoc (NOT TinyIoC/Autofac)
- **Location**: `src/NzbDrone.Host/Bootstrap.cs:155`
- **Auto-registration**: `c.AutoAddServices(Bootstrap.ASSEMBLIES)` at line 162
- **Pattern**: All interfaces/classes in specified assemblies are automatically registered
  - Interfaces → Singleton reuse
  - Classes → Transient reuse
- **No manual registration needed!**

#### 2. Event System: Automatic Discovery
- **Location**: `src/NzbDrone.Core/Messaging/Events/EventAggregator.cs:29-37`
- **How it works**:
  ```csharp
  // EventAggregator automatically calls BuildAll<IHandle<TEvent>>()
  _syncHandlers = serviceFactory.BuildAll<IHandle<TEvent>>().ToArray();
  ```
- **Result**: Just implement `IHandle<TEvent>` and EventAggregator automatically calls your handler!
- **Performance**: Handlers discovered once at startup, cached forever

#### 3. ASP.NET Core Middleware Pipeline
- **Location**: `src/NzbDrone.Host/Startup.cs:301-339`
- **Standard pattern**: Easy to add via `app.UseEndpoints()` or `app.UseMiddleware()`

### Implementation Simplification

| Aspect | Original Plan | Actual Implementation |
|--------|--------------|----------------------|
| **DI Registration** | Manual registration in container | ✅ Automatic via `AutoAddServices` |
| **Event Subscription** | Manual subscription in constructor | ✅ Automatic via `IHandle<T>` |
| **Collector Pattern** | Central monolithic collector | ✅ Domain-specific collectors |
| **Assembly Setup** | Add new assembly to Bootstrap | ✅ Use existing assemblies |
| **Configuration** | Complex setup required | ✅ Zero configuration needed |

### The Transparent Pattern

```csharp
// File: src/NzbDrone.Core/Tv/SeriesMetricsCollector.cs
// NO registration, NO subscription, NO configuration needed!

namespace NzbDrone.Core.Tv
{
    public class SeriesMetricsCollector :
        IProvideMetrics,              // For scheduled updates
        IHandle<SeriesAddedEvent>     // Automatically called!
    {
        private readonly ISeriesService _seriesService;

        private static readonly Counter AddedCounter =
            Metrics.CreateCounter("sonarr_series_added_total", "Series added");

        // Constructor injection - DryIoc handles this
        public SeriesMetricsCollector(ISeriesService seriesService)
        {
            _seriesService = seriesService;
        }

        // EventAggregator automatically calls this on SeriesAddedEvent!
        public void Handle(SeriesAddedEvent message)
        {
            AddedCounter.Inc();
        }

        // Background task calls this periodically
        public void UpdateGauges()
        {
            var allSeries = _seriesService.GetAllSeries();
            // update gauges...
        }
    }
}
```

### Recommended File Organization

**Option A: Co-located with domain** (recommended for discoverability):
```
src/NzbDrone.Core/
├── Instrumentation/Metrics/
│   ├── IProvideMetrics.cs         (interface only)
│   ├── UpdateMetricsCommand.cs    (scheduled task)
│   └── UpdateMetricsService.cs    (executor)
├── Tv/
│   ├── Series.cs
│   ├── SeriesService.cs
│   ├── SeriesMetricsCollector.cs  ← Lives with domain code
│   └── EpisodeMetricsCollector.cs
├── Queue/
│   ├── Queue.cs
│   ├── QueueService.cs
│   └── QueueMetricsCollector.cs   ← Lives with domain code
└── HealthCheck/
    ├── HealthCheck.cs
    └── HealthMetricsCollector.cs  ← Lives with domain code
```

**Option B: Centralized metrics directory**:
```
src/NzbDrone.Core/Instrumentation/Metrics/
├── IProvideMetrics.cs
├── UpdateMetricsCommand.cs
├── UpdateMetricsService.cs
├── SeriesMetricsCollector.cs     (namespace: NzbDrone.Core.Tv)
├── EpisodeMetricsCollector.cs    (namespace: NzbDrone.Core.Tv)
├── QueueMetricsCollector.cs      (namespace: NzbDrone.Core.Queue)
└── HealthMetricsCollector.cs     (namespace: NzbDrone.Core.HealthCheck)
```

### Performance Characteristics (Validated)

Based on architecture analysis:
- **Event handler discovery**: Once at startup via `BuildAll<IHandle<T>>()`
- **Event dispatch**: O(1) dictionary lookup, then direct method calls
- **Metric operations**: ~50-200ns per operation (prometheus-net benchmarks)
- **Memory overhead**: Static metrics, minimal allocations
- **Background updates**: Configurable interval (default: 5 minutes)

## Endpoint Implementation

### Library Selection: prometheus-net

Use [prometheus-net](https://github.com/prometheus-net/prometheus-net) - the official and most widely-used .NET Prometheus library.

**Why prometheus-net?**
- ✅ Actively maintained (regular updates in 2025)
- ✅ Most popular C# Prometheus library (~6.5k+ stars)
- ✅ Built-in ASP.NET Core integration
- ✅ Supports all Prometheus metric types (Counter, Gauge, Histogram, Summary)
- ✅ Used by major .NET projects in production

**Alternatives considered:**
- ❌ **App.Metrics**: Now deprecated/no longer maintained
- ⚠️ **OpenTelemetry**: Good alternative if you need multi-backend support (Prometheus + Application Insights), but adds complexity

**Package**: `prometheus-net.AspNetCore`

```bash
# Add to Sonarr.Http project (for endpoint and HTTP metrics)
dotnet add src/Sonarr.Http/Sonarr.Http.csproj package prometheus-net.AspNetCore --version 8.2.1

# Add base package to Core (for domain metric collection)
dotnet add src/NzbDrone.Core/Sonarr.Core.csproj package prometheus-net --version 8.2.1
```

**Note**: No need to modify `Bootstrap.ASSEMBLIES` - prometheus-net types will be available in existing assemblies.

### Endpoint Setup

#### 1. Modify Startup.cs

Edit `src/NzbDrone.Host/Startup.cs` in the `Configure` method:

**Option A: Add as middleware (before routing)**:

```csharp
public void Configure(IApplicationBuilder app, ...)
{
    // ... existing code (line ~300)

    app.UseForwardedHeaders();
    app.UseMiddleware<LoggingMiddleware>();

    // ADD THIS: Prometheus HTTP metrics (tracks all requests)
    app.UseHttpMetrics(options =>
    {
        options.ReduceStatusCodeCardinality(); // Groups 2xx, 3xx, 4xx, 5xx
    });

    app.UsePathBase(new PathString(configFileProvider.UrlBase));
    // ... rest of middleware
}
```

**Option B: Add as endpoint (in UseEndpoints block)** (RECOMMENDED):

```csharp
public void Configure(IApplicationBuilder app, ...)
{
    // ... existing code down to line ~335

    app.UseEndpoints(x =>
    {
        x.MapHub<MessageHub>("/signalr/messages").RequireAuthorization("SignalR");

        // ADD THIS: Prometheus metrics endpoint
        x.MapMetrics("/metrics");  // Exposes at http://localhost:8989/metrics

        x.MapControllers();
    });
}
```

**Note**: Both can be used together - Option A tracks HTTP metrics, Option B exposes the `/metrics` endpoint.

### Endpoint Configuration

The metrics endpoint will be available at: `http://localhost:8989/metrics`

**Configuration Options** (via `config.xml` or environment variables):

```xml
<Config>
  <PrometheusEnabled>true</PrometheusEnabled>
  <PrometheusPort>8989</PrometheusPort>  <!-- Use existing port -->
  <PrometheusPath>/metrics</PrometheusPath>
</Config>
```

### Security Considerations

Add authentication middleware for the metrics endpoint:

```csharp
app.MapWhen(
    context => context.Request.Path.StartsWithSegments("/metrics"),
    appMetrics =>
    {
        // Optional: Add API key authentication
        appMetrics.Use(async (context, next) =>
        {
            var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (IsValidApiKey(apiKey))
            {
                await next();
            }
            else
            {
                context.Response.StatusCode = 401;
            }
        });

        appMetrics.UseMetricServer("");
    }
);
```

### Scrape Configuration

Example Prometheus scrape config:

```yaml
scrape_configs:
  - job_name: 'sonarr'
    static_configs:
      - targets: ['localhost:8989']
    metrics_path: '/metrics'
    # Optional: Add API key auth
    # authorization:
    #   credentials: 'your-sonarr-api-key'
```

## Metric Collection Architecture

### Sonarr's Automatic Event Discovery

**KEY INSIGHT**: Sonarr's `EventAggregator` (built on DryIoc) **automatically discovers** all classes implementing `IHandle<TEvent>`. No manual subscription needed!

**How it works** (`src/NzbDrone.Core/Messaging/Events/EventAggregator.cs:29-37`):
1. When `PublishEvent<TEvent>` is called, EventAggregator uses `IServiceFactory.BuildAll<IHandle<TEvent>>()`
2. DryIoc returns ALL registered implementations of that interface
3. Handlers are called automatically (sync first, then async)

**This means**: Just implement `IHandle<TEvent>` and you're done!

### Transparent Metrics Collection Pattern

Create domain-specific metric collectors that are automatically discovered:

#### 1. Define Metrics Provider Interface

```csharp
namespace NzbDrone.Core.Instrumentation.Metrics
{
    /// <summary>
    /// Implement this interface to provide metrics that should be updated periodically
    /// All implementations are automatically discovered and called by UpdateMetricsService
    /// </summary>
    public interface IProvideMetrics
    {
        void UpdateGauges();
    }
}
```

#### 2. Implement Domain-Specific Collectors

**Example: Series Metrics** (`src/NzbDrone.Core/Tv/SeriesMetricsCollector.cs`):

```csharp
using Prometheus;
using NzbDrone.Core.Instrumentation.Metrics;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;
using System.Linq;

namespace NzbDrone.Core.Tv
{
    /// <summary>
    /// This class is AUTOMATICALLY discovered and registered by DryIoc!
    /// Event handlers are AUTOMATICALLY called by EventAggregator!
    /// </summary>
    public class SeriesMetricsCollector : IProvideMetrics,
        IHandle<SeriesAddedEvent>,
        IHandle<SeriesDeletedEvent>,
        IHandle<SeriesUpdatedEvent>
    {
        private readonly ISeriesService _seriesService;

        // Define metrics as static fields (created once, reused forever)
        private static readonly Counter AddedCounter = Metrics.CreateCounter(
            "sonarr_series_added_total",
            "Total number of series added to Sonarr"
        );

        private static readonly Counter DeletedCounter = Metrics.CreateCounter(
            "sonarr_series_deleted_total",
            "Total number of series deleted from Sonarr"
        );

        private static readonly Gauge TotalGauge = Metrics.CreateGauge(
            "sonarr_series_total",
            "Current number of series in library",
            new GaugeConfiguration { LabelNames = new[] { "status", "monitored" } }
        );

        // Constructor injection - DryIoc handles this automatically
        public SeriesMetricsCollector(ISeriesService seriesService)
        {
            _seriesService = seriesService;

            // Initialize gauges on startup
            UpdateGauges();
        }

        // Event handlers - AUTOMATICALLY called by EventAggregator!
        public void Handle(SeriesAddedEvent message)
        {
            AddedCounter.Inc();
            UpdateGauges(); // Update current state
        }

        public void Handle(SeriesDeletedEvent message)
        {
            DeletedCounter.Inc();
            UpdateGauges();
        }

        public void Handle(SeriesUpdatedEvent message)
        {
            // Only update if status/monitoring changed
            // Skip updates to avoid unnecessary DB queries
        }

        // Called by background task periodically
        public void UpdateGauges()
        {
            var allSeries = _seriesService.GetAllSeries();
            var grouped = allSeries.GroupBy(s => new { s.Status, s.Monitored });

            foreach (var group in grouped)
            {
                TotalGauge
                    .WithLabels(group.Key.Status.ToString(), group.Key.Monitored.ToString())
                    .Set(group.Count());
            }
        }
    }
}
```

**Example: Episode Metrics** (`src/NzbDrone.Core/Tv/EpisodeMetricsCollector.cs`):

```csharp
namespace NzbDrone.Core.Tv
{
    public class EpisodeMetricsCollector : IProvideMetrics,
        IHandle<EpisodeImportedEvent>,
        IHandle<EpisodeFileDeletedEvent>
    {
        private readonly IEpisodeService _episodeService;

        private static readonly Counter ImportedCounter = Metrics.CreateCounter(
            "sonarr_episodes_imported_total",
            "Total episodes imported",
            new CounterConfiguration { LabelNames = new[] { "source" } }
        );

        private static readonly Gauge MissingGauge = Metrics.CreateGauge(
            "sonarr_episodes_missing_total",
            "Number of missing episodes (monitored, aired, no file)"
        );

        private static readonly Gauge WithFilesGauge = Metrics.CreateGauge(
            "sonarr_episodes_with_files_total",
            "Number of episodes with files"
        );

        public EpisodeMetricsCollector(IEpisodeService episodeService)
        {
            _episodeService = episodeService;
            UpdateGauges();
        }

        public void Handle(EpisodeImportedEvent message)
        {
            var source = message.ImportedEpisode.ImportMode?.ToString() ?? "unknown";
            ImportedCounter.WithLabels(source).Inc();
            UpdateGauges();
        }

        public void Handle(EpisodeFileDeletedEvent message)
        {
            UpdateGauges();
        }

        public void UpdateGauges()
        {
            var allEpisodes = _episodeService.GetAllEpisodes();

            var withFiles = allEpisodes.Count(e => e.HasFile);
            var missing = allEpisodes.Count(e =>
                e.Monitored &&
                e.AirDateUtc.HasValue &&
                e.AirDateUtc.Value < DateTime.UtcNow &&
                !e.HasFile);

            WithFilesGauge.Set(withFiles);
            MissingGauge.Set(missing);
        }
    }
}
```

### Advantages of This Architecture

1. **Zero Manual Wiring**: DryIoc discovers everything automatically
2. **Truly Transparent**: Just implement interfaces, metrics appear
3. **Domain-Organized**: Each domain (TV, Queue, Health) has its own collector
4. **Testable**: Can mock services and test metric logic
5. **Low Overhead**: Event handlers are discovered once at startup
6. **Type-Safe**: Compiler ensures event types match

### Background Metrics Updater

For expensive calculations (e.g., storage metrics, statistics), use a scheduled task that updates all gauge metrics periodically:

```csharp
namespace NzbDrone.Core.Instrumentation.Metrics
{
    /// <summary>
    /// Command to trigger metrics update
    /// </summary>
    public class UpdateMetricsCommand : Command
    {
        public override bool SendUpdatesToClient => false;
        public override bool IsLongRunning => false;
    }

    /// <summary>
    /// Scheduled task that updates all metric providers
    /// DryIoc automatically injects ALL implementations of IProvideMetrics!
    /// </summary>
    public class UpdateMetricsService : IExecute<UpdateMetricsCommand>
    {
        private readonly IEnumerable<IProvideMetrics> _metricsProviders;
        private readonly Logger _logger;

        // DryIoc will automatically inject ALL IProvideMetrics implementations here!
        public UpdateMetricsService(IEnumerable<IProvideMetrics> metricsProviders, Logger logger)
        {
            _metricsProviders = metricsProviders;
            _logger = logger;
        }

        public void Execute(UpdateMetricsCommand message)
        {
            _logger.Debug("Updating Prometheus metrics...");

            foreach (var provider in _metricsProviders)
            {
                try
                {
                    provider.UpdateGauges();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to update metrics from {0}", provider.GetType().Name);
                }
            }

            _logger.Debug("Prometheus metrics updated successfully");
        }
    }
}
```

**Register in TaskManager** (`src/NzbDrone.Core/Jobs/TaskManager.cs`):

Add to the `GetDefinitions()` method:

```csharp
private List<ScheduledTask> GetDefinitions()
{
    return new List<ScheduledTask>
    {
        // ... existing tasks

        // ADD THIS: Update Prometheus metrics every 5 minutes
        new ScheduledTask
        {
            Interval = 300, // 5 minutes in seconds
            TypeName = typeof(UpdateMetricsCommand).FullName
        }
    };
}
```

## Metric Naming Convention

All metrics follow the Prometheus naming convention:
- Prefix: `sonarr_`
- Snake case format
- Suffixes: `_total` (counters), `_bytes` (sizes), `_seconds` (durations)

## Core Domain Metrics

### Series Metrics

**Gauges** (current state):
```prometheus
# Total number of series in the library
sonarr_series_total{status="continuing|ended|upcoming|deleted"}

# Monitored vs unmonitored series
sonarr_series_monitored{monitored="true|false"}

# Series by type
sonarr_series_by_type{type="standard|anime|daily"}

# Series by quality profile
sonarr_series_by_quality_profile{profile_name="HD-1080p|Ultra-HD|..."}

# Series by network
sonarr_series_by_network{network="HBO|Netflix|..."}
```

**Counters** (cumulative events):
```prometheus
# Series additions over time
sonarr_series_added_total

# Series deletions over time
sonarr_series_deleted_total

# Series updates/refreshes
sonarr_series_refreshed_total
```

### Episode Metrics

**Gauges**:
```prometheus
# Total episodes by status
sonarr_episodes_total{has_file="true|false",monitored="true|false"}

# Episodes by series
sonarr_episodes_by_series{series_id="123",series_title="Breaking Bad"}

# Episodes awaiting download (monitored, no file, aired)
sonarr_episodes_missing_total

# Episodes with files
sonarr_episodes_with_files_total

# Episodes by season
sonarr_episodes_by_season{series_id="123",season_number="1"}

# Episode file count per quality
sonarr_episode_files_by_quality{quality="HDTV-1080p|Bluray-1080p|..."}

# Unmonitored episodes
sonarr_episodes_unmonitored_total
```

**Counters**:
```prometheus
# Episode imports over time
sonarr_episodes_imported_total{source="download_folder|series_folder"}

# Episode searches performed
sonarr_episodes_searched_total{search_type="automatic|manual|interactive"}

# Episode file deletions
sonarr_episodes_deleted_total{reason="upgrade|missing_from_disk|manual|..."}

# Episode file renames
sonarr_episodes_renamed_total
```

### Storage Metrics

**Gauges**:
```prometheus
# Total disk space used by episode files (bytes)
sonarr_storage_used_bytes{series_id="123",series_title="Breaking Bad"}

# Total disk space by quality profile
sonarr_storage_by_quality_bytes{quality="HDTV-1080p|Bluray-1080p|..."}

# Root folder free space
sonarr_root_folder_free_bytes{path="/media/tv"}

# Root folder total space
sonarr_root_folder_total_bytes{path="/media/tv"}

# Average episode file size by quality
sonarr_episode_file_average_size_bytes{quality="HDTV-1080p|..."}
```

## Download & Queue Metrics

### Queue Metrics

**Gauges**:
```prometheus
# Current queue size
sonarr_queue_total{status="downloading|queued|paused|warning|failed"}

# Queue items by protocol
sonarr_queue_by_protocol{protocol="usenet|torrent"}

# Queue items by download client
sonarr_queue_by_client{client_name="SABnzbd|qBittorrent|..."}

# Estimated time remaining (seconds)
sonarr_queue_time_remaining_seconds{download_id="abc123"}

# Queue size remaining (bytes)
sonarr_queue_size_remaining_bytes

# Total queue size (bytes)
sonarr_queue_size_total_bytes
```

**Counters**:
```prometheus
# Downloads grabbed
sonarr_downloads_grabbed_total{protocol="usenet|torrent",indexer="NZBGeek|..."}

# Downloads completed successfully
sonarr_downloads_completed_total{client="SABnzbd|qBittorrent|..."}

# Downloads failed
sonarr_downloads_failed_total{client="SABnzbd|...",reason="timeout|..."}

# Downloads ignored
sonarr_downloads_ignored_total{reason="quality|..."}
```

### History Metrics

**Counters** (based on EpisodeHistoryEventType):
```prometheus
# History events by type
sonarr_history_events_total{event_type="grabbed|downloaded|failed|deleted|renamed|ignored"}

# History events by series
sonarr_history_by_series_total{series_id="123",event_type="grabbed|..."}

# Download failures by reason
sonarr_download_failures_total{reason="timeout|bad_content|..."}

# Import events by source
sonarr_imports_total{source="download_folder|series_folder"}
```

**Gauges**:
```prometheus
# Last successful import timestamp
sonarr_last_import_timestamp_seconds

# Last download timestamp
sonarr_last_download_timestamp_seconds
```

## Indexer & Download Client Metrics

### Indexer Metrics

**Gauges**:
```prometheus
# Indexer availability status
sonarr_indexer_status{indexer="NZBGeek|...",status="available|unavailable"}

# Indexer response time
sonarr_indexer_response_time_seconds{indexer="NZBGeek|..."}

# Indexers enabled vs disabled
sonarr_indexers_enabled{enabled="true|false"}
```

**Counters**:
```prometheus
# Indexer queries
sonarr_indexer_queries_total{indexer="NZBGeek|...",query_type="rss|search"}

# Indexer failures
sonarr_indexer_failures_total{indexer="NZBGeek|...",error_type="timeout|..."}

# Indexer results returned
sonarr_indexer_results_total{indexer="NZBGeek|..."}

# Indexer grabs
sonarr_indexer_grabs_total{indexer="NZBGeek|..."}
```

### Download Client Metrics

**Gauges**:
```prometheus
# Download client availability
sonarr_download_client_status{client="SABnzbd|...",status="available|unavailable"}

# Active download clients
sonarr_download_clients_active_total

# Download client queue size
sonarr_download_client_queue_size{client="SABnzbd|..."}
```

**Counters**:
```prometheus
# Downloads sent to client
sonarr_download_client_downloads_sent_total{client="SABnzbd|..."}

# Communication errors with client
sonarr_download_client_errors_total{client="SABnzbd|...",error_type="connection|..."}
```

## Command & Task Metrics

### Command Metrics (based on CommandModel)

**Gauges**:
```prometheus
# Active commands
sonarr_commands_active{command_name="SeriesSearch|RssSync|..."}

# Queued commands
sonarr_commands_queued{command_name="SeriesSearch|...",priority="high|normal|low"}

# Command queue depth by priority
sonarr_command_queue_depth{priority="high|normal|low"}
```

**Counters**:
```prometheus
# Commands executed
sonarr_commands_executed_total{command_name="SeriesSearch|...",result="completed|failed|aborted"}

# Command failures
sonarr_commands_failed_total{command_name="SeriesSearch|..."}
```

**Histograms**:
```prometheus
# Command execution duration
sonarr_command_duration_seconds{command_name="SeriesSearch|..."}
# Buckets: 1, 5, 10, 30, 60, 300, 600, 1800, 3600
```

### Scheduled Task Metrics (based on ScheduledTask)

**Gauges**:
```prometheus
# Last execution timestamp for each task
sonarr_task_last_execution_timestamp_seconds{task_name="RssSync|RefreshSeries|..."}

# Task interval (seconds)
sonarr_task_interval_seconds{task_name="RssSync|..."}

# Time since last task execution
sonarr_task_time_since_last_run_seconds{task_name="RssSync|..."}
```

**Counters**:
```prometheus
# Task executions
sonarr_task_executions_total{task_name="RssSync|...",result="success|failure"}
```

## Health Check Metrics

### Health Status (based on HealthCheck model)

**Gauges**:
```prometheus
# Overall health status (0=ok, 1=notice, 2=warning, 3=error)
sonarr_health_status{check_name="IndexerCheck|DownloadClientCheck|..."}

# Health checks by result type
sonarr_health_checks_by_type{type="ok|notice|warning|error"}

# Number of health issues
sonarr_health_issues_total{severity="notice|warning|error"}
```

**Info Metric** (labels only):
```prometheus
# Health check metadata
sonarr_health_info{check_name="...",message="...",reason="..."}
```

**Counters**:
```prometheus
# Health check failures
sonarr_health_failures_total{reason="DownloadClientCheckNoneAvailable|..."}

# Health checks restored
sonarr_health_restored_total{reason="..."}
```

## Application Metrics

### System Metrics

**Gauges**:
```prometheus
# Sonarr version info
sonarr_version_info{version="4.0.0",branch="v5-develop"}

# Uptime
sonarr_uptime_seconds

# Database size
sonarr_database_size_bytes{database="sonarr.db"}

# Configuration changes
sonarr_config_version
```

**Counters**:
```prometheus
# Application starts
sonarr_app_starts_total

# Application crashes/restarts
sonarr_app_crashes_total

# API requests
sonarr_api_requests_total{endpoint="/api/v3/series",method="GET|POST|...",status="200|404|..."}

# SignalR events broadcast
sonarr_signalr_events_total{event_type="SeriesUpdated|EpisodeFileAdded|..."}
```

**Histograms**:
```prometheus
# API request duration
sonarr_api_request_duration_seconds{endpoint="/api/v3/series",method="GET"}
# Buckets: 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0, 5.0
```

### Event Metrics

All domain events should emit metrics:

**Counters** (examples):
```prometheus
# Series events
sonarr_events_total{event="series_added|series_deleted|series_updated|series_edited|series_bulk_edited|series_moved"}

# Episode events
sonarr_events_total{event="episode_imported|episode_deleted|episode_file_renamed"}

# Media cover events
sonarr_events_total{event="media_covers_updated"}

# Custom format events
sonarr_events_total{event="custom_format_added|custom_format_deleted"}

# Tag events
sonarr_events_total{event="tags_updated|auto_tags_updated"}

# Config events
sonarr_events_total{event="config_saved|config_file_saved"}

# Update events
sonarr_events_total{event="update_installed"}
```

## Quality & Language Metrics

**Gauges**:
```prometheus
# Files by language
sonarr_episode_files_by_language{language="English|Japanese|..."}

# Quality profiles defined
sonarr_quality_profiles_total

# Custom formats defined
sonarr_custom_formats_total

# Files matching custom format
sonarr_files_by_custom_format{format_name="..."}
```

## Statistics & Aggregates

**Gauges** (derived from SeriesStatistics):
```prometheus
# Episodes per series statistics
sonarr_series_episode_count{series_id="123",type="total|available|monitored|with_files"}

# Season statistics
sonarr_season_episode_count{series_id="123",season="1",type="total|available|with_files"}

# Next airing episode timestamp
sonarr_series_next_airing_timestamp_seconds{series_id="123"}

# Previous aired episode timestamp
sonarr_series_previous_airing_timestamp_seconds{series_id="123"}

# Last aired date
sonarr_series_last_aired_timestamp_seconds{series_id="123"}
```

## Import List Metrics

**Gauges**:
```prometheus
# Import lists configured
sonarr_import_lists_total{enabled="true|false"}

# Import list status
sonarr_import_list_status{list_name="Trakt|...",status="available|unavailable"}

# Import list items
sonarr_import_list_items_total{list_name="Trakt|..."}
```

**Counters**:
```prometheus
# Import list syncs
sonarr_import_list_syncs_total{list_name="Trakt|...",result="success|failure"}

# Series added from import lists
sonarr_import_list_series_added_total{list_name="Trakt|..."}
```

## Notification Metrics

**Counters**:
```prometheus
# Notifications sent
sonarr_notifications_sent_total{notification="Discord|Slack|...",event="Download|Upgrade|..."}

# Notification failures
sonarr_notifications_failed_total{notification="Discord|...",error="connection|..."}
```

**Gauges**:
```prometheus
# Notification status
sonarr_notification_status{notification="Discord|...",status="available|unavailable"}
```

## Implementation Strategy

### DRY Principles

1. **Generic Repository Metrics**: Create a generic metric collector for all `BasicRepository<TModel>` operations
   - Track: inserts, updates, deletes, queries
   - Labels: `{operation="insert|update|delete|get", model="Series|Episode|..."}`

2. **Event-Driven Metrics**: Subscribe to `IEventAggregator` and automatically emit metrics for all `IHandle<TEvent>` events
   - Pattern: `sonarr_events_total{event="<EventTypeName>"}`

3. **Command Metrics Middleware**: Wrap `CommandExecutor` to automatically track all command execution metrics
   - Duration histograms
   - Success/failure counters
   - Queue depth gauges

4. **API Middleware**: Add middleware to track all API requests automatically
   - No manual instrumentation in controllers needed

5. **Health Check Observer**: Subscribe to health check events to maintain current health state

### Cardinality Considerations

**High Cardinality Labels to Avoid**:
- Individual download IDs (use aggregates instead)
- Full file paths
- Specific timestamps in labels (use values instead)
- User-provided text fields

**Cardinality-Safe Approaches**:
- Use `series_id` but limit exposure in dashboards
- Aggregate by category (quality, network, status) rather than individual items
- Use histograms for distributions rather than individual gauges

### Metric Collection Points

1. **Repository Layer**: Intercept all database operations
2. **Service Layer**: Track domain logic operations
3. **Event Handlers**: Subscribe to all domain events
4. **API Controllers**: Middleware for request/response metrics
5. **Background Jobs**: Task and command execution tracking
6. **Health Checks**: Periodic health status collection

## Example Implementation Pattern

```csharp
public class PrometheusMetricsCollector : IHandle<SeriesAddedEvent>, IHandle<EpisodeImportedEvent>
{
    private static readonly Counter SeriesAddedCounter = Metrics.CreateCounter(
        "sonarr_series_added_total",
        "Total number of series added to Sonarr"
    );

    private static readonly Gauge SeriesTotal = Metrics.CreateGauge(
        "sonarr_series_total",
        "Current number of series in library",
        new GaugeConfiguration { LabelNames = new[] { "status", "monitored" } }
    );

    public void Handle(SeriesAddedEvent message)
    {
        SeriesAddedCounter.Inc();
        // Update gauge by querying repository
        UpdateSeriesGauges();
    }

    // ... more handlers
}
```

## Dashboard & Alerting Examples

### Useful Prometheus Queries

```promql
# Series added per day
rate(sonarr_series_added_total[24h]) * 86400

# Episode import rate
rate(sonarr_episodes_imported_total[5m]) * 300

# Download success rate
rate(sonarr_downloads_completed_total[1h]) / rate(sonarr_downloads_grabbed_total[1h])

# Average command execution time
rate(sonarr_command_duration_seconds_sum[5m]) / rate(sonarr_command_duration_seconds_count[5m])

# Health issues by severity
sum by (severity) (sonarr_health_issues_total)

# Storage growth rate
deriv(sonarr_storage_used_bytes[1d])

# Queue processing rate
rate(sonarr_queue_total[5m])
```

### Alert Rules

```yaml
groups:
  - name: sonarr_alerts
    rules:
      - alert: SonarrDownloadFailureRate
        expr: rate(sonarr_downloads_failed_total[1h]) > 0.1
        annotations:
          summary: "High download failure rate"

      - alert: SonarrHealthCritical
        expr: sonarr_health_issues_total{severity="error"} > 0
        annotations:
          summary: "Critical health check failures detected"

      - alert: SonarrIndexerDown
        expr: sonarr_indexer_status{status="unavailable"} == 1
        for: 5m
        annotations:
          summary: "Indexer {{ $labels.indexer }} is unavailable"

      - alert: SonarrQueueStalled
        expr: changes(sonarr_queue_total[1h]) == 0 AND sonarr_queue_total > 0
        for: 30m
        annotations:
          summary: "Download queue appears stalled"
```

## Testing the Metrics Endpoint

### Manual Testing

Once implemented, test the endpoint:

```bash
# Check if metrics endpoint is available
curl http://localhost:8989/metrics

# Expected output (sample):
# HELP sonarr_series_total Current number of series in library
# TYPE sonarr_series_total gauge
# sonarr_series_total{status="continuing",monitored="true"} 42
# sonarr_series_total{status="ended",monitored="true"} 15
# ...
```

### Validate Metrics Format

Use Prometheus's promtool to validate:

```bash
# Install promtool
brew install prometheus  # macOS
# or download from https://prometheus.io/download/

# Validate metrics endpoint
curl http://localhost:8989/metrics | promtool check metrics
```

### Integration Testing

Create a test in `src/NzbDrone.Integration.Test`:

```csharp
[Test]
public void should_expose_metrics_endpoint()
{
    var request = new RestRequest("metrics");
    var response = _restClient.Get(request);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    response.ContentType.Should().Contain("text/plain");
    response.Content.Should().Contain("sonarr_series_total");
}

[Test]
public void should_track_series_metrics()
{
    // Add a series
    var series = EnsureSeries(1, "Breaking Bad", true);

    // Check metrics
    var metricsResponse = _restClient.Get(new RestRequest("metrics"));
    metricsResponse.Content.Should().Contain("sonarr_series_total");
    metricsResponse.Content.Should().Contain("sonarr_series_added_total");
}
```

## Migration & Rollout Strategy

### Phase 1: Infrastructure Setup (Week 1)
1. Add prometheus-net packages
2. Implement basic `/metrics` endpoint
3. Add automatic HTTP metrics middleware
4. Deploy to development environment
5. Configure Prometheus to scrape dev instance

### Phase 2: Core Metrics (Week 2-3)
1. Implement Series metrics (gauges + counters)
2. Implement Episode metrics
3. Implement Queue/Download metrics
4. Add event-based metric updates
5. Test in development

### Phase 3: Extended Metrics (Week 4)
1. Add Command/Task metrics with histograms
2. Implement Health Check metrics
3. Add Indexer/Download Client metrics
4. Create background metrics updater task

### Phase 4: Optimization & Production (Week 5-6)
1. Performance testing (ensure <10ms overhead)
2. Cardinality analysis (target <1000 unique time series)
3. Documentation and Grafana dashboards
4. Beta release with metrics enabled by default
5. Monitor production rollout

## Performance Considerations

### Metric Collection Overhead

prometheus-net is designed to be extremely low-overhead:
- Counters: ~50ns per increment
- Gauges: ~100ns per set
- Histograms: ~200ns per observation

**Best Practices:**
1. Use static metric instances (defined once)
2. Avoid creating metrics in hot paths
3. Use background tasks for expensive calculations (storage, statistics)
4. Batch gauge updates when possible

### Cardinality Management

Keep total time series count reasonable (<10,000):

```csharp
// ❌ BAD: High cardinality (could be thousands of series)
episodeGauge.WithLabels(series.Title, episode.Title).Set(1);

// ✅ GOOD: Low cardinality
episodeGauge.WithLabels(series.Id.ToString()).Set(episodeCount);

// ✅ BETTER: Even lower cardinality
episodesByStatus.WithLabels(hasFile ? "true" : "false").Set(count);
```

## Grafana Dashboard Examples

### Essential Sonarr Dashboard

Create a dashboard with these panels:

**Row 1: Overview**
- Series Total (gauge)
- Episodes Missing (gauge)
- Queue Size (gauge)
- Download Rate (graph)

**Row 2: Activity**
- Episode Imports (graph): `rate(sonarr_episodes_imported_total[5m])*300`
- Downloads (graph): `rate(sonarr_downloads_grabbed_total[1h])`
- Command Execution Times (heatmap): `sonarr_command_duration_seconds`

**Row 3: Health & Performance**
- Health Issues by Severity (stat)
- Indexer Status (stat)
- API Response Times (graph)
- Database Size (gauge)

**Row 4: Storage**
- Total Storage Used (gauge)
- Storage by Quality (pie chart)
- Storage Growth Rate (graph): `deriv(sonarr_storage_used_bytes[1d])`

### JSON Export Example

```json
{
  "dashboard": {
    "title": "Sonarr Monitoring",
    "panels": [
      {
        "title": "Series Total",
        "targets": [
          {
            "expr": "sum(sonarr_series_total)",
            "legendFormat": "Total Series"
          }
        ]
      },
      {
        "title": "Download Success Rate",
        "targets": [
          {
            "expr": "rate(sonarr_downloads_completed_total[1h]) / rate(sonarr_downloads_grabbed_total[1h]) * 100",
            "legendFormat": "Success Rate %"
          }
        ]
      }
    ]
  }
}
```

## Troubleshooting

### Metrics Not Appearing

1. Check endpoint accessibility: `curl http://localhost:8989/metrics`
2. Verify prometheus-net is registered in DI container
3. Check logs for metric collection errors
4. Ensure `UseMetricServer()` is called in startup

### High Memory Usage

1. Check metric cardinality: `curl localhost:8989/metrics | grep -c "sonarr_"`
2. Review label combinations (series_id, episode_id may be too granular)
3. Consider using background updater with longer intervals
4. Use `_total` suffixes for counters, not gauges

### Stale Metrics

1. Verify background updater task is scheduled
2. Check event subscriptions are working
3. Look for exceptions in metric collection code
4. Ensure database queries are returning current data

## Security Considerations

### API Key Protection

```csharp
// Option 1: Require API key (recommended)
app.MapWhen(
    context => context.Request.Path.StartsWithSegments("/metrics"),
    metricsApp =>
    {
        metricsApp.Use(async (context, next) =>
        {
            var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
            if (string.IsNullOrEmpty(apiKey) || !_configService.GetApiKey().Equals(apiKey))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }
            await next();
        });
        metricsApp.UseMetricServer("");
    }
);
```

### Network Restrictions

```yaml
# Prometheus config with authentication
scrape_configs:
  - job_name: 'sonarr'
    static_configs:
      - targets: ['localhost:8989']
    metrics_path: '/metrics'
    authorization:
      credentials: 'your-sonarr-api-key'
    # Optional: TLS
    # scheme: https
    # tls_config:
    #   insecure_skip_verify: false
```

### Sensitive Data Filtering

```csharp
// Ensure no sensitive data in labels
private static string SanitizeLabel(string input)
{
    // Remove API keys, tokens, passwords from labels
    if (input.Contains("apikey", StringComparison.OrdinalIgnoreCase))
        return "[REDACTED]";
    return input;
}
```

## Configuration Options

Add to `src/NzbDrone.Core/Configuration/ConfigService.cs`:

```csharp
public interface IConfigService
{
    bool EnablePrometheusMetrics { get; }
    string PrometheusMetricsPath { get; }
    bool RequireApiKeyForMetrics { get; }
    int MetricsUpdateIntervalSeconds { get; }
}

// Defaults:
// EnablePrometheusMetrics = true
// PrometheusMetricsPath = "/metrics"
// RequireApiKeyForMetrics = false
// MetricsUpdateIntervalSeconds = 300 (5 minutes)
```

## Next Steps

1. **Phase 1**: Implement basic endpoint and HTTP metrics (1-2 days)
2. **Phase 2**: Add core domain metrics (Series, Episodes, Queue) (3-5 days)
3. **Phase 3**: Event-driven metrics collection (2-3 days)
4. **Phase 4**: Background metrics updater task (1-2 days)
5. **Phase 5**: Testing, documentation, Grafana dashboards (2-3 days)

**Total estimated effort**: 2-3 weeks

## References

- [prometheus-net Documentation](https://github.com/prometheus-net/prometheus-net)
- [Prometheus Best Practices](https://prometheus.io/docs/practices/naming/)
- [Prometheus Exposition Formats](https://prometheus.io/docs/instrumenting/exposition_formats/)
- [Grafana Prometheus Data Source](https://grafana.com/docs/grafana/latest/datasources/prometheus/)
- Sonarr Core Models: `src/NzbDrone.Core/`
- Sonarr Events: `src/NzbDrone.Core/*/Events/`
