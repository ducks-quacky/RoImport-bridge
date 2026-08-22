# RoImport Bridge

A standalone Windows bridge for RoImport image uploads.

## Features

- Runs on `127.0.0.1:27123`
- `/health` and `/upload` routes compatible with the original bridge
- No Node.js installation required for end users
- Optional launch at Windows sign-in
- Startup launches silently with `--background`
- System tray icon while running
- Single-instance protection
- Single-file Windows executable

## Build locally

Install the .NET 8 SDK, then run:

```powershell
dotnet publish RoImportBridge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The executable will be created at:

```text
publish\RoImportBridge.exe
```

## GitHub build

The included GitHub Actions workflow builds the executable when you manually run the workflow or push a tag such as `v1.0.0`.

Download the resulting `RoImportBridge-win-x64` artifact from the workflow run.

## Startup behavior

When a user enables `Run RoImport Bridge when Windows starts`, the app adds this command to the current user's Windows startup registry entry:

```text
"C:\path\to\RoImportBridge.exe" --background
```

This does not require administrator access.

## API

### GET /health

Returns bridge health and version information.

### POST /upload

Accepts the same JSON payload as the original local Node.js bridge.
