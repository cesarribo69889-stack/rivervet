using System.Net;
using System.Security.Cryptography;

namespace RiverVet.Pruebas.Integracion;

/// <summary>
/// Crea productos PROPIOS para cada prueba (SKU único): todas las pruebas
/// comparten la base, y si usaran los productos del seed se descontarían el
/// stock unas a otras.
/// </summary>
public static class Catalogo
{
    public static string SkuUnico(string prefijo = "PRB") =>
        $"{prefijo}-{RandomNumberGenerator.GetInt32(0, int.MaxValue):X8}";

    public static async Task<int> CategoriaAsync(FabricaApi fabrica, string nombre = "Alimentos") =>
        await fabrica.EscalarAsync<int>("SELECT id FROM categorias WHERE nombre = @n", ("@n", nombre));

    public static async Task<int> CrearProductoAsync(
        FabricaApi fabrica, ClienteApi api, string tokenAdmin, int stock, decimal precio = 25.50m, bool activo = true, int stockMinimo = 2)
    {
        var r = await api.PostAsync(
            "/api/productos",
            new
            {
                categoriaId = await CategoriaAsync(fabrica),
                sku = SkuUnico(),
                nombre = "Producto de prueba",
                descripcion = "Creado por las pruebas automáticas",
                precio,
                stock,
                stockMinimo,
                activo,
            },
            tokenAdmin);
        r.Estado.ShouldBe(HttpStatusCode.Created, r.Json.ToString());
        return r.Data.GetProperty("id").GetInt32();
    }

    public static Task<int> StockAsync(FabricaApi fabrica, int productoId) =>
        fabrica.EscalarAsync<int>("SELECT stock FROM productos WHERE id = @id", ("@id", productoId));
}
