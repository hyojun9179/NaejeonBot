FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["NaejeonBot.csproj", "."]
RUN dotnet restore "NaejeonBot.csproj"
COPY . .
RUN dotnet publish "NaejeonBot.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NaejeonBot.dll"]