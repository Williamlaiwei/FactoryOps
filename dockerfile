FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY FactoryOps.Web/*.csproj FactoryOps.Web/
RUN dotnet restore FactoryOps.Web/FactoryOps.Web.csproj

COPY FactoryOps.Web/ FactoryOps.Web/
RUN dotnet publish FactoryOps.Web/FactoryOps.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "FactoryOps.Web.dll"]