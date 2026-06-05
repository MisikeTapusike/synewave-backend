FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY src/Synewave.API/Synewave.API.csproj ./src/Synewave.API/
RUN dotnet restore ./src/Synewave.API/Synewave.API.csproj
COPY src/ ./src/
RUN dotnet publish ./src/Synewave.API/Synewave.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Synewave.API.dll"]
