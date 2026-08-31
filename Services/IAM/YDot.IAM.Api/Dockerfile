# Build context is YDots/src (see DockerfileContext in YDot.IAM.Api.csproj).
#
# NO SECRET REACHES THIS IMAGE. appsettings.json ships with its sensitive values blank,
# and .dockerignore keeps appsettings.Development.json — which holds real ones — out of
# the build context entirely. Everything the container needs arrives as an environment
# variable; see the IAM service in docker-compose.yml.

# ---------- Base runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# The aspnet image ships without wget or curl, and the compose healthcheck needs one.
# /app is owned by root, so the Serilog file sink gets its own writable folder — without
# it the container logs to the console only.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/logs \
    && chown -R $APP_UID /app/logs
USER $APP_UID

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# The props files first: they carry the target framework and the pinned package versions,
# and copying them separately keeps a restore from being invalidated by a source edit.
COPY ["Services/IAM/Directory.Packages.props", "Services/IAM/"]
COPY ["Services/IAM/Directory.Build.props", "Services/IAM/"]

# Then every csproj the API references, so restore resolves the whole graph.
COPY ["Services/IAM/YDot.IAM.Api/YDot.IAM.Api.csproj", "Services/IAM/YDot.IAM.Api/"]
COPY ["Services/IAM/YDot.IAM.Application/YDot.IAM.Application.csproj", "Services/IAM/YDot.IAM.Application/"]
COPY ["Services/IAM/YDot.IAM.Domain/YDot.IAM.Domain.csproj", "Services/IAM/YDot.IAM.Domain/"]
COPY ["Services/IAM/YDot.IAM.Infrastructure/YDot.IAM.Infrastructure.csproj", "Services/IAM/YDot.IAM.Infrastructure/"]
RUN dotnet restore "./Services/IAM/YDot.IAM.Api/YDot.IAM.Api.csproj"

COPY . .
WORKDIR "/src/Services/IAM/YDot.IAM.Api"
RUN dotnet build "./YDot.IAM.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ---------- Publish ----------
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./YDot.IAM.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Belt and braces. .dockerignore already keeps these out of the context; deleting them
# here means the image is clean even if somebody builds it from a context that does not.
RUN rm -f /app/publish/appsettings.Development.json /app/publish/appsettings.Local.json

# ---------- Final ----------
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YDot.IAM.Api.dll"]
