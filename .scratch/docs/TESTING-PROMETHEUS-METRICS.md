# Testing Prometheus Metrics Implementation

## Implementation Summary

### Files Created/Modified

**Package References:**
- ✅ `src/Sonarr.Http/Sonarr.Http.csproj` - Added `prometheus-net.AspNetCore@8.2.1`
- ✅ `src/NzbDrone.Core/Sonarr.Core.csproj` - Added `prometheus-net@8.2.1`

**Endpoint Configuration:**
- ✅ `src/NzbDrone.Host/Startup.cs` - Added metrics endpoint at `/metrics`

**Core Metrics Infrastructure:**
- ✅ `src/NzbDrone.Core/Instrumentation/Metrics/IProvideMetrics.cs` - Interface for metric providers
- ✅ `src/NzbDrone.Core/Instrumentation/Metrics/UpdateMetricsCommand.cs` - Scheduled task command
- ✅ `src/NzbDrone.Core/Instrumentation/Metrics/UpdateMetricsService.cs` - Task executor

**Metric Collectors:**
- ✅ `src/NzbDrone.Core/Tv/SeriesMetricsCollector.cs` - Series domain metrics
- ✅ `src/NzbDrone.Core/Tv/EpisodeMetricsCollector.cs` - Episode domain metrics

**Task Registration:**
- ✅ `src/NzbDrone.Core/Jobs/TaskManager.cs` - Registered UpdateMetricsCommand (runs every 5 minutes)

## Build Instructions

### Prerequisites

From the CONTRIBUTING.md file:
- Visual Studio 2019+ OR Rider
- Node.js 20.X.X+
- Yarn
- .NET SDK 8.0.405 (see global.json)

### Build Steps

```bash
# 1. Restore NuGet packages (in Visual Studio or via CLI)
dotnet restore src/Sonarr.sln

# 2. Build frontend
yarn install
yarn start  # This runs webpack in watch mode

# 3. Build backend in Visual Studio:
#    - Open src/Sonarr.sln
#    - Set startup project to "Sonarr.Console"
#    - Set framework to x86
#    - Press F5 to build and debug

# OR build via CLI (if dotnet is available):
dotnet build src/Sonarr.sln --configuration Debug
```

## Running Sonarr Locally

### Start the Application

**Option A: Visual Studio**
1. Open `src/Sonarr.sln` in Visual Studio
2. Set `Sonarr.Console` as startup project
3. Press F5 to run
4. Application starts at `http://localhost:8989`

**Option B: Command Line** (after building):
```bash
# Navigate to output directory
cd _output

# Run Sonarr
./Sonarr.Console.exe  # Windows
# or
./Sonarr.Console      # Linux/Mac
```

### Access the Metrics Endpoint

Once Sonarr is running:

```bash
# Check metrics endpoint
curl http://localhost:8989/metrics

# Or open in browser
open http://localhost:8989/metrics
```

## Expected Metrics Output

You should see Prometheus format metrics like:

```prometheus
# HELP sonarr_series_added_total Total number of series added to Sonarr
# TYPE sonarr_series_added_total counter
sonarr_series_added_total 0

# HELP sonarr_series_deleted_total Total number of series deleted from Sonarr
# TYPE sonarr_series_deleted_total counter
sonarr_series_deleted_total 0

# HELP sonarr_series_total Current number of series in library
# TYPE sonarr_series_total gauge
sonarr_series_total{status="Continuing",monitored="True"} 5
sonarr_series_total{status="Ended",monitored="True"} 3
sonarr_series_total{status="Ended",monitored="False"} 1

# HELP sonarr_series_by_type Series count by type
# TYPE sonarr_series_by_type gauge
sonarr_series_by_type{type="Standard"} 8
sonarr_series_by_type{type="Anime"} 1

# HELP sonarr_episodes_imported_total Total number of episodes imported
# TYPE sonarr_episodes_imported_total counter
sonarr_episodes_imported_total{source="unknown"} 0

# HELP sonarr_episodes_deleted_total Total number of episode files deleted
# TYPE sonarr_episodes_deleted_total counter
sonarr_episodes_deleted_total 0

# HELP sonarr_episodes_missing_total Number of missing episodes (monitored, aired, no file)
# TYPE sonarr_episodes_missing_total gauge
sonarr_episodes_missing_total 42

# HELP sonarr_episodes_with_files_total Number of episodes with files
# TYPE sonarr_episodes_with_files_total gauge
sonarr_episodes_with_files_total 158

# HELP sonarr_episodes_total Total number of episodes
# TYPE sonarr_episodes_total gauge
sonarr_episodes_total{monitored="True"} 180
sonarr_episodes_total{monitored="False"} 20
```

## Testing Scenarios

### Test 1: Metrics Endpoint Availability

```bash
# Should return 200 OK with metrics in Prometheus format
curl -v http://localhost:8989/metrics

# Validate metrics format using promtool (if installed)
curl http://localhost:8989/metrics | promtool check metrics
```

**Expected**: HTTP 200, content-type `text/plain`, valid Prometheus format

