FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["AtoZClinical.Web/AtoZClinical.Web.csproj", "AtoZClinical.Web/"]
COPY ["AtoZClinical.Infrastructure/AtoZClinical.Infrastructure.csproj", "AtoZClinical.Infrastructure/"]
COPY ["AtoZClinical.Core/AtoZClinical.Core.csproj", "AtoZClinical.Core/"]
RUN dotnet restore "AtoZClinical.Web/AtoZClinical.Web.csproj"
COPY . .
RUN dotnet publish "AtoZClinical.Web/AtoZClinical.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "AtoZClinical.Web.dll"]
