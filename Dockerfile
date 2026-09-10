## Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution + project files first for better layer caching
COPY Lamie.sln ./
COPY Lamie.API/Lamie.API.csproj Lamie.API/
COPY Lamie.Application/Lamie.Application.csproj Lamie.Application/
COPY Lamie.Domain/Lamie.Domain.csproj Lamie.Domain/
COPY Lamie.Infrastructure/Lamie.Infrastructure.csproj Lamie.Infrastructure/
COPY Lamie.Shared/Lamie.Shared.csproj Lamie.Shared/

RUN dotnet restore "./Lamie.sln"

# Copy everything else and publish
COPY . .
RUN dotnet publish "Lamie.API/Lamie.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

## Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-eng tesseract-ocr-vie \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
ENV LocalStorage__RootPath=/app/data/uploads
ENV FeDataExport__AllowedRoot=/app/data \
    FeDataExport__TargetDirectory=/app/data/fe-data
EXPOSE 8080

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
RUN mkdir -p /app/data/uploads && chown -R $APP_UID:$APP_UID /app/data
VOLUME ["/app/data"]
USER $APP_UID
ENTRYPOINT ["dotnet", "Lamie.API.dll"]
