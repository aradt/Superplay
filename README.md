# Superplay Game Server

A .NET 8.0 WebSocket game server with extensible message routing, dual storage support (SQLite/Redis), an interactive console client, and structured Serilog logging.

## Quick Start

```bash
# Build
dotnet build Superplay.sln

# Run server (SQLite by default — no external dependencies)
dotnet run --project src/Superplay.Server

# Run client (in a separate terminal)
dotnet run --project src/Superplay.Client

# Run tests
dotnet test
```

## Configuration

All configuration is managed via `appsettings.json` files. Environment-specific overrides are supported via `appsettings.{ENVIRONMENT}.json` (e.g., `appsettings.Development.json`).

### Server Configuration

**File:** `src/Superplay.Server/appsettings.json`

```json
{
  "Storage": {
    "Provider": "Sqlite"
  },
  "Sqlite": {
    "ConnectionString": "Data Source=superplay.db"
  },
  "Redis": {
    "ConnectionString": "localhost:6379"
  },
  "Serilog": { ... },
  "Urls": "http://localhost:5000"
}
```

#### Storage Provider

| Setting | Description |
|---------|-------------|
| `Storage:Provider` | `"Sqlite"` (default) or `"Redis"` |

**SQLite (default)** — zero external dependencies. The database file is created automatically on first run.

| Setting | Default | Description |
|---------|---------|-------------|
| `Sqlite:ConnectionString` | `Data Source=superplay.db` | Path to the SQLite database file |

**Redis** — requires a running Redis instance. Use `docker-compose up -d` to start one locally.

| Setting | Default | Description |
|---------|---------|-------------|
| `Redis:ConnectionString` | `localhost:6379` | Redis server address and port |

#### Server URL

| Setting | Default | Description |
|---------|---------|-------------|
| `Urls` | `http://localhost:5000` | HTTP address the server listens on. The WebSocket endpoint is at `/ws` |

#### Logging (Serilog)

| Setting | Default | Description |
|---------|---------|-------------|
| `Serilog:MinimumLevel:Default` | `Information` | Minimum log level |
| `Serilog:MinimumLevel:Override:Microsoft` | `Warning` | Suppress noisy ASP.NET Core logs |
| `Serilog:WriteTo` | Console | Log sinks (Console by default, can add File) |

To add file logging, add an entry to the `WriteTo` array:

```json
{
  "Serilog": {
    "WriteTo": [
      { "Name": "Console", "Args": { ... } },
      { "Name": "File", "Args": { "path": "logs/server-.log", "rollingInterval": "Day" } }
    ]
  }
}
```

### Client Configuration

**File:** `src/Superplay.Client/appsettings.json`

```json
{
  "Server": {
    "Url": "ws://localhost:5000/ws"
  },
  "Serilog": { ... }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `Server:Url` | `ws://localhost:5000/ws` | WebSocket URL of the game server |
| `Serilog:MinimumLevel:Default` | `Debug` | Client logs at Debug level by default |

The server URL can also be passed as a command-line argument (highest priority):

```bash
dotnet run --project src/Superplay.Client -- ws://other-host:5000/ws
```

### Environment Overrides

Both server and client support environment-specific config files:

```
appsettings.json                  # Base config (committed to repo)
appsettings.Development.json      # Dev overrides (optional, gitignored)
appsettings.Production.json       # Production overrides (optional)
```

The environment is determined by the `DOTNET_ENVIRONMENT` variable (server) or `ASPNETCORE_ENVIRONMENT`.

### Default Values

All fallback defaults are defined in `src/Superplay.Shared/Defaults.cs`:

| Constant | Value | Used When |
|----------|-------|-----------|
| `StorageProvider` | `"Sqlite"` | No `Storage:Provider` in config |
| `SqliteConnectionString` | `"Data Source=superplay.db"` | No `Sqlite:ConnectionString` in config |
| `RedisConnectionString` | `"localhost:6379"` | No `Redis:ConnectionString` in config |
| `ServerUrl` | `"http://localhost:5000"` | Server base URL reference |
| `WebSocketEndpoint` | `"/ws"` | WebSocket endpoint path reference |
| `ClientWebSocketUrl` | `"ws://localhost:5000/ws"` | No `Server:Url` in client config |

## Switching to Redis

1. Start Redis:
   ```bash
   docker-compose up -d
   ```

2. Update `src/Superplay.Server/appsettings.json`:
   ```json
   {
     "Storage": {
       "Provider": "Redis"
     }
   }
   ```

3. Restart the server.

## Project Structure

```
Superplay/
├── Superplay.sln
├── docker-compose.yml              # Redis container
├── src/
│   ├── Superplay.Shared/           # Shared DTOs, enums, defaults
│   ├── Superplay.Server/           # WebSocket game server
│   │   ├── Handlers/               # Login, UpdateResources, SendGift
│   │   ├── Networking/             # WebSocket handler, connection manager
│   │   ├── Routing/                # Extensible message routing (IMessageHandler)
│   │   └── Services/               # IPlayerRepository, SQLite & Redis implementations
│   ├── Superplay.Client/           # Interactive console client
│   └── Superplay.Tests/            # xUnit tests (79 tests)
```
