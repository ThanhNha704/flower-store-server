# Phase 1: Build project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj va restore
COPY ["Web_hoatuoi.Server.csproj", "./"]
RUN dotnet restore "Web_hoatuoi.Server.csproj"

# Copy toan bo source code va build Release
COPY . .
RUN dotnet publish "Web_hoatuoi.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Phase 2: Run container
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

# Chu y dung phan biet hoa/thuong cua ten file dll
ENTRYPOINT ["dotnet", "Web_hoatuoi.Server.dll"]