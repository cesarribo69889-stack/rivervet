namespace RiverVet.Dominio.Constantes;

/// <summary>Acciones que se registran en la tabla auditoria.</summary>
public static class AccionesAuditoria
{
    public const string Login = "LOGIN";
    public const string LoginFallido = "LOGIN_FALLIDO";
    public const string CuentaBloqueada = "CUENTA_BLOQUEADA";
    public const string Logout = "LOGOUT";
    public const string ReusoRefreshToken = "REUSO_REFRESH_TOKEN";
    public const string CambioPassword = "CAMBIO_PASSWORD";
    public const string Crear = "CREAR";
    public const string Actualizar = "ACTUALIZAR";
    public const string Eliminar = "ELIMINAR";
    public const string Cancelar = "CANCELAR";
    public const string AjusteStock = "AJUSTE_STOCK";
    public const string Entregar = "ENTREGAR";
    public const string RegistrarPago = "REGISTRAR_PAGO";
    public const string VerificarPago = "VERIFICAR_PAGO";
    public const string WebhookPago = "WEBHOOK_PAGO";
    public const string WebhookFirmaInvalida = "WEBHOOK_FIRMA_INVALIDA";
    public const string EnviarComprobante = "ENVIAR_COMPROBANTE";
    public const string ExportarDatos = "EXPORTAR_DATOS";

    public static readonly IReadOnlyList<string> Todas =
    [
        Login, LoginFallido, CuentaBloqueada, Logout, ReusoRefreshToken, CambioPassword, Crear, Actualizar, Eliminar, Cancelar,
        AjusteStock, Entregar, RegistrarPago, VerificarPago, WebhookPago, WebhookFirmaInvalida, EnviarComprobante, ExportarDatos,
    ];
}

/// <summary>Recursos sobre los que se audita una acción.</summary>
public static class RecursosAuditoria
{
    public const string Sesion = "sesion";
    public const string Usuario = "usuario";
    public const string Cliente = "cliente";
    public const string Mascota = "mascota";
    public const string Cita = "cita";
    public const string Historial = "historial";
    public const string Vacuna = "vacuna";
    public const string Categoria = "categoria";
    public const string Producto = "producto";
    public const string Pedido = "pedido";
    public const string Pago = "pago";
    public const string Comprobante = "comprobante";
    public const string DatosPersonales = "datos_personales";
    public const string ConfiguracionPagos = "configuracion_pagos";
}
