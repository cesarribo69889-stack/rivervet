using System.Text.Json;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RiverVet.Aplicacion.Abstracciones;
using RiverVet.Aplicacion.Comun;
using RiverVet.Aplicacion.Ventas;
using RiverVet.Dominio.Constantes;

namespace RiverVet.Aplicacion.Pagos;

/// <summary>
/// HU-017 (Yape por QR), HU-019 (pasarela con checkout hospedado) y pago en
/// efectivo en caja.
///
/// Reglas comunes:
/// - El MONTO siempre sale del pedido guardado en la base, nunca de la petición.
/// - Solo se paga un pedido en estado pendiente_pago.
/// - Orden de bloqueo: SIEMPRE primero el pedido y después el pago (igual que
///   al cancelar). Si un flujo bloqueara pago → pedido y otro pedido → pago,
///   podrían quedar esperándose mutuamente (deadlock).
/// </summary>
public sealed class PagosServicio(
    IPagosRepositorio pagos,
    IPedidosRepositorio pedidos,
    IPasarelaPagos pasarela,
    IUnidadTrabajo unidadTrabajo,
    IServicioAuditoria auditoria,
    IContextoPeticion contexto,
    IValidator<EventoPasarela> validadorEvento,
    IOptions<OpcionesPagos> opciones,
    TimeProvider reloj,
    ILogger<PagosServicio> logger)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, string> _mensajesUnicos = new()
    {
        // Fraude clásico: reutilizar la captura de un Yape anterior para "pagar" otro pedido.
        ["UX_pagos_yape_operacion"] = "Ese número de operación de Yape ya fue registrado",
        ["UX_pagos_referencia"] = "Esa referencia de pago ya existe",
    };

    private OpcionesPagos Opciones => opciones.Value;

    // ── Yape (HU-017) ─────────────────────────────────────────────────────

    /// <summary>Número y titular del Yape del negocio, para mostrar junto al QR.</summary>
    public DatosYapeRespuesta DatosYape()
    {
        var yape = Opciones.Yape;
        if (!yape.Habilitado)
        {
            throw new ServicioNoDisponibleException("El pago con Yape no está habilitado");
        }
        return new DatosYapeRespuesta(yape.Celular!, yape.Titular,
        [
            "Abre Yape y escanea el QR de RiverVet o yapea al celular indicado.",
            "Paga EXACTAMENTE el total de tu pedido.",
            "Copia el número de operación que te muestra Yape y regístralo aquí.",
            "Recepción confirmará el pago y te avisaremos cuando esté listo.",
        ]);
    }

    /// <summary>
    /// El cliente (o recepción por él) registra el código de operación. No se
    /// da por pagado: queda "pendiente de verificación" hasta que recepción
    /// confirme en la app de Yape del negocio que el dinero llegó. Una captura
    /// de pantalla o un código se pueden inventar; el saldo de la cuenta no.
    /// </summary>
    public async Task<PedidoRespuesta> RegistrarYapeAsync(int pedidoId, RegistrarYapeSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        if (!Opciones.Yape.Habilitado)
        {
            throw new ServicioNoDisponibleException("El pago con Yape no está habilitado");
        }
        try
        {
            await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
            var pedido = await BloquearPendienteDePagoAsync(pedidoId, contexto.PropietarioId, ct);
            var pagoId = await pagos.CrearAsync(
                pedido.Id, MetodosPago.Yape, EstadosPago.PendienteVerificacion, pedido.Total, solicitud.CodigoOperacion,
                null, null, null, ct);
            await pedidos.CambiarEstadoAsync(pedido.Id, EstadosPedido.PendientePago, EstadosPedido.PendienteVerificacion, ct);
            await auditoria.RegistrarAsync(
                AccionesAuditoria.RegistrarPago, RecursosAuditoria.Pago, pagoId,
                new { metodo = MetodosPago.Yape, pedido.Codigo, monto = pedido.Total, solicitud.CodigoOperacion }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        catch (RegistroDuplicadoException ex)
        {
            throw ex.ComoConflicto(_mensajesUnicos);
        }
        return await DetalleAsync(pedidoId, ct);
    }

    public Task<IReadOnlyList<PagoPendienteVerificacion>> PendientesDeVerificacionAsync(CancellationToken ct = default) =>
        pagos.PendientesDeVerificacionAsync(ct);

    /// <summary>Recepción confirma o rechaza un Yape pendiente de verificación.</summary>
    public async Task<PedidoRespuesta> VerificarAsync(int pagoId, VerificarPagoSolicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var referencia = await pagos.ObtenerAsync(pagoId, ct) ?? throw new NoEncontradoException("Pago no encontrado");
        var aprobado = solicitud.Aprobado!.Value;
        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            // Pedido → pago (ver el orden de bloqueo arriba).
            var pedido = await pedidos.BloquearAsync(referencia.PedidoId, null, ct) ?? throw new NoEncontradoException("Pedido no encontrado");
            var pago = await pagos.BloquearAsync(pagoId, ct) ?? throw new NoEncontradoException("Pago no encontrado");
            if (pago.Metodo != MetodosPago.Yape || pago.Estado != EstadosPago.PendienteVerificacion
                || pedido.Estado != EstadosPedido.PendienteVerificacion)
            {
                throw new ConflictoException("Este pago ya fue verificado o ya no está pendiente");
            }

            await pagos.CambiarEstadoAsync(pagoId, EstadosPago.PendienteVerificacion,
                aprobado ? EstadosPago.Aprobado : EstadosPago.Rechazado, contexto.UsuarioId, ct);
            // Rechazado: el pedido vuelve a "pendiente de pago" para que el
            // cliente pueda registrar el Yape correcto (o pagar de otra forma).
            await pedidos.CambiarEstadoAsync(pedido.Id, EstadosPedido.PendienteVerificacion,
                aprobado ? EstadosPedido.Pagado : EstadosPedido.PendientePago, ct);
            await auditoria.RegistrarAsync(
                AccionesAuditoria.VerificarPago, RecursosAuditoria.Pago, pagoId,
                new { pedido.Codigo, aprobado, observacion = solicitud.Observacion }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return await DetalleAsync(referencia.PedidoId, ct);
    }

    // ── Efectivo (caja) ───────────────────────────────────────────────────

    public async Task<PedidoRespuesta> RegistrarEfectivoAsync(int pedidoId, CancellationToken ct = default)
    {
        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            var pedido = await BloquearPendienteDePagoAsync(pedidoId, null, ct);
            var pagoId = await pagos.CrearAsync(
                pedido.Id, MetodosPago.Efectivo, EstadosPago.Aprobado, pedido.Total, null, null, null, contexto.UsuarioId, ct);
            await pedidos.CambiarEstadoAsync(pedido.Id, EstadosPedido.PendientePago, EstadosPedido.Pagado, ct);
            await auditoria.RegistrarAsync(
                AccionesAuditoria.RegistrarPago, RecursosAuditoria.Pago, pagoId,
                new { metodo = MetodosPago.Efectivo, pedido.Codigo, monto = pedido.Total }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return await DetalleAsync(pedidoId, ct);
    }

    // ── Pasarela (HU-019) ─────────────────────────────────────────────────

    /// <summary>
    /// Crea la sesión de pago en la pasarela y devuelve la URL de SU página,
    /// donde el cliente escribe la tarjeta. Aquí nunca llega ningún dato de tarjeta.
    /// </summary>
    public async Task<CheckoutRespuesta> IniciarCheckoutAsync(int pedidoId, CancellationToken ct = default)
    {
        if (!Opciones.Pasarela.Habilitada)
        {
            throw new ServicioNoDisponibleException("El pago con tarjeta no está habilitado");
        }
        var pedido = await pedidos.ObtenerAsync(pedidoId, contexto.PropietarioId, ct) ?? throw new NoEncontradoException("Pedido no encontrado");
        if (pedido.Estado != EstadosPedido.PendientePago)
        {
            throw new ConflictoException("El pedido no está pendiente de pago");
        }

        // La llamada a la pasarela va FUERA de la transacción: no se tienen
        // filas bloqueadas mientras se espera a un servicio externo.
        var sesion = await pasarela.CrearCheckoutAsync(pedido.Codigo, pedido.Total, ct);

        int pagoId;
        await using (var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct))
        {
            var bloqueado = await BloquearPendienteDePagoAsync(pedidoId, contexto.PropietarioId, ct);
            pagoId = await pagos.CrearAsync(
                bloqueado.Id, MetodosPago.Pasarela, EstadosPago.Pendiente, bloqueado.Total, null,
                pasarela.Proveedor, sesion.Referencia, null, ct);
            await auditoria.RegistrarAsync(
                AccionesAuditoria.RegistrarPago, RecursosAuditoria.Pago, pagoId,
                new { metodo = MetodosPago.Pasarela, bloqueado.Codigo, monto = bloqueado.Total, proveedor = pasarela.Proveedor }, ct: ct);
            await tx.ConfirmarAsync(ct);
        }
        return new CheckoutRespuesta(pagoId, sesion.Referencia, sesion.UrlCheckout);
    }

    /// <summary>
    /// Webhook de la pasarela. Orden estricto:
    ///   1) verificar la firma sobre los BYTES recibidos (antes de interpretar nada);
    ///   2) registrar el evento: si ya se había recibido, no se procesa otra vez;
    ///   3) comparar monto y moneda con los guardados;
    ///   4) recién entonces cambiar estados.
    /// </summary>
    public async Task<ResultadoWebhook> ProcesarWebhookAsync(byte[] cuerpo, string? tiempo, string? firma, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cuerpo);
        var config = Opciones.Pasarela;
        if (!config.Habilitada || string.IsNullOrEmpty(config.SecretoWebhook))
        {
            throw new ServicioNoDisponibleException("La pasarela de pagos no está configurada");
        }

        if (!FirmaWebhook.Verificar(config.SecretoWebhook, tiempo, firma, cuerpo, reloj.GetUtcNow(), config.ToleranciaSegundos))
        {
            await RegistrarFirmaInvalidaAsync(ct);
            throw new NoAutenticadoException("Firma inválida");
        }

        var evento = LeerEvento(cuerpo);
        await using var tx = await unidadTrabajo.IniciarTransaccionAsync(ct: ct);
        var eventoId = await pagos.RegistrarEventoAsync(pasarela.Proveedor, evento.Id!, evento.Tipo!, true, ct);
        if (eventoId is null)
        {
            // La pasarela reintenta los avisos: responder OK sin repetir nada.
            return new ResultadoWebhook("duplicado");
        }

        var resultado = await AplicarEventoAsync(evento, ct);
        await pagos.MarcarEventoProcesadoAsync(eventoId.Value, ct);
        await auditoria.RegistrarAsync(
            AccionesAuditoria.WebhookPago, RecursosAuditoria.Pago, evento.Referencia,
            new { evento.Id, evento.Tipo, resultado }, ct: ct);
        await tx.ConfirmarAsync(ct);
        return new ResultadoWebhook(resultado);
    }

    private async Task<string> AplicarEventoAsync(EventoPasarela evento, CancellationToken ct)
    {
        var referencia = await pagos.BuscarPorReferenciaAsync(pasarela.Proveedor, evento.Referencia!, ct);
        if (referencia is null)
        {
            logger.LogWarning("Webhook con referencia desconocida (evento {EventoId})", evento.Id);
            return "referencia_desconocida";
        }
        var pedido = await pedidos.BloquearAsync(referencia.PedidoId, null, ct);
        var pago = await pagos.BloquearAsync(referencia.Id, ct);
        if (pedido is null || pago is null)
        {
            return "referencia_desconocida";
        }

        // Aunque la firma sea válida, el monto debe coincidir con lo que
        // cobramos: protege de una sesión de pago creada por otro monto.
        if (evento.Monto != pago.Monto || !string.Equals(evento.Moneda, pago.Moneda, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Webhook con monto o moneda distintos al pago {PagoId}: no se aprueba", pago.Id);
            return "monto_no_coincide";
        }
        if (pago.Estado != EstadosPago.Pendiente)
        {
            // Lo anulamos (el pedido se canceló) pero la pasarela igual cobró:
            // el dinero entró y hay que devolverlo. No se oculta como "sin cambios".
            if (evento.Tipo == "pago.aprobado" && pago.Estado == EstadosPago.Rechazado)
            {
                logger.LogError("Pago {PagoId} anulado pero cobrado por la pasarela (pedido {Codigo}): requiere devolución",
                    pago.Id, pedido.Codigo);
                return "requiere_devolucion";
            }
            return "sin_cambios";
        }

        switch (evento.Tipo)
        {
            case "pago.aprobado":
                await pagos.CambiarEstadoAsync(pago.Id, EstadosPago.Pendiente, EstadosPago.Aprobado, null, ct);
                if (pedido.Estado == EstadosPedido.PendientePago)
                {
                    await pedidos.CambiarEstadoAsync(pedido.Id, EstadosPedido.PendientePago, EstadosPedido.Pagado, ct);
                    return "aprobado";
                }
                // El dinero se cobró pero el pedido ya estaba pagado por otro
                // medio o cancelado: se deja constancia para devolverlo.
                logger.LogError("Pago {PagoId} aprobado para el pedido {Codigo} en estado {Estado}: requiere devolución",
                    pago.Id, pedido.Codigo, pedido.Estado);
                return "requiere_devolucion";
            case "pago.rechazado":
                await pagos.CambiarEstadoAsync(pago.Id, EstadosPago.Pendiente, EstadosPago.Rechazado, null, ct);
                return "rechazado";
            default:
                return "ignorado";
        }
    }

    private EventoPasarela LeerEvento(byte[] cuerpo)
    {
        EventoPasarela? evento;
        try
        {
            evento = JsonSerializer.Deserialize<EventoPasarela>(cuerpo, _json);
        }
        catch (JsonException ex)
        {
            throw new ValidacionException("El cuerpo del webhook no es un JSON válido", ex);
        }
        if (evento is null || !validadorEvento.Validate(evento).IsValid)
        {
            throw new ValidacionException("(general)", "Evento de pago incompleto");
        }
        return evento;
    }

    /// <summary>
    /// Deja constancia del intento con un id PROPIO, no con el que trae el
    /// cuerpo: ese id no es confiable, y si se guardara, un atacante podría
    /// "gastar" por adelantado el id de un evento real para que después se
    /// descarte como duplicado.
    /// </summary>
    private async Task RegistrarFirmaInvalidaAsync(CancellationToken ct)
    {
        logger.LogWarning("Webhook de pagos con firma inválida desde {Ip}", contexto.Ip);
        await pagos.RegistrarEventoAsync(pasarela.Proveedor, "firma-invalida-" + Guid.NewGuid().ToString("N"), "desconocido", false, ct);
        await auditoria.RegistrarAsync(AccionesAuditoria.WebhookFirmaInvalida, RecursosAuditoria.Pago, ct: ct);
    }

    private async Task<PedidoBloqueado> BloquearPendienteDePagoAsync(int pedidoId, int? propietarioId, CancellationToken ct)
    {
        var pedido = await pedidos.BloquearAsync(pedidoId, propietarioId, ct) ?? throw new NoEncontradoException("Pedido no encontrado");
        return pedido.Estado == EstadosPedido.PendientePago
            ? pedido
            : throw new ConflictoException(pedido.Estado switch
            {
                EstadosPedido.PendienteVerificacion => "Este pedido ya tiene un pago en verificación",
                EstadosPedido.Cancelado => "El pedido está cancelado",
                _ => "El pedido ya está pagado",
            });
    }

    private async Task<PedidoRespuesta> DetalleAsync(int pedidoId, CancellationToken ct)
    {
        var pedido = await pedidos.ObtenerAsync(pedidoId, null, ct) ?? throw new NoEncontradoException("Pedido no encontrado");
        return new PedidoRespuesta(pedido, await pedidos.ItemsAsync(pedidoId, ct), await pedidos.PagosAsync(pedidoId, ct));
    }
}
