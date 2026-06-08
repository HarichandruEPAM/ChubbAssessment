# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy solution and project files for layer-cached restore
COPY PolicyManagement.sln ./
COPY src/PolicyManagement.Domain/PolicyManagement.Domain.csproj src/PolicyManagement.Domain/
COPY src/PolicyManagement.Application/PolicyManagement.Application.csproj src/PolicyManagement.Application/
COPY src/PolicyManagement.Infrastructure/PolicyManagement.Infrastructure.csproj src/PolicyManagement.Infrastructure/
COPY src/PolicyManagement.API/PolicyManagement.API.csproj src/PolicyManagement.API/
COPY src/PolicyManagement.UnitTests/PolicyManagement.UnitTests.csproj src/PolicyManagement.UnitTests/

# Restore NuGet packages
RUN dotnet restore PolicyManagement.sln

# Copy remaining source files
COPY . ./

# Build and publish the API project
RUN dotnet publish src/PolicyManagement.API/PolicyManagement.API.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copy published output from the build stage
COPY --from=build /app/publish .

# Expose the port the API listens on
EXPOSE 8080

# Set ASPNETCORE_URLS so Kestrel binds to port 8080 (not 80 or HTTPS)
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "PolicyManagement.API.dll"]
