FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["LogAnalyzer/LogAnalyzer.Api.csproj", "LogAnalyzer/"]
COPY ["LogAnalyzer.Domain/LogAnalyzer.Domain.csproj", "LogAnalyzer.Domain/"]
COPY ["LogAnalyzer.Infrastructure/LogAnalyzer.Infrastructure.csproj", "LogAnalyzer.Infrastructure/"]
COPY ["LogAnalyzer.AI/LogAnalyzer.AI.csproj", "LogAnalyzer.AI/"]
COPY ["LogAnalyzer.Processor/LogAnalyzer.Processor.csproj", "LogAnalyzer.Processor/"]
RUN dotnet restore "LogAnalyzer/LogAnalyzer.Api.csproj"

COPY . .
WORKDIR "/src/LogAnalyzer"
RUN dotnet publish "LogAnalyzer.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "LogAnalyzer.Api.dll"]
