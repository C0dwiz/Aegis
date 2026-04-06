# Aegis.API

Standalone API documentation service for Aegis protocol.

## Purpose

`Aegis.API` exposes protocol docs as HTTP endpoints with OpenAPI/Swagger:

- Message type catalog
- Per-message structure reference
- Protocol exchange examples
- Error/status code reference
- v1 -> v2 migration guide

## Run

```bash
dotnet run --project src/Aegis.API/Aegis.API.csproj
```

## Endpoints

- `GET /swagger`
- `GET /health`
- `GET /api/protocol/message-types`
- `GET /api/protocol/messages/{messageType}`
- `GET /api/protocol/exchanges`
- `GET /api/protocol/errors`
- `GET /api/protocol/migration/v1-to-v2`
