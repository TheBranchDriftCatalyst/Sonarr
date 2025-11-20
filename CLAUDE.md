# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Sonarr is a PVR (Personal Video Recorder) for Usenet and BitTorrent users. It monitors RSS feeds for new TV show episodes and can automatically download, sort, and rename them. The project is a full-stack application with a C# .NET backend and a React/TypeScript frontend.

**Tech Stack:**
- Backend: C# .NET 8.0
- Frontend: React 18 + TypeScript, Redux, webpack
- Database: SQLite with Dapper ORM
- API: RESTful API (v3 and v5)
- Build: Visual Studio / MSBuild for C#, webpack for frontend

**Main Branch:** `v5-develop` (not `main`)

## Development Setup

### Prerequisites

- .NET SDK 8.0.405 (see global.json)
- Node.js 20.11.1+ (specified via Volta)
- Yarn 1.22.22
- Visual Studio 2019+ (Community Edition works) OR Rider

### Initial Setup

```bash
# Install frontend dependencies
yarn install

# Start webpack in watch mode (monitors frontend changes)
yarn start

# Build the backend in Visual Studio:
# - Set startup project to Sonarr.Console
# - Set framework to x86
# - Press F5 to debug

# Application runs at http://localhost:8989
```

## Common Commands

### Frontend Development

```bash
# Start webpack in watch mode (required for frontend development)
yarn start          # or: yarn watch

# Build frontend for production
yarn build

# Lint and fix TypeScript/JavaScript
yarn lint
yarn lint-fix

# Lint CSS
yarn stylelint

# Clean build artifacts
yarn clean          # Removes _output/UI and .js.map files
```

### Backend Development

Build the solution in Visual Studio or using the .NET CLI:

```bash
# Note: dotnet CLI may not be available in all environments
# Prefer using Visual Studio for building the backend

# If dotnet is available:
dotnet build src/Sonarr.sln
dotnet run --project src/NzbDrone.Console/Sonarr.Console.csproj
```

### Testing

**Backend tests** are organized by type:
- Unit tests: `*.Test.csproj` (e.g., Sonarr.Core.Test, Sonarr.Api.Test)
- Integration tests: `Sonarr.Integration.Test`
- Automation tests: `Sonarr.Automation.Test`

Run tests using the test script:

```bash
# Run unit tests
./scripts/test.sh <Platform> Unit Test
# Platform: Windows, Linux, or Mac

# Run integration tests
./scripts/test.sh <Platform> Integration Test

# Run with coverage
./scripts/test.sh <Platform> Unit Coverage
```

**Frontend tests** use the NUnit test runner via dotnet test. Individual test assemblies can be tested in Visual Studio's Test Explorer.

## Code Architecture

### Backend Structure

The C# backend follows a layered architecture with dependency injection:

**Core Projects:**
- `NzbDrone.Core` (Sonarr.Core.csproj): Business logic, domain models, and services
  - Domain entities: `Tv/Series.cs`, `Tv/Episode.cs`
  - Services: Organized by feature (Indexers, Download, MediaFiles, Parser, etc.)
  - Data access: Repository pattern using `BasicRepository<TModel>`
  - Uses Dapper for data access with SQLite

- `Sonarr.Http`: Shared HTTP infrastructure, base controllers, REST utilities

- `Sonarr.Api.V3`: V3 REST API controllers (legacy, still in use)
  - Controllers inherit from `RestControllerWithSignalR<TResource, TModel>`
  - Example: `SeriesController` in `Sonarr.Api.V3/Series/`

- `Sonarr.Api.V5`: V5 REST API controllers (newer endpoints being migrated)
  - Uses OpenAPI/Swagger spec (see `openapi.json`)

- `NzbDrone.Console` (Sonarr.Console.csproj): Application entry point

- `NzbDrone.Host` (Sonarr.Host.csproj): Web server hosting, startup configuration

- `NzbDrone.Common` (Sonarr.Common.csproj): Shared utilities and helpers

