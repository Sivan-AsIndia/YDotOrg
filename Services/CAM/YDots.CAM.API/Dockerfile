# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# HEALTHCHECK NEEDS A CLIENT, and the .NET runtime image ships neither curl nor wget. Without
# this the compose healthcheck fails with "curl: not found" every fifteen seconds and the
# container is marked unhealthy while the application is working perfectly - which is exactly
# what happened to CAM and PAY.
USER root
RUN apt-get update     && apt-get install -y --no-install-recommends curl     && rm -rf /var/lib/apt/lists/*     && mkdir -p /app/logs     && chown -R $APP_UID /app/logs
USER $APP_UID


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Services/CAM/YDots.CAM.API/YDots.CAM.API.csproj", "Services/CAM/YDots.CAM.API/"]
COPY ["Services/CAM/YDots.CAM.Application/YDots.CAM.Application.csproj", "Services/CAM/YDots.CAM.Application/"]
COPY ["Services/CAM/YDots.CAM.Domain/YDots.CAM.Domain.csproj", "Services/CAM/YDots.CAM.Domain/"]
COPY ["Services/CAM/YDots.CAM.Infrastructure/YDots.CAM.Infrastructure.csproj", "Services/CAM/YDots.CAM.Infrastructure/"]
RUN dotnet restore "./Services/CAM/YDots.CAM.API/YDots.CAM.API.csproj"
COPY . .
WORKDIR "/src/Services/CAM/YDots.CAM.API"
RUN dotnet build "./YDots.CAM.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./YDots.CAM.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YDots.CAM.API.dll"]