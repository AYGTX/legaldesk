FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5000
# Chromium + Liberation Sans for headless PDF generation (PuppeteerSharp); Tesseract (French, English, Arabic) and
# pdftoppm to read scanned contracts (R-M2-4)
RUN apt-get update && apt-get install -y --no-install-recommends \
        chromium fonts-liberation fontconfig \
        tesseract-ocr tesseract-ocr-fra tesseract-ocr-ara poppler-utils \
    && fc-cache -fv \
    && rm -rf /var/lib/apt/lists/*
# Tell PuppeteerSharp to use the system Chromium instead of downloading its own
ENV CHROME_EXECUTABLE_PATH=/usr/bin/chromium

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["SignFlow.Domain/SignFlow.Domain.csproj", "SignFlow.Domain/"]
COPY ["SignFlow.Application/SignFlow.Application.csproj", "SignFlow.Application/"]
COPY ["SignFlow.Infrastructure/SignFlow.Infrastructure.csproj", "SignFlow.Infrastructure/"]
COPY ["SignFlow.Api/SignFlow.Api.csproj", "SignFlow.Api/"]
RUN dotnet restore "SignFlow.Api/SignFlow.Api.csproj"

COPY . .
WORKDIR "/src/SignFlow.Api"
RUN dotnet build "SignFlow.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SignFlow.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
RUN mkdir -p /app/uploads
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SignFlow.Api.dll"]
