FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY KartDeliveryTrackingService.sln Directory.Build.props nuget.config ./
COPY packages/ packages/
COPY src/Api/KartDeliveryTrackingService.Api.csproj src/Api/
COPY src/Application/KartDeliveryTrackingService.Application.csproj src/Application/
COPY src/Domain/KartDeliveryTrackingService.Domain.csproj src/Domain/
COPY src/Infrastructure/KartDeliveryTrackingService.Infrastructure.csproj src/Infrastructure/
RUN dotnet restore src/Api/KartDeliveryTrackingService.Api.csproj

COPY src/ src/
COPY contracts/ contracts/
RUN dotnet publish src/Api/KartDeliveryTrackingService.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "KartDeliveryTrackingService.Api.dll"]
