# Etapa de compilación
FROM ://microsoft.com AS build
WORKDIR /src
COPY ["PortfolioApi.csproj", "./"]
RUN dotnet restore "PortfolioApi.csproj"
COPY . .
RUN dotnet publish "PortfolioApi.csproj" -c Release -o /app/publish

# Etapa de ejecución
FROM ://microsoft.com AS final
WORKDIR /app
COPY --from=build /app/publish .
# Render asigna el puerto dinámicamente en la variable PORT
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PortfolioApi.dll"]
