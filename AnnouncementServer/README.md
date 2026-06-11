# SX3 Announcement Server

## Run

```powershell
$env:SX3_ANNOUNCEMENT_ADMIN_TOKEN = "replace-with-a-long-random-token"
dotnet run --project .\AnnouncementServer\SX3.AnnouncementServer.csproj
```

The server listens on port `5088` by default:

- WebSocket: `/ws/announcements`
- Current snapshot: `/api/announcements/current`
- Publish: `POST /api/announcements`
- Health: `/health`

Set `AnnouncementRealtimeUrl` in the scanner `App.config` to the server's
LAN or HTTPS address. Use `wss://` when the server is behind TLS.

## Publish

```powershell
.\BuildTools\Publish-Announcement.ps1 `
  -ServerUrl "http://server-ip:5088" `
  -Token $env:SX3_ANNOUNCEMENT_ADMIN_TOKEN
```
