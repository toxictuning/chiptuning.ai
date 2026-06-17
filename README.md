# Chiptuning.Ai .NET Client

Official .NET SDK for the [Chiptuning.Ai](https://chiptuning.ai) API.  
Upload ECU files, generate patches, and integrate chiptuning into your application in minutes.

## Installation

```bash
dotnet add package ChiptuningAi.Client
```

Targets **.NET 6**, **.NET 8**, and **.NET Standard 2.1**.

---

## Quick start

```csharp
using ChiptuningAi.Client;
using ChiptuningAi.Client.Files;

// 1. Create the client
var client = new ChiptuningAiClient();

// 2. Login
await client.Auth.LoginAsync("you@workshop.com", "your-password");

// 3. Upload an ECU file
var file = await client.Files.UploadAsync("C:/ecu/bmw_320i_stock.bin", new FileMetadata
{
    VehicleClass   = "Passenger Car",
    VehicleMake    = "BMW",
    VehicleModel   = "3 Series",
    VehicleVariant = "320i",
    EngineType     = "Diesel",
    ECUType        = "ECU",
    ECUMake        = "Bosch",
    ECUModel       = "EDC17C64",
    ReadHardware   = "CMD Flash",
    ReadMode       = "OBD",
    PowerOutput    = 184,
    TorqueOutput   = 380,
});

Console.WriteLine($"Uploaded: {file.FileId}");

// 4. Upload a tuned version as a patch
var patch = await client.Patches.UploadAsync(
    modifiedFilePath: "C:/ecu/bmw_320i_stage1.bin",
    parentFileId:     file.FileId,
    description:      "Stage 1 — +40 hp / +80 Nm",
    version:          "v1.0");

Console.WriteLine($"Patch created: {patch.PatchId}");

// 5. Apply the patch to another compatible file
var result = await client.Patches.ApplyAsync(patch.PatchId, sourceFileId: file.FileId);
Console.WriteLine($"Patched file ID: {result.ResultFileId}");
```

---

## Authentication

### Login

```csharp
await client.Auth.LoginAsync("you@workshop.com", "password");
```

Tokens are stored automatically — you do not need to manage headers yourself.

### Persist tokens between sessions

```csharp
// After login, save the tokens
var profile = await client.Auth.GetProfileAsync();
// store accessToken and refreshToken somewhere secure

// Next session — restore without logging in again
var client = ChiptuningAiClient.FromToken(accessToken, refreshToken);
```

### Token refresh

Tokens are refreshed automatically when a request returns `401 Unauthorized`.  
You do not need to handle this yourself.

---

## File uploads

### Single file (≤ 20 MB)

```csharp
var file = await client.Files.UploadAsync("path/to/ecu.bin", metadata);
```

### Large file (> 20 MB) — chunked upload with progress

The SDK detects file size automatically and switches to chunked upload.  
Pass an `IProgress<int>` to receive percentage updates:

```csharp
var progress = new Progress<int>(pct => Console.WriteLine($"{pct}%"));

var file = await client.Files.UploadAsync(
    "path/to/large_ecu.bin",
    metadata,
    progress: progress);
```

### List files

```csharp
var page = await client.Files.ListAsync(ecuMake: "Bosch", ecuModel: "EDC17C64");

foreach (var f in page.Items)
    Console.WriteLine($"{f.FileName} — {f.VehicleMake} {f.VehicleVariant}");
```

### Find similar files

```csharp
var matches = await client.Files.FindSimilarAsync(
    "path/to/unknown.bin", ecuMake: "Bosch", ecuModel: "EDC17C64");

foreach (var m in matches)
    Console.WriteLine($"{m.FileName} — {m.Similarity:P0} similar");
```

### Delete a file

```csharp
await client.Files.DeleteAsync(fileId);
// File is hidden immediately and permanently deleted after 30 days.
```

---

## Patches

### Upload a patch

```csharp
var patch = await client.Patches.UploadAsync(
    modifiedFilePath: "tuned.bin",
    parentFileId:     originalFileId,
    description:      "Stage 2 — EGR off + DPF off",
    version:          "v2.0");
```

### Apply a patch

```csharp
try
{
    var result = await client.Patches.ApplyAsync(patchId, sourceFileId);
    Console.WriteLine($"Done — output file: {result.ResultFileId}");
}
catch (DailyLimitExceededException)
{
    Console.WriteLine("Daily quota reached. Upgrade your plan or try again tomorrow.");
}
```

### List patches for a file

```csharp
var patches = await client.Patches.ListAsync(parentFileId);

foreach (var p in patches.Items)
    Console.WriteLine($"{p.Version} — {p.Description}");
```

### Application history

```csharp
var history = await client.Patches.GetHistoryAsync(fileId);

foreach (var entry in history.Items)
    Console.WriteLine($"Applied {entry.PatchId} at {entry.AppliedAt:u}");
```

### Delete a patch

```csharp
await client.Patches.DeleteAsync(patchId);
```

---

## Error handling

All API errors throw `ApiException`. Check `ErrorCode` for the machine-readable reason:

```csharp
using ChiptuningAi.Client.Common;

try
{
    var file = await client.Files.GetAsync(fileId);
}
catch (DailyLimitExceededException)
{
    // Patch quota exhausted — specific subclass for easy handling
}
catch (ApiException ex) when (ex.ErrorCode == "FILE_NOT_FOUND")
{
    Console.WriteLine("File does not exist or was deleted.");
}
catch (ApiException ex)
{
    Console.WriteLine($"API error {ex.StatusCode}: {ex.Message} ({ex.ErrorCode})");
}
```

---

## Full metadata reference

| Field | Required | Description |
|---|---|---|
| `VehicleClass` | Yes | `Passenger Car`, `Truck`, `Van`, etc. |
| `VehicleMake` | Yes | Vehicle manufacturer, e.g. `BMW` |
| `VehicleModel` | Yes | Model name, e.g. `3 Series` |
| `VehicleVariant` | Yes | Variant, e.g. `320i` |
| `EngineType` | Yes | `Petrol` or `Diesel` |
| `ECUType` | Yes | `ECU`, `TCU`, or `CPC` |
| `ECUMake` | Yes | Controller manufacturer, e.g. `Bosch` |
| `ECUModel` | Yes | Controller model, e.g. `EDC17C64` |
| `ReadHardware` | Yes | Tool used, e.g. `CMD Flash`, `Alientech KESS3` |
| `ReadMode` | Yes | `OBD`, `Bench`, or `Boot` |
| `ControllerHWNumber` | No | Hardware number stamped on the controller |
| `ControllerSWNumber` | No | Software number |
| `EngineCode` | No | Engine code, e.g. `N57D30` |
| `VIN` | No | Vehicle identification number |
| `PowerOutput` | No | Stock power in kW |
| `TorqueOutput` | No | Stock torque in Nm |

---

## License

MIT — see [LICENSE](LICENSE).
