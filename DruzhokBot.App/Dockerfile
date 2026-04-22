#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["DruzhokBot.App/DruzhokBot.App.csproj", "DruzhokBot.App/"]
RUN dotnet restore "DruzhokBot.App/DruzhokBot.App.csproj"
COPY . .
WORKDIR "/src/DruzhokBot.App"
RUN dotnet build "DruzhokBot.App.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DruzhokBot.App.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DruzhokBot.App.dll"]
