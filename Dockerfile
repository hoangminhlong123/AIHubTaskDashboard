# =============================
# STAGE 1: BUILD
# =============================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY AIHubTaskDashboard/AIHubTaskDashboard.csproj ./AIHubTaskDashboard/

RUN dotnet restore "./AIHubTaskDashboard/AIHubTaskDashboard.csproj"

# Copy toàn bộ source
COPY AIHubTaskDashboard/. ./AIHubTaskDashboard/

WORKDIR /src/AIHubTaskDashboard
RUN dotnet publish -c Release -o /app/publish

# =============================
# STAGE 2: RUNTIME
# =============================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AIHubTaskDashboard.dll"]
