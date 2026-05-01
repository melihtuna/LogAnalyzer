FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["MockLogProducer.csproj", "./"]
RUN dotnet restore "MockLogProducer.csproj"

COPY . .
RUN dotnet publish "MockLogProducer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MockLogProducer.dll"]
