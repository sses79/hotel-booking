FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/HotelBooking.Api/HotelBooking.Api.csproj src/HotelBooking.Api/
COPY src/HotelBooking.Models/HotelBooking.Models.csproj src/HotelBooking.Models/
COPY src/HotelBooking.Repository/HotelBooking.Repository.csproj src/HotelBooking.Repository/
COPY src/HotelBooking.Services/HotelBooking.Services.csproj src/HotelBooking.Services/
RUN dotnet restore src/HotelBooking.Api/HotelBooking.Api.csproj

COPY src/ src/
RUN dotnet publish src/HotelBooking.Api/HotelBooking.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "HotelBooking.Api.dll"]

