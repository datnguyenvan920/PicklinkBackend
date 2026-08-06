FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["PicklinkBackend/PicklinkBackend.csproj", "PicklinkBackend/"]
RUN dotnet restore "PicklinkBackend/PicklinkBackend.csproj"

COPY . .
WORKDIR "/src/PicklinkBackend"
RUN dotnet build "PicklinkBackend.csproj" -c Release -o /app/build
RUN dotnet publish "PicklinkBackend.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_USE_POLLING_FILE_WATCHER=true
ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PicklinkBackend.dll"]
