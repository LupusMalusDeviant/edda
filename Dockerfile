# ─── Stage 1: Build ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore — copy csproj files first so the NuGet layer is cached.
COPY ["Directory.Build.props", "."]
COPY ["global.json", "."]
COPY ["src/Core/Core.csproj", "src/Core/"]
COPY ["src/AKG/AKG.csproj", "src/AKG/"]
COPY ["src/AKG.Ingestion/AKG.Ingestion.csproj", "src/AKG.Ingestion/"]
COPY ["src/Embeddings/Embeddings.csproj", "src/Embeddings/"]
COPY ["src/Security/Security.csproj", "src/Security/"]
COPY ["src/Sandboxing/Sandboxing.csproj", "src/Sandboxing/"]
COPY ["src/Agent/Agent.csproj", "src/Agent/"]
COPY ["src/AKG.Mcp/AKG.Mcp.csproj", "src/AKG.Mcp/"]
COPY ["src/Edda.Hosting/Edda.Hosting.csproj", "src/Edda.Hosting/"]
COPY ["src/Web/Web.csproj", "src/Web/"]
RUN dotnet restore "src/Web/Web.csproj"

# Build + publish the web host.
COPY . .
RUN dotnet publish "src/Web/Web.csproj" \
    -c Release -o /app/publish \
    /p:UseAppHost=false

# ─── Stage 2: Runtime ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Docker CLI for the opt-in TDK docker sandbox (docker-compose.tdk.yml), python3 for the
# default wasm sandbox, curl for the container healthcheck.
RUN apt-get update && apt-get install -y --no-install-recommends \
    docker.io \
    python3 \
    curl \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# A8: run as a non-root user. UID/GID default to 1000 so host bind mounts (./data, ./knowledge)
# owned by the typical first user work out of the box; override via build args when they differ.
# The opt-in TDK docker mode grants socket access via group_add in docker-compose.tdk.yml.
ARG APP_UID=1000
ARG APP_GID=1000
RUN groupadd --gid "${APP_GID}" edda \
    && useradd --uid "${APP_UID}" --gid "${APP_GID}" --create-home edda

COPY --from=build --chown=edda:edda /app/publish .
# Bundled knowledge is staged as knowledge.seed; an empty mounted knowledge/ volume
# self-seeds from it at first start (idempotent for bare-metal bind mounts).
COPY --from=build --chown=edda:edda /src/knowledge ./knowledge.seed
RUN mkdir -p /app/data /app/knowledge && chown -R edda:edda /app

USER edda

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Edda.Web.dll"]
