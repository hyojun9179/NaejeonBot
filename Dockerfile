# 1. 빌드 단계 (SDK)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 프로젝트 파일 복사 및 패키지 복원
COPY ["NaejeonBot.csproj", "."]
RUN dotnet restore "NaejeonBot.csproj"

# 전체 소스 코드 복사 및 Publish
COPY . .
RUN dotnet publish "NaejeonBot.csproj" -c Release -o /app/publish

# 2. 실행 단계 (Runtime)
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# 💡 [핵심] 실제 봇이 돌아가는 final 단계에 라이브러리를 설치해야 적용됩니다!
RUN apt-get update && apt-get install -y \
    libopus-dev \
    libsodium-dev \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# 빌드 결과물 복사 및 실행
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NaejeonBot.dll"]