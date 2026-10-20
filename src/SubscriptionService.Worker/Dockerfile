# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app

# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/SubscriptionService.Worker/SubscriptionService.Worker.csproj", "src/SubscriptionService.Worker/"]
COPY ["src/SubscriptionService.Application/SubscriptionService.Application.csproj", "src/SubscriptionService.Application/"]
COPY ["src/SubscriptionService.Domain/SubscriptionService.Domain.csproj", "src/SubscriptionService.Domain/"]
COPY ["src/SubscriptionService.Infrastructure/SubscriptionService.Infrastructure.csproj", "src/SubscriptionService.Infrastructure/"]
RUN dotnet restore "./src/SubscriptionService.Worker/SubscriptionService.Worker.csproj"
COPY . .
WORKDIR "/src/src/SubscriptionService.Worker"
RUN dotnet build "./SubscriptionService.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SubscriptionService.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SubscriptionService.Worker.dll"]
