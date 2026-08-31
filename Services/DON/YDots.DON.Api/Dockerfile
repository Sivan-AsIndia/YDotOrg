# Build context is YDots/src (see DockerfileContext in YDots.DON.Api.csproj).
# See https://aka.ms/customizecontainer for how Visual Studio uses this file.

# ---------- Base runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# The aspnet image ships without curl, and the compose healthcheck needs it.
# /app is owned by root, so the Serilog file sink needs its own writable folder —
# without this the container logs to the console only.
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/logs \
    && chown -R $APP_UID /app/logs
USER $APP_UID

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# The two props files come first so a restore is not invalidated by a source change.
COPY ["Services/DON/Directory.Packages.props", "Services/DON/"]
COPY ["Services/DON/Directory.Build.props", "Services/DON/"]
COPY ["Services/DON/YDots.DON.Api/YDots.DON.Api.csproj", "Services/DON/YDots.DON.Api/"]
COPY ["Services/DON/YDots.DON.Application/YDots.DON.Application.csproj", "Services/DON/YDots.DON.Application/"]
COPY ["Services/DON/YDots.DON.Domain/YDots.DON.Domain.csproj", "Services/DON/YDots.DON.Domain/"]
COPY ["Services/DON/YDots.DON.Infrastructure/YDots.DON.Infrastructure.csproj", "Services/DON/YDots.DON.Infrastructure/"]
RUN dotnet restore "./Services/DON/YDots.DON.Api/YDots.DON.Api.csproj"
COPY . .
WORKDIR "/src/Services/DON/YDots.DON.Api"
RUN dotnet build "./YDots.DON.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ---------- Publish ----------
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./YDots.DON.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ---------- Final ----------
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Kestrel listens on HTTP only. HTTPS termination is not this container's job,
# and an HTTPS port here would make UseHttpsRedirection() redirect proxied calls.
ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "YDots.DON.Api.dll"]
