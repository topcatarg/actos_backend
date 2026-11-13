using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace actos_backend.Ia;

/// <summary>
/// Abre las conexiones TCP del HttpClient de la IA probando las direcciones del host de a una,
/// con un tiempo máximo corto por intento.
///
/// Por qué existe: .NET prueba las direcciones en orden y, si la red resuelve IPv6 pero esa ruta
/// está rota, se queda esperando hasta el timeout de la llamada (90 s) sin probar IPv4. curl no
/// tiene ese problema porque prueba las dos familias en paralelo.
///
/// Si una dirección IPv6 falla, durante un rato se prueba IPv4 primero para no pagar la espera en cada llamada.
/// </summary>
public class ConectorHttp(IOptions<IaOptions> opciones, ILogger<ConectorHttp> logger)
{
    private static readonly TimeSpan PenalidadIpv6 = TimeSpan.FromMinutes(10);

    private DateTime _ipv6RotoHasta = DateTime.MinValue;

    public bool Ipv6Penalizado => DateTime.UtcNow < _ipv6RotoHasta;

    /// <summary>Para SocketsHttpHandler.ConnectCallback.</summary>
    public async ValueTask<Stream> ConectarAsync(SocketsHttpConnectionContext contexto, CancellationToken ct)
    {
        var host = contexto.DnsEndPoint.Host;
        var puerto = contexto.DnsEndPoint.Port;

        IPAddress[] direcciones = IPAddress.TryParse(host, out var ip) ? [ip] : await Dns.GetHostAddressesAsync(host, ct);
        return await ConectarAsync(host, puerto, direcciones, ct);
    }

    internal async ValueTask<Stream> ConectarAsync(string host, int puerto, IReadOnlyList<IPAddress> direcciones,
        CancellationToken ct)
    {
        if (direcciones.Count == 0)
            throw new HttpRequestException($"El DNS no devolvió direcciones para {host}.");

        var limite = TimeSpan.FromSeconds(opciones.Value.TimeoutConexionSegundos);
        var ordenadas = Ordenar(direcciones);
        Exception? ultimoError = null;

        foreach (var direccion in ordenadas)
        {
            Socket? socket = null;
            using var intento = CancellationTokenSource.CreateLinkedTokenSource(ct);
            intento.CancelAfter(limite);
            var reloj = Stopwatch.StartNew();

            try
            {
                // Adentro del try: si la máquina no tiene IPv6, crear el socket ya tira SocketException.
                socket = new Socket(direccion.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                await socket.ConnectAsync(new IPEndPoint(direccion, puerto), intento.Token);
                logger.LogDebug("Conectado a {Host} por {Direccion} en {Ms} ms", host, direccion, reloj.ElapsedMilliseconds);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                socket?.Dispose();
                ultimoError = ex;

                if (direccion.AddressFamily == AddressFamily.InterNetworkV6)
                    _ipv6RotoHasta = DateTime.UtcNow + PenalidadIpv6;

                var motivo = ex is SocketException se ? se.SocketErrorCode.ToString() : $"sin respuesta en {limite.TotalSeconds:0} s";
                logger.LogWarning("No conectó a {Host} por {Direccion} ({Familia}) tras {Ms} ms: {Motivo}. Pruebo la siguiente dirección.",
                    host, direccion, Familia(direccion), reloj.ElapsedMilliseconds, motivo);
            }
            catch
            {
                // Lo canceló quien llamó (timeout de la llamada o pedido abortado): no seguimos probando.
                socket?.Dispose();
                throw;
            }
        }

        throw new HttpRequestException(
            $"No se pudo conectar a {host}:{puerto} por ninguna de sus {ordenadas.Count} direcciones.", ultimoError);
    }

    /// <summary>
    /// Alterna las familias (como hace curl) respetando la preferencia del sistema;
    /// si IPv6 falló hace poco, arranca por IPv4.
    /// </summary>
    internal List<IPAddress> Ordenar(IReadOnlyList<IPAddress> direcciones)
    {
        var v4 = direcciones.Where(d => d.AddressFamily == AddressFamily.InterNetwork).ToList();
        var v6 = direcciones.Where(d => d.AddressFamily == AddressFamily.InterNetworkV6).ToList();

        var primeroV6 = !Ipv6Penalizado && direcciones[0].AddressFamily == AddressFamily.InterNetworkV6;
        var (a, b) = primeroV6 ? (v6, v4) : (v4, v6);

        var resultado = new List<IPAddress>(direcciones.Count);
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            if (i < a.Count) resultado.Add(a[i]);
            if (i < b.Count) resultado.Add(b[i]);
        }
        return resultado;
    }

    private static string Familia(IPAddress d) => d.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
}
