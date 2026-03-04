# Observable Smarts — OSINT Live Globe — Deployment Guide

## Prerequisites

- .NET 8 SDK (or later)
- Visual Studio 2022 (17.8+) with the **ASP.NET and web development** workload
- A target host: Azure App Service, IIS, Linux server, or Docker

---

## 1. Configuration

Before deploying, set the API keys and credentials in your hosting environment. **Never commit secrets to source control.** Use environment variables or the host's secret management instead.

| Setting | Purpose | Required |
|---|---|---|
| `CesiumIonToken` | CesiumJS Ion access token for terrain/imagery | Yes |
| `OpenSkyUsername` / `OpenSkyPassword` | OpenSky Network credentials (higher rate limits) | Optional |
| `AisHubApiKey` | AISHub API key for ship AIS data | Optional |
| `UsgsM2MUsername` / `UsgsM2MPassword` | USGS M2M API for Landsat imagery feeds | Optional |
| *(Copernicus)* | Sentinel imagery catalogue — **no key needed** (public API) | N/A |

These can be set using any of the methods below. They are listed in priority order — a value set via a higher-priority method overrides lower ones.

### Method 1: `appsettings.Local.json` (recommended for local development)

This is the simplest way to manage keys during development. The file lives alongside the other config files and is **git-ignored** so it never gets committed.

**Location:** `SkyWatch.Api/appsettings.Local.json`

**Setup:**
```bash
cd SkyWatch.Api
cp appsettings.Local.json.example appsettings.Local.json
# Now edit appsettings.Local.json with your real keys
```

**File contents:**
```json
{
  "CesiumIonToken": "your_cesium_ion_token_here",
  "AisHubApiKey": "your_aishub_api_key_here",
  "UsgsM2MUsername": "your_usgs_username_here",
  "UsgsM2MPassword": "your_usgs_password_here",
  "OpenSkyUsername": "your_opensky_username_here",
  "OpenSkyPassword": "your_opensky_password_here"
}
```

The file is loaded automatically by `Program.cs` at startup. It is listed in `.gitignore` so `git status` will never show it.

### Method 2: Environment variables

Set them in your shell, systemd unit, or container runtime:
```bash
export CesiumIonToken=your_token_here
export AisHubApiKey=your_key_here
```

### Method 3: Azure App Settings

In the Azure Portal → your App Service → **Configuration** blade, add each key as an Application Setting.

### Method 4: .NET User Secrets (local dev alternative)

```bash
cd SkyWatch.Api
dotnet user-secrets init
dotnet user-secrets set "CesiumIonToken" "your_token_here"
dotnet user-secrets set "AisHubApiKey" "your_key_here"
```

Secrets are stored in `~/.microsoft/usersecrets/<user-secrets-id>/secrets.json` — completely outside the repository.

### Where to get API keys

| Key | Sign-up URL | Notes |
|---|---|---|
| `CesiumIonToken` | https://ion.cesium.com/tokens | Free tier available; needed for premium terrain/imagery |
| `OpenSkyUsername` / `OpenSkyPassword` | https://opensky-network.org/register | Free; gives higher API rate limits |
| `AisHubApiKey` | https://www.aishub.net/join | Requires sharing an AIS receiver or a fee |
| `UsgsM2MUsername` / `UsgsM2MPassword` | https://ers.cr.usgs.gov/register | Free USGS EarthExplorer account |
| Copernicus Dataspace | https://dataspace.copernicus.eu | **No key required** for the catalogue OData API (`catalogue.dataspace.copernicus.eu/odata/v1`). It provides free, unauthenticated access to Sentinel-1/2/3/5P scene metadata, quicklook thumbnails, and GeoJSON footprints. If you want to download full-resolution products, register for a free account at the URL above. |

---

## 2. Visual Studio Publish Settings

### 2a. Publish to a Folder (self-contained deployment)

1. Right-click **SkyWatch.Api** in Solution Explorer → **Publish…**
2. Choose **Folder** as the target → click **Next**
3. Set the folder path (e.g. `bin\Release\publish`) → click **Finish**
4. Click the **pencil icon** next to the profile to edit settings:

| Setting | Value |
|---|---|
| **Configuration** | `Release` |
| **Target Framework** | `net8.0` |
| **Deployment Mode** | `Self-contained` |
| **Target Runtime** | Match your server — `win-x64`, `linux-x64`, or `linux-arm64` |
| **Target Location** | `bin\Release\net8.0\publish\` |
| **File Publish Options** | Check **Enable ReadyToRun compilation** (faster cold start, optional) |
| | Check **Trim unused assemblies** (optional, reduces size) |

5. Click **Save** → **Publish**

The output folder will contain everything needed to run the app, including the .NET runtime.

### 2b. Publish to Azure App Service

1. Right-click **SkyWatch.Api** → **Publish…**
2. Choose **Azure** → **Azure App Service (Linux)** → sign in
3. Select or create an App Service (recommended plan: **B1** or higher)
4. Settings:

| Setting | Value |
|---|---|
| **Configuration** | `Release` |
| **Target Framework** | `net8.0` |
| **Deployment Mode** | `Framework-dependent` |
| **Target Runtime** | `linux-x64` |

5. Click **Publish**
6. After deployment, go to the Azure Portal → your App Service → **Configuration** and add the app settings listed in section 1 above.

### 2c. Publish to IIS

1. Follow the Folder publish steps (2a) with **Target Runtime** = `win-x64`
2. On the server, install the [ASP.NET Core 8.0 Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0)
3. In IIS Manager:
   - Create a new site pointing to the publish folder
   - Set the Application Pool to **No Managed Code**
   - Bind to your desired port/hostname
4. Set environment variables in `web.config` or via IIS Configuration Editor:
   ```xml
   <aspNetCore processPath=".\SkyWatch.Api.exe" arguments="">
     <environmentVariables>
       <environmentVariable name="CesiumIonToken" value="your_token" />
       <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
     </environmentVariables>
   </aspNetCore>
   ```

---

## 3. CLI Publish (without Visual Studio)

```bash
# Framework-dependent (requires .NET 8 runtime on server)
dotnet publish SkyWatch.Api/SkyWatch.Api.csproj -c Release -o ./publish

# Self-contained for Linux
dotnet publish SkyWatch.Api/SkyWatch.Api.csproj -c Release -r linux-x64 --self-contained -o ./publish
```

---

## 4. Docker

Create a `Dockerfile` in the repo root:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY SkyWatch.sln .
COPY SkyWatch.Core/ SkyWatch.Core/
COPY SkyWatch.Api/ SkyWatch.Api/
RUN dotnet publish SkyWatch.Api/SkyWatch.Api.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "SkyWatch.Api.dll"]
```

```bash
docker build -t skywatch .
docker run -d -p 5000:5000 \
  -e CesiumIonToken=your_token \
  -e AisHubApiKey=your_key \
  skywatch
```

---

## 5. Production Checklist

- [ ] Set `ASPNETCORE_ENVIRONMENT` to `Production`
- [ ] Provide all API keys via environment variables or secret store
- [ ] Lock down CORS in `Program.cs` — replace `AllowAnyOrigin()` with your domain
- [ ] Put a reverse proxy (nginx, Caddy, Azure Front Door) in front for HTTPS
- [ ] Confirm background workers are running — check logs for TLE/flight/ship/imagery refresh messages
- [ ] Verify Swagger is accessible at `/swagger` (consider disabling in production)
- [ ] Set up health checks and monitoring

---

## 6. Refresh Intervals

These can be tuned in `appsettings.json` or via environment variables:

| Setting | Default | Notes |
|---|---|---|
| `TleRefreshIntervalHours` | 4 | TLE data changes slowly; 4–12 hrs is fine |
| `FlightRefreshIntervalSeconds` | 15 | OpenSky rate limit: 5 req/s (authenticated) |
| `ShipRefreshIntervalSeconds` | 60 | AISHub updates roughly every minute |
| `ImageryRefreshIntervalMinutes` | 30 | Copernicus/USGS scene catalogs |