### Test 2: Gauge Metrics Reflect Current State

```bash
# Get current metrics
curl http://localhost:8989/metrics | grep sonarr_series_total
```

Then in Sonarr UI:
1. Add a new series
2. Wait a few seconds for event to fire
3. Check metrics again

```bash
# Should show increased count
curl http://localhost:8989/metrics | grep sonarr_series_total
```

**Expected**: Series count should increase by 1

### Test 3: Counter Metrics Increment

```bash
# Get baseline
curl http://localhost:8989/metrics | grep sonarr_series_added_total
```

Add a series in Sonarr UI, then:

```bash
# Should show +1
curl http://localhost:8989/metrics | grep sonarr_series_added_total
```

**Expected**: Counter should increment and never decrease

### Test 4: Scheduled Task Runs

Check Sonarr logs for:

```
[Debug] Updating Prometheus metrics...
[Debug] Prometheus metrics updated successfully from 2 providers
```

**Expected**: Every 5 minutes, metrics are updated

### Test 5: Episode Import Tracking

Import an episode (via download or manual import), then:

```bash
curl http://localhost:8989/metrics | grep sonarr_episodes_imported_total
```

**Expected**: Counter should increment with appropriate source label

## Integration with Prometheus

### Prometheus Configuration

Add to your `prometheus.yml`:

```yaml
scrape_configs:
  - job_name: 'sonarr'
    static_configs:
      - targets: ['localhost:8989']
    metrics_path: '/metrics'
    scrape_interval: 30s
```

### Test Prometheus Scraping

```bash
# Start Prometheus with config
prometheus --config.file=prometheus.yml

# Check targets at http://localhost:9090/targets
# Sonarr should show as "UP"

# Query metrics in Prometheus UI
# Example queries:
# - sonarr_series_total
# - rate(sonarr_episodes_imported_total[5m])
# - sonarr_episodes_missing_total
```

## Grafana Dashboard

### Quick Test Dashboard

1. Add Prometheus as data source in Grafana
2. Create new dashboard
3. Add panels:

**Panel 1: Series Total**
```promql
sum(sonarr_series_total)
```

**Panel 2: Missing Episodes**
```promql
sonarr_episodes_missing_total
```

**Panel 3: Episode Import Rate**
```promql
rate(sonarr_episodes_imported_total[1h]) * 3600
```

**Panel 4: Episodes by Monitored Status**
```promql
sonarr_episodes_total
```

## Troubleshooting

### Metrics Endpoint Returns 404

**Check:**
1. Is Sonarr running? (`curl http://localhost:8989`)
2. Is the metrics endpoint registered? (Check Startup.cs)
3. Check Sonarr logs for startup errors

### No Metrics Shown

**Check:**
1. Are there any series/episodes in the database?
2. Check logs for "Failed to initialize ... metrics"
3. Verify DryIoc discovered the collectors (debug logs at startup)

### Metrics Don't Update

**Check:**
1. Are events firing? (Add series and check logs)
2. Is UpdateMetricsCommand scheduled? (Check System > Tasks in UI)
3. Check logs for "Failed to update metrics from..."

### Build Errors

**Common issues:**
- Missing NuGet packages: Run `dotnet restore`
- Prometheus namespace not found: Check .csproj has prometheus-net package
- Missing using statements: Add `using Prometheus;`

## Validation Checklist

- [ ] Sonarr builds successfully
- [ ] Sonarr starts without errors
- [ ] `/metrics` endpoint returns 200 OK
- [ ] Metrics are in valid Prometheus format
- [ ] Gauge metrics show current state (series/episode counts)
- [ ] Counter metrics increment when events occur
- [ ] Scheduled task runs every 5 minutes
- [ ] No errors in Sonarr logs related to metrics
- [ ] Prometheus can scrape the endpoint
- [ ] Grafana can display the metrics

## Next Steps

Once basic testing is complete:

1. **Add more collectors**:
   - QueueMetricsCollector
   - HealthCheckMetricsCollector
   - CommandMetricsCollector
   - DownloadMetricsCollector

2. **Add configuration**:
   - Enable/disable metrics endpoint
   - Configure update interval
   - Optional API key protection

3. **Add tests**:
   - Integration test for /metrics endpoint
   - Unit tests for metric collectors

4. **Documentation**:
   - Add to Sonarr wiki
   - Create example Grafana dashboards
   - Document all available metrics

## Performance Testing

Monitor Sonarr's performance with metrics enabled:

```bash
# Memory usage
ps aux | grep Sonarr

# CPU usage during metric updates
top -p $(pgrep Sonarr)

# Metric collection overhead
# Check logs for timing: "Prometheus metrics updated successfully" message
```

**Expected overhead**: <10ms per update, <5MB additional memory

## Support

If you encounter issues:

1. Check Sonarr logs in `~/.config/Sonarr/logs/`
2. Enable Debug logging in Sonarr UI
3. Verify all files were created/modified correctly
4. Check the implementation document: `.scratch/docs/features/prometheus-metrics.md`
