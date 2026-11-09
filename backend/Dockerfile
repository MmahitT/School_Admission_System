# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY SchoolAdmission.sln ./
COPY SchoolAdmission.Domain/*.csproj SchoolAdmission.Domain/
COPY SchoolAdmission.Application/*.csproj SchoolAdmission.Application/
COPY SchoolAdmission.Infrastructure/*.csproj SchoolAdmission.Infrastructure/
COPY SchoolAdmission.API/*.csproj SchoolAdmission.API/
COPY SchoolAdmission.Tests/*.csproj SchoolAdmission.Tests/
RUN dotnet restore SchoolAdmission.API/SchoolAdmission.API.csproj
COPY . .
RUN dotnet publish SchoolAdmission.API/SchoolAdmission.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SchoolAdmission.API.dll"]
