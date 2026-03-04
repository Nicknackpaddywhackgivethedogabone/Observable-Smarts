# SkyWatch OSINT Live Globe — Deployment Guide

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

These can be set as:
- **Environment variables** — e.g. `CesiumIonToken=your_token_here`
- **Azure App Settings** — in the Configuration blade
- **User secrets** (local dev) — `dotnet user-secrets set "CesiumIonToken" "your_token_here"`

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
| **File Publish Options** | Check **Produce single file** |
| | Check **Trim unused assemblies** (optional, reduces size) |
| | Check **Enable ReadyToRun compilation** (faster cold start) |

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

# Self-contained single-file for Linux
dotnet publish SkyWatch.Api/SkyWatch.Api.csproj -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o ./publish
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
