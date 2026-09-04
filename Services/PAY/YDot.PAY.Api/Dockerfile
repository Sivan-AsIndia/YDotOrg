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
#
# /var/ydot/receipts IS CREATED AND CHOWNED HERE FOR A REASON THAT IS EASY TO MISS.
#
# It is a named volume in compose. When a named volume's mountpoint does not exist in the image,
# Docker creates it owned by root:root - and this container runs as $APP_UID (uid 1654), which
# then cannot create the per-receipt subdirectory underneath it. Every rendered receipt PDF
# failed with
#
#     UnauthorizedAccessException: Access to the path '/var/ydot/receipts/<id>' is denied.
#
# so receipts were issued and numbered correctly but had no document behind them, and the donor's
# e-mail went out with nothing attached. Creating the directory in the IMAGE with the right owner
# is what makes Docker seed the fresh volume with that ownership instead of root's.
#
USER root
RUN apt-get update     && apt-get install -y --no-install-recommends curl     && rm -rf /var/lib/apt/lists/*     && mkdir -p /app/logs /var/ydot/receipts     && chown -R $APP_UID /app/logs /var/ydot
USER $APP_UID


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Services/PAY/YDot.PAY.Api/YDot.PAY.Api.csproj", "Services/PAY/YDot.PAY.Api/"]
COPY ["Services/PAY/YDot.PAY.Application/YDot.PAY.Application.csproj", "Services/PAY/YDot.PAY.Application/"]
COPY ["Services/PAY/YDot.PAY.Domain/YDot.PAY.Domain.csproj", "Services/PAY/YDot.PAY.Domain/"]
COPY ["Services/PAY/YDot.PAY.Infrastructure/YDot.PAY.Infrastructure.csproj", "Services/PAY/YDot.PAY.Infrastructure/"]
RUN dotnet restore "./Services/PAY/YDot.PAY.Api/YDot.PAY.Api.csproj"
COPY . .
WORKDIR "/src/Services/PAY/YDot.PAY.Api"
RUN dotnet build "./YDot.PAY.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./YDot.PAY.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "YDot.PAY.Api.dll"]