# FlacMigrator (Console Edition)

Safely migrate a FLAC music archive to MP3 (320 kbps) while preserving folder structure, metadata, and artwork, with verification and crash-safe resume.

## Features

- Recursively scans for .flac
- Preserves folder structure in the target
- Preserves metadata and cover art
- Verifies output (duration check plus decode test)
- Estimates total size and compression ratio
- Crash-safe resume with a state file
- Optional two-phase delete (only after verification)

## Requirements

- .NET 8 runtime or SDK
- ffmpeg and ffprobe on PATH

Tested with Windows, but the app is .NET 8 and cross-platform.

## Build

```powershell
dotnet build
```

## Run

```powershell
dotnet run --project FlacMigratorLite -- "<sourceDir>" "<targetDir>" [--delete] [--retry-failed]
dotnet run --project FlacMigratorLite -- "<sourceDir>" --in-place [--delete] [--retry-failed]
dotnet run --project FlacMigratorLite -- --source "<sourceDir>" --target "<targetDir>" [--delete] [--retry-failed]
dotnet run --project FlacMigratorLite -- --source "<sourceDir>" --in-place [--delete] [--retry-failed]
```

Example (positional):

```powershell
dotnet run --project FlacMigratorLite -- "C:\TestFlacConvert" "D:\TestFlacConvertMp3" --delete --retry-failed
```

Example (named flags):

```powershell
dotnet run --project FlacMigratorLite -- --source "C:\TestFlacConvert" --in-place --delete --retry-failed
```

### Flags

- `--source <dir>` source directory (can also be positional; first argument)
- `--target <dir>` target directory (can also be positional; second argument if not using `--in-place`)
- `--in-place` write MP3s next to FLACs and use the source directory as the target
- `--delete` delete verified FLAC files after the full pass
- `--retry-failed` reprocess tracks that previously failed

## How it works

1. Scan for .flac files
2. Probe duration with ffprobe
3. Estimate MP3 size
4. Confirm with the user
5. Convert with ffmpeg
6. Verify with duration check and decode test
7. Persist state after each track
8. Delete verified FLAC files if `--delete` is used

## Safety notes

- No in-place overwrites
- In-place mode writes MP3s next to FLACs and deletes FLACs only after verification
- No deletion before verification
- Uses a state file to resume after a crash
- Uses a temporary output file and then renames on success

## State file

A file named `migration_state.json` is written into the target directory to track progress. Delete it to start fresh.

## Troubleshooting

- ffmpeg errors about output format: ensure ffmpeg is on PATH and the target drive is writable.
- Run finishes immediately: use `--retry-failed` or remove the state file if a previous run recorded failures.

## License

MIT
