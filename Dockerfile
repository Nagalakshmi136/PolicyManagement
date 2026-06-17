# ============================================================
# Stage 1: Build
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first — Docker layer cache optimisation.
# Restore only re-runs when a .csproj changes, not on every source change.
COPY PolicyManagement.slnx .
COPY src/PolicyManagement.Api/PolicyManagement.Api.csproj                         src/PolicyManagement.Api/
COPY src/PolicyManagement.Application/PolicyManagement.Application.csproj         src/PolicyManagement.Application/
COPY src/PolicyManagement.Domain/PolicyManagement.Domain.csproj                   src/PolicyManagement.Domain/
COPY src/PolicyManagement.Infrastructure/PolicyManagement.Infrastructure.csproj   src/PolicyManagement.Infrastructure/

RUN dotnet restore src/PolicyManagement.Api/PolicyManagement.Api.csproj

# Copy full source and publish
COPY . .
RUN dotnet publish src/PolicyManagement.Api/PolicyManagement.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

# ============================================================
# Stage 2: Runtime
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as non-root user
RUN adduser --disabled-password --gecos "" appuser \
 && chown -R appuser /app
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "PolicyManagement.Api.dll"]
