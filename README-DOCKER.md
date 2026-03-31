# Local Development with Docker

## Setup

### Prerequisites
- Docker and Docker Compose installed
- .NET 8 SDK

### Starting the Database

1. Copy the `.env.example` to `.env` (optional, but recommended for customization):
```bash
cp .env.example .env
```

2. Start the SQL Server container:
```bash
docker-compose up -d
```

This will start a SQL Server 2022 Express container with:
- **Host**: localhost
- **Port**: 1433
- **SA Password**: P@ssw0rd
- **Database**: MediaHandler (will be created by migrations)

### Verifying the Database Connection

You can verify the connection using sqlcmd:
```bash
docker exec -it <container_id> /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P P@ssw0rd -Q 'SELECT 1'
```

Or use any SQL client (SSMS, Azure Data Studio, etc.) with:
- Server: localhost,1433
- Authentication: SQL Server Authentication
- Login: sa
- Password: P@ssw0rd

### Running the API

1. Ensure the database container is running
2. Set the environment to Development:
```bash
export ASPNETCORE_ENVIRONMENT=Development
```

3. Run the API:
```bash
cd MediaHandler.API
dotnet run
```

The connection string from `appsettings.Development.json` will be automatically used, which connects to the Docker container.

### Stopping the Database

```bash
docker-compose down
```

### Removing All Data

```bash
docker-compose down -v
```

This will also remove the named volume, clearing all data.

## Configuration Files

- `appsettings.json` - Production/default settings
- `appsettings.Development.json` - Local development settings (uses Docker container)
- `docker-compose.yml` - Docker services configuration
- `.env.example` - Example environment variables

