FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY API/API.csproj API/
COPY BLL/BLL.csproj BLL/
COPY DAL/DAL.csproj DAL/
RUN dotnet restore API/API.csproj

COPY API/ API/
COPY BLL/ BLL/
COPY DAL/ DAL/
COPY KnowledgeBase/ KnowledgeBase/
RUN dotnet publish API/API.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /out .

RUN mkdir -p /app/.local/video-processing && chown -R app:app /app/.local
USER app
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 10000

# Render supplies PORT; bind publicly rather than to localhost.
CMD ["sh", "-c", "exec dotnet API.dll --urls http://0.0.0.0:${PORT:-10000}"]
