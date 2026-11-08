namespace GoalFlow.Api.Hosting;

/// <summary>
/// Lee un cuerpo con un límite explícito (Spec 005 R-16, C-23). No depende de <c>MaxRequestBodySize</c> de Kestrel ni de
/// que el servidor informe la longitud: <c>TestServer</c> almacena el cuerpo y la fija, así que esta pieza se prueba aparte.
/// </summary>
public static class LectorDeCuerpoLimitado
{
    /// <param name="longitudDeclarada">El <c>Content-Length</c>, si la petición lo trae.</param>
    /// <returns>Los bytes del cuerpo, o <c>null</c> si superan <paramref name="limite"/>.</returns>
    public static async Task<byte[]?> LeerAsync(
        Stream cuerpo, long? longitudDeclarada, int limite, CancellationToken cancellationToken)
    {
        if (longitudDeclarada > limite) return null;

        using var memoria = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int leidos;
        while ((leidos = await cuerpo.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memoria.Length + leidos > limite) return null;
            memoria.Write(buffer, 0, leidos);
        }

        return memoria.ToArray();
    }
}
