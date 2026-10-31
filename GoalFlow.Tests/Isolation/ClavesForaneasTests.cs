using Npgsql;
using static GoalFlow.Tests.Isolation.BaseDeAislamiento;

namespace GoalFlow.Tests.Isolation;

/// <summary>Spec 002 T-22. Toda escritura corre en una transacción que siempre se revierte.</summary>
[Collection(ColeccionDeAislamiento.Nombre)]
public class ClavesForaneasTests(BaseDeAislamiento db)
{
    private const string ViolaClaveForanea = "23503";
    private const string FkEquipos = "fk_equipos_ligas";
    private const string FkAdmins = "fk_admins_liga_ligas";

    private static async Task<NpgsqlConnection> Abrir(string cadena)
    {
        var conexion = new NpgsqlConnection(cadena);
        await conexion.OpenAsync();
        return conexion;
    }

    private static async Task Ejecutar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task RechazaPorClave(
        NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql, string restriccion)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => Ejecutar(conexion, transaccion, sql));

        Assert.Equal(ViolaClaveForanea, error.SqlState);
        Assert.Equal(restriccion, error.ConstraintName);
    }

    [Fact]
    public async Task T22_insertar_equipo_de_una_liga_inexistente_viola_la_clave()
    {
        var inexistente = Guid.NewGuid();
        await using var conexion = await Abrir(db.CadenaApp);
        await using var transaccion = await conexion.BeginTransactionAsync();
        await Ejecutar(conexion, transaccion, $"select set_config('app.tenant_id', '{inexistente}', true)");

        await RechazaPorClave(
            conexion, transaccion,
            $"insert into goalflow.equipos (id, tenant_id, nombre) values ('{Guid.NewGuid()}', '{inexistente}', 'equipo-huerfano')",
            FkEquipos);
    }

    [Fact]
    public async Task T22_insertar_admin_de_una_liga_inexistente_viola_la_clave()
    {
        await using var conexion = await Abrir(db.CadenaSuperusuario);
        await using var transaccion = await conexion.BeginTransactionAsync();

        await RechazaPorClave(
            conexion, transaccion,
            $"insert into goalflow.admins_liga (user_id, tenant_id, rol) values ('{Guid.NewGuid()}', '{Guid.NewGuid()}', 'admin')",
            FkAdmins);
    }

    // Una liga con hijos en ambas tablas no probaría nada: una sola clave RESTRICT bloquea el borrado
    // aunque la otra fuera CASCADE. Por eso cada caso deja un único hijo.
    [Fact]
    public async Task T22_una_liga_con_solo_un_equipo_no_se_puede_borrar()
    {
        var liga = Guid.NewGuid();
        await using var conexion = await Abrir(db.CadenaSuperusuario);
        await using var transaccion = await conexion.BeginTransactionAsync();
        await Ejecutar(conexion, transaccion, $"""
            insert into goalflow.ligas (id, slug, nombre) values ('{liga}', 'liga-solo-equipo', 'Liga con equipo');
            insert into goalflow.equipos (id, tenant_id, nombre) values ('{Guid.NewGuid()}', '{liga}', 'equipo-unico');
            """);

        await RechazaPorClave(conexion, transaccion, $"delete from goalflow.ligas where id = '{liga}'", FkEquipos);
    }

    [Fact]
    public async Task T22_una_liga_con_solo_un_admin_no_se_puede_borrar()
    {
        var liga = Guid.NewGuid();
        await using var conexion = await Abrir(db.CadenaSuperusuario);
        await using var transaccion = await conexion.BeginTransactionAsync();
        await Ejecutar(conexion, transaccion, $"""
            insert into goalflow.ligas (id, slug, nombre) values ('{liga}', 'liga-solo-admin', 'Liga con admin');
            insert into goalflow.admins_liga (user_id, tenant_id, rol) values ('{Guid.NewGuid()}', '{liga}', 'admin');
            """);

        await RechazaPorClave(conexion, transaccion, $"delete from goalflow.ligas where id = '{liga}'", FkAdmins);
    }

    [Theory]
    [InlineData(FkEquipos)]
    [InlineData(FkAdmins)]
    public async Task T22_el_catalogo_muestra_on_delete_restrict(string restriccion)
    {
        await using var conexion = await Abrir(db.CadenaSuperusuario);
        await using var comando = new NpgsqlCommand(
            "select confdeltype::text from pg_constraint where conname = @nombre and connamespace = 'goalflow'::regnamespace",
            conexion);
        comando.Parameters.AddWithValue("nombre", restriccion);

        Assert.Equal("r", await comando.ExecuteScalarAsync());
    }
}
