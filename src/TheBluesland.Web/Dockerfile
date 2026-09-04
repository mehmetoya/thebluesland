# US-013 AC1 (spec 12.2 "Container: Official ASP.NET Core Linux runtime image, non-root", 12.5).
# Deliberately placed at src/TheBluesland.Web/Dockerfile rather than the repo root: this agent's
# write scope is limited to src/tests/tools/.github, and Docker's `<name>.dockerignore` lookup
# convention (see the sibling Dockerfile.dockerignore) lets that stay true while the build context
# is still the repo root - required because this Dockerfile also needs Directory.Build.props,
# Directory.Packages.props and src/TheBluesland.Data (TheBluesland.Web's only project reference).
# Build from the repo root: `docker build -f src/TheBluesland.Web/Dockerfile .`
#
# Deploy (Render env vars, PORT, etc.) is explicitly out of scope - US-014. This Dockerfile only
# has to build a runnable, non-root image that CI can validate on every PR (US-013 AC1).

# ---- Stage 1: Tailwind CSS 4 production build (US-013's npm-based build chain) ----
FROM node:22-alpine AS css-build
WORKDIR /src
COPY src/TheBluesland.Web/package.json ./
RUN npm install --no-audit --no-fund
COPY src/TheBluesland.Web/Styles ./Styles
COPY src/TheBluesland.Web/Components ./Components
RUN npm run build:css

# ---- Stage 2: .NET publish ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/TheBluesland.Data/TheBluesland.Data.csproj src/TheBluesland.Data/
COPY src/TheBluesland.Web/TheBluesland.Web.csproj src/TheBluesland.Web/
RUN dotnet restore src/TheBluesland.Web/TheBluesland.Web.csproj
COPY src/ src/
# Overlays the Tailwind output built in the css-build stage - wwwroot doesn't exist in source
# control (see src/TheBluesland.Web/.gitignore) so `COPY src/ src/` above never brings its own copy.
COPY --from=css-build /src/wwwroot/css/app.css src/TheBluesland.Web/wwwroot/css/app.css
RUN dotnet publish src/TheBluesland.Web/TheBluesland.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ---- Stage 3: runtime ----
# Official ASP.NET Core Linux image; ships a non-root "app" user since .NET 8 (spec 12.2) and
# already defaults ASPNETCORE_HTTP_PORTS/EXPOSE to 8080, so no port config is needed here.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# Editorial content (US-006/US-008): PlaylistContentRepository defaults to
# `Directory.GetCurrentDirectory()/content/playlists`, i.e. ./content/playlists relative to WORKDIR
# when this image runs `dotnet TheBluesland.Web.dll` from /app.
COPY content/ ./content/
USER app
# US-005/US-012: /health/ready and /health/live (WebHostFactory.cs) stay reachable unchanged -
# nothing in this stage alters routing, only how the compiled app is packaged and run.
ENTRYPOINT ["dotnet", "TheBluesland.Web.dll"]
