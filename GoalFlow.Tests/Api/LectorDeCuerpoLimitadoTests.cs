using GoalFlow.Api.Hosting;

namespace GoalFlow.Tests.Api;

/// <summary>
/// Spec 005 T-11 (b), C-23: la lectura acotada del cuerpo, sin pasar por TestServer (que almacena el cuerpo y le pone
/// Content-Length, de modo que ninguna petición de prueba llega al bucle de lectura por partes).
/// </summary>
public class LectorDeCuerpoLimitadoTests
{
    private const int UnMiB = 1024 * 1024;

    /// <summary>Flujo no buscable que entrega los datos en bloques de 1.000 bytes como máximo y cuenta las lecturas.</summary>
    private sealed class FlujoEnBloques(byte[] datos) : Stream
    {
        private int _posicion;

        public int Lecturas { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Lecturas++;
            var n = Math.Min(Math.Min(count, 1000), datos.Length - _posicion);
            Array.Copy(datos, _posicion, buffer, offset, n);
            _posicion += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Lecturas++;
            var n = Math.Min(Math.Min(buffer.Length, 1000), datos.Length - _posicion);
            datos.AsMemory(_posicion, n).CopyTo(buffer);
            _posicion += n;
            return ValueTask.FromResult(n);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static byte[] Datos(int longitud)
    {
        var datos = new byte[longitud];
        new Random(7).NextBytes(datos);
        return datos;
    }

    [Fact] // T-11 (b)
    public async Task Un_flujo_de_1_MiB_mas_1_byte_sin_longitud_declarada_se_rechaza()
    {
        var flujo = new FlujoEnBloques(Datos(UnMiB + 1));

        var resultado = await LectorDeCuerpoLimitado.LeerAsync(flujo, null, UnMiB, CancellationToken.None);

        Assert.Null(resultado);
    }

    [Fact] // T-11 (b)
    public async Task Un_flujo_de_exactamente_1_MiB_sin_longitud_declarada_se_lee_completo()
    {
        var enviado = Datos(UnMiB);
        var flujo = new FlujoEnBloques(enviado);

        var resultado = await LectorDeCuerpoLimitado.LeerAsync(flujo, null, UnMiB, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.True(enviado.AsSpan().SequenceEqual(resultado), "Los bytes leídos deben ser iguales a los enviados");
    }

    [Fact] // T-11 (b)
    public async Task Una_longitud_declarada_mayor_que_el_limite_se_rechaza_sin_leer_el_flujo()
    {
        var flujo = new FlujoEnBloques(Datos(10));

        var resultado = await LectorDeCuerpoLimitado.LeerAsync(flujo, UnMiB + 1, UnMiB, CancellationToken.None);

        Assert.Null(resultado);
        Assert.Equal(0, flujo.Lecturas);
    }
}
