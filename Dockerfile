# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore
COPY ["src/RemoteExec.Api/RemoteExec.Api.csproj", "RemoteExec.Api/"]
RUN dotnet restore "RemoteExec.Api/RemoteExec.Api.csproj"

# Copy everything else
COPY src/RemoteExec.Api/ RemoteExec.Api/
WORKDIR "/src/RemoteExec.Api"
RUN dotnet build "RemoteExec.Api.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "RemoteExec.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "RemoteExec.Api.dll"]
