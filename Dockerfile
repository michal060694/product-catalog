# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files first for better layer caching
COPY ProductCatalog.sln .
COPY ProductCatalog.Domain/ProductCatalog.Domain.csproj ProductCatalog.Domain/
COPY ProductCatalog.Application/ProductCatalog.Application.csproj ProductCatalog.Application/
COPY ProductCatalog.Infrastructure/ProductCatalog.Infrastructure.csproj ProductCatalog.Infrastructure/
COPY ProductCatalog.Api/ProductCatalog.Api.csproj ProductCatalog.Api/
COPY ProductCatalog.Tests/ProductCatalog.Tests.csproj ProductCatalog.Tests/

RUN dotnet restore

# Copy remaining source and publish
COPY . .
RUN dotnet publish ProductCatalog.Api/ProductCatalog.Api.csproj \
    -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ProductCatalog.Api.dll"]
