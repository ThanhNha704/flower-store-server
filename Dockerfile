# Phase 1: Build project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Web_hoatuoi.Server.csproj", "./"]
RUN dotnet restore "Web_hoatuoi.Server.csproj"
COPY . .
RUN dotnet publish "Web_hoatuoi.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Phase 2: Run project
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Web_hoatuoi.Server.dll"]