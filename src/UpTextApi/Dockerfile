FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["UpTextApi/UpTextApi.csproj", "UpTextApi/"]
RUN dotnet restore "UpTextApi/UpTextApi.csproj"
COPY . .
WORKDIR "/src/UpTextApi"
RUN dotnet publish "./UpTextApi.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "UpTextApi.dll"]
