![AssetForge](src/AssetForge.App/Assets/Brand/assetforge-logo.png)

# AssetForge

AssetForge is a local-first desktop tool for browsing, previewing, generating, and safely replacing assets in software and game projects. It uses .NET 10, Avalonia UI, MVVM, and a locally running [LocalAI](https://localai.io/) instance.

## First milestone

- Open a local project folder and browse supported assets in a filtered tree.
- Preview PNG, JPEG, and WebP images.
- Play, pause, and stop supported audio through a replaceable Windows audio service.
- Discover LocalAI models by capability without hardcoded model names.
- Generate named sound-effect or music drafts through `POST /v1/sound-generation`, then iterate with Retry before explicitly applying the chosen revision.
- Reopen a durable per-asset prompt/activity timeline and compare revisions stored under `.assetforge/revisions`.
- Apply, rename, recoverably delete, and persistently undo asset operations without silently overwriting project files.
- Preview generated audio and replace an existing audio asset after creating a timestamped backup.
- Refresh automatically when project assets change outside AssetForge.

TTS, image generation, recent-project persistence, installers, and non-Windows validation are planned for later milestones. When image generation is added, published images will use the selected project's `img` folder.

## Requirements

- Windows 10 or later
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [LocalAI](https://localai.io/) at `http://localhost:8080`
- A LocalAI model advertising the `sound_generation` capability

AssetForge does not require LocalAI to launch. Generation controls remain unavailable while LocalAI is offline or lacks a compatible model.

## Run

```powershell
dotnet restore AssetForge.slnx --configfile NuGet.Config
dotnet run --project src/AssetForge.App/AssetForge.App.csproj
```

Override the LocalAI URL with an environment variable:

```powershell
$env:ASSETFORGE_LocalAI__BaseUrl = 'http://localhost:8080'
dotnet run --project src/AssetForge.App/AssetForge.App.csproj
```

## Test

```powershell
dotnet test AssetForge.slnx -c Release
```

## Supported assets

- Images: `.png`, `.jpg`, `.jpeg`, `.webp`
- Audio: `.wav`, `.mp3`, `.ogg`, `.flac`, `.opus`

Audio is classified as speech or music when its folder path contains a matching context term; other supported audio is treated as a sound effect. Actual playback support depends on the codecs available through NAudio on Windows.

## Iterative workspace and safe replacement

Each selected asset has a persistent AssetForge workspace. Generate and Retry create private revisions under:

```text
.assetforge/revisions/<asset-id>/
```

Only **Apply revision** publishes the chosen draft into the project's `sounds` folder or replaces an existing project asset. Delete moves assets into `.assetforge/trash`, while project operation history supports Undo after restarting AssetForge.

AssetForge only replaces a selected project asset after validating its general media type. Before replacement it copies the original to:

```text
.assetforge/backups/<UTC timestamp>/<original relative path>
```

The backup area is excluded from the asset browser. If backup creation or staging fails, the original is not replaced.

## Architecture

- `AssetForge.App` — Avalonia views, controls, MVVM view models, configuration, and hosting.
- `AssetForge.Core` — media models, generation requests, service contracts, and classification rules.
- `AssetForge.Infrastructure` — filesystem scanning/watching, safe replacement, LocalAI HTTP integration, and NAudio playback.
- `AssetForge.Tests` — unit and mocked HTTP integration tests.

LocalAI-specific wire formats and NAudio types stay behind interfaces so other providers and playback engines can be added without changing the UI workflow.

## License

[MIT](LICENSE)
