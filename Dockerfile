# ── Stage 1: Build ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies (layer caching)
COPY src/Synewave.API/Synewave.API.csproj ./src/Synewave.API/
RUN dotnet restore ./src/Synewave.API/Synewave.API.csproj

# Copy all source code
COPY src/ ./src/

# Build and publish
RUN dotnet publish ./src/Synewave.API/Synewave.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install EF migrations tool (needed for auto-migrate on startup)
COPY --from=build /app/publish .

# Railway uses PORT env variable
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "Synewave.API.dll"]
