# Build context is the repository root (see docker-compose.yml)

# --- Build stage: restore and publish with the full .NET SDK ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project file first, so the restore step is cached until dependencies change
COPY src/StudentSuccess.Web/StudentSuccess.Web.csproj src/StudentSuccess.Web/
RUN dotnet restore src/StudentSuccess.Web/StudentSuccess.Web.csproj

COPY src/StudentSuccess.Web/ src/StudentSuccess.Web/
RUN dotnet publish src/StudentSuccess.Web/StudentSuccess.Web.csproj -c Release -o /app --no-restore

# --- Runtime stage: only the ASP.NET runtime, running as a non-root user ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "StudentSuccess.Web.dll"]