**Data Access Pattern:**
- All models inherit from `ModelBase` (which has `Id` property)
- Repositories inherit from `BasicRepository<TModel>` in `NzbDrone.Core/Datastore/`
- Uses Dapper with manual SQL queries, not an ORM like Entity Framework
- Events are published via `IEventAggregator` for cross-cutting concerns

**Dependency Injection:**
- The project does NOT use TinyIoC or Autofac modules in the traditional sense
- Dependencies are constructor-injected
- Service registration happens in the Host project

### Frontend Structure

The frontend is a React/Redux SPA written in TypeScript:

**Key Directories in `frontend/src/`:**
- `App/`: Application shell, routing, main layout
- `Store/`: Redux store configuration, actions, and reducers
  - `Actions/`: Redux actions (see `Store/Actions/index.js` for exports)
  - Organized by domain: series, episodes, settings, etc.
- `Series/`: Series listing, details, and management UI
- `Episode/`: Episode views and components
- `Calendar/`: Calendar view for upcoming episodes
- `Activity/`: Queue and history views
- `Settings/`: Application settings UI
- `Components/`: Reusable UI components
- `Utilities/`: Helper functions and utilities

**State Management:**
- Redux for global state
- Actions use `redux-actions` library
- Connected to React Router via `connected-react-router`

**Build Process:**
- webpack bundles TypeScript/React into `_output/UI/`
- Uses Babel for transpilation
- CSS Modules with PostCSS for styling
- Development mode: `yarn start` runs webpack in watch mode with LiveReload
- Production mode: `yarn build` creates optimized bundles

### API Communication

The frontend communicates with the backend via REST API:
- Base API endpoints defined in the API projects (V3 and V5)
- V3 API is the primary API currently in use
- V5 API is being gradually introduced (react-query migration in progress)
- Controllers use SignalR for real-time updates (see `@microsoft/signalr` package)

### Migration to V5 API and react-query

Recent commits show ongoing migration:
- New V5 endpoints being added (see commits: "Add v5 episode, missing and cutoff unmet endpoints", "Add v5 tag endpoints")
- Frontend migrating from Redux to react-query (`@tanstack/react-query`)
- When adding new features, prefer V5 API patterns and react-query

## Development Workflow

### Adding a New API Endpoint

1. **Backend:**
   - Create controller in `src/Sonarr.Api.V5/` (for new endpoints)
   - Inherit from appropriate base controller in `Sonarr.Http`
   - Create resource model (DTO) for API serialization
   - Wire up service dependencies via constructor injection
   - Add validation using FluentValidation

2. **Frontend:**
   - For V5 endpoints, use react-query hooks
   - Define API client functions
   - Create query/mutation hooks
   - Update UI components to use the new hooks

### Code Style

**C#:**
- 4 spaces for indentation (Visual Studio default)
- StyleCop analyzers enforce code style
- FluentValidation for input validation
- NLog for logging

**TypeScript:**
- ESLint configuration in `frontend/.eslintrc.js`
- Prettier for code formatting
- 4 spaces for indentation
- Use TypeScript strict mode

### Running a Single Test

**Backend:**
Visual Studio Test Explorer allows running individual tests. Alternatively:

```bash
# Run specific test DLL
dotnet test _tests/Sonarr.Core.Test.dll --filter "FullyQualifiedName~NameOfYourTest"
```

**Note:** The test script (`scripts/test.sh`) runs all tests in a category, not individual tests.

## Pull Request Guidelines

- Target branch: `v5-develop` (NOT `main`)
- Rebase from `v5-develop`, don't merge
- One feature/bug fix per PR
- Include tests (unit/integration)
- Use meaningful commit messages
- Use feature branches with descriptive names (e.g., `feature/add-custom-format`, `fix/calendar-timezone`)
- Commit with *nix line endings (CRLF → LF)

## Key Files

- `src/Sonarr.sln`: Main solution file
- `package.json`: Frontend dependencies and scripts
- `frontend/build/webpack.config.js`: Frontend build configuration
- `global.json`: .NET SDK version specification
- `src/Directory.Build.props`: Shared MSBuild properties
- `scripts/test.sh`: Test execution script
