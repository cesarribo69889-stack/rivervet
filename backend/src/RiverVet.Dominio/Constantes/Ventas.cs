namespace RiverVet.Dominio.Constantes;

/// <summary>
/// Estados de un pedido (CHECK CK_pedidos_estado). Transiciones permitidas:
///
///   pendiente_pago ──(Yape registrado)──► pendiente_verificacion ──(recepción confirma)──► pagado ──► entregado
///        │   ▲                                   │
///        │   └────────(recepción rechaza)────────┘
///        ├──(pasarela aprobada o pago en efectivo)──► pagado
///        └──(cancelación)──► cancelado   (el stock vuelve al inventario)
/// </summary>
public static class EstadosPedido
{
    public const string PendientePago = "pendiente_pago";
    public const string PendienteVerificacion = "pendiente_verificacion";
    public const string Pagado = "pagado";
    public const string Entregado = "entregado";
    public const string Cancelado = "cancelado";

    public static readonly IReadOnlyList<string> Todos = [PendientePago, PendienteVerificacion, Pagado, Entregado, Cancelado];

    /// <summary>Estados que cuentan como venta concretada (dashboard).</summary>
    public static readonly IReadOnlyList<string> Cobrados = [Pagado, Entregado];
}

/// <summary>Canal de la venta (CHECK CK_pedidos_canal).</summary>
public static class CanalesPedido
{
    public const string Web = "web";
    public const string Presencial = "presencial";
}

/// <summary>Métodos de pago (CHECK CK_pagos_metodo).</summary>
public static class MetodosPago
{
    public const string Yape = "yape";
    public const string Pasarela = "pasarela";
    public const string Efectivo = "efectivo";
}

/// <summary>Estados de un pago (CHECK CK_pagos_estado).</summary>
public static class EstadosPago
{
    public const string Pendiente = "pendiente";
    public const string PendienteVerificacion = "pendiente_verificacion";
    public const string Aprobado = "aprobado";
    public const string Rechazado = "rechazado";
}

/// <summary>Límites de cantidades del carrito y de una venta.</summary>
public static class LimitesVenta
{
    /// <summary>Máximo por producto en un pedido (CHECK CK_carrito_items_cantidad).</summary>
    public const int CantidadMaxima = 99;

    /// <summary>Máximo de productos distintos en un pedido.</summary>
    public const int ItemsMaximos = 50;
}
