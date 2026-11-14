FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore src/Flenta.Api/Flenta.Api.csproj
RUN dotnet publish src/Flenta.Api/Flenta.Api.csproj -c Release -o /app/publish --no-restore


FROM build AS migrate
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet tool install --global dotnet-ef --version "10.*"
ENTRYPOINT ["dotnet", "ef", "database", "update", "--project", "src/Flenta.Infrastructure"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER app

ENTRYPOINT ["dotnet", "Flenta.Api.dll"]