FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish samples/viewer-blazor/viewer-blazor.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
ENV REPORTCREATOR_CONTAINER=1
ENV ASPNETCORE_URLS=http://+:8080
RUN bash playwright.sh install chromium --with-deps \
    && rm -rf /var/lib/apt/lists/*
EXPOSE 8080
ENTRYPOINT ["dotnet", "viewer-blazor.dll"]
