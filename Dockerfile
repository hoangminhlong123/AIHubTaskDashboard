# =============================
# STAGE 1: BUILD
# =============================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AIHubTaskTracker/AIHubTaskTracker.csproj AIHubTaskTracker/

# Restore dependencies
RUN dotnet restore "AIHubTaskTracker/AIHubTaskTracker.csproj"

COPY . .

# Build và publish ra thư mục /app/publish
WORKDIR /src/AIHubTaskTracker
RUN dotnet publish -c Release -o /app/publish

# =============================
# STAGE 2: RUNTIME
# =============================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AIHubTaskTracker.dll"]
