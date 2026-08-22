# RoImport Bridge

A standalone Windows bridge for RoImport image uploads.

## Features

- Runs on `127.0.0.1:27123`
- Compatible `/health` and `/upload` routes
- No Node.js installation required for end users
- Dark Windows desktop interface
- RoImport application and tray icon
- Optional launch when Windows starts
- Separate startup background toggle
- Manual `Run in background` action
- Reopening the EXE brings the existing background instance back to the front
- Persistent upload history stored per user
- Upload log viewer with copyable details
- System tray Open and Exit actions
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

## GitHub repository layout

```text
RoImport-bridge/
├── .github/
│   └── workflows/
│       └── build.yml
├── RoImportBridge-application/
│   ├── RoImportBridge.csproj
│   └── application source files
├── README.md
└── Windows-bridge.bat
```

## Startup behavior

When startup is enabled, the app writes a per-user Windows startup entry. If `Start in the background` is enabled, the command includes `--background`. Otherwise the normal application window opens when the user signs in.

No administrator access is required.

## Upload logs

Successful uploads are stored at:

```text
%LOCALAPPDATA%\RoImportBridge\uploads.json
```

API keys and image data are never written to the upload log.
