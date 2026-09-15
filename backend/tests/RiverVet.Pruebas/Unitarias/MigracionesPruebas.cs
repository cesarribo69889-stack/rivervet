using RiverVet.Migrador;

namespace RiverVet.Pruebas.Unitarias;

[Trait("Categoria", "Unitaria")]
public class MigracionesPruebas
{
    private static Migracion M(string nombre, string? hash = null) => new(nombre, string.Empty, hash ?? $"h-{nombre}");

    [Fact]
    public void Divide_el_script_por_las_lineas_GO() =>
        Migraciones.DividirEnLotes("CREATE TABLE a (id INT);\nGO\nCREATE INDEX i ON a(id);\ngo  \n")
            .ShouldBe(["CREATE TABLE a (id INT);", "CREATE INDEX i ON a(id);"]);

    [Fact]
    public void No_corta_palabras_con_go_ni_ejecuta_lotes_de_solo_comentarios() =>
        Migraciones.DividirEnLotes("SELECT N'algo' AS GOTO;\nGO\n-- DROP TABLE a;\n-- fin\n")
            .ShouldBe(["SELECT N'algo' AS GOTO;"]);

    [Fact]
    public void El_hash_es_igual_con_fin_de_linea_Windows_y_Linux()
    {
        Migraciones.CalcularHash("a\r\nb\r\n").ShouldBe(Migraciones.CalcularHash("a\nb\n"));
        Migraciones.CalcularHash("a\nb").ShouldNotBe(Migraciones.CalcularHash("a\nc"));
    }

    [Fact]
    public void Lista_solo_las_pendientes() =>
        Migraciones.Planificar([M("0001_a.sql"), M("0002_b.sql")], new Dictionary<string, string> { ["0001_a.sql"] = "h-0001_a.sql" })
            .Select(m => m.Nombre).ShouldBe(["0002_b.sql"]);

    [Fact]
    public void Es_idempotente_sin_pendientes_no_hay_nada_que_hacer() =>
        Migraciones.Planificar([M("0001_a.sql")], new Dictionary<string, string> { ["0001_a.sql"] = "h-0001_a.sql" }).ShouldBeEmpty();

    [Fact]
    public void Falla_si_una_migracion_aplicada_fue_editada() =>
        Should.Throw<InvalidOperationException>(() =>
                Migraciones.Planificar([M("0001_a.sql", "hash-nuevo")], new Dictionary<string, string> { ["0001_a.sql"] = "hash-original" }))
            .Message.ShouldContain("MODIFICADA");

    [Fact]
    public void Falla_si_una_migracion_aplicada_ya_no_existe_como_archivo() =>
        Should.Throw<InvalidOperationException>(() =>
                Migraciones.Planificar([], new Dictionary<string, string> { ["0001_a.sql"] = "x" }))
            .Message.ShouldContain("no existe");

    [Fact]
    public void Falla_si_aparece_una_migracion_con_numero_menor_a_la_ultima_aplicada() =>
        Should.Throw<InvalidOperationException>(() =>
                Migraciones.Planificar([M("0002_b.sql"), M("0003_c.sql")], new Dictionary<string, string> { ["0003_c.sql"] = "h-0003_c.sql" }))
            .Message.ShouldContain("fuera de orden");

    [Fact]
    public void Las_migraciones_del_repositorio_son_validas_y_sin_sintaxis_de_MySQL()
    {
        var migraciones = Migraciones.Leer(Path.Combine(Rutas.CarpetaDatabase(), "migraciones"));
        migraciones.Count.ShouldBeGreaterThanOrEqualTo(4);
        migraciones[0].Nombre.ShouldBe("0001_esquema_inicial.sql");
        foreach (var m in migraciones)
        {
            m.Contenido.ShouldNotContain("AUTO_INCREMENT", Case.Insensitive);
            m.Contenido.ShouldNotContain("IFNULL(", Case.Insensitive);
            m.Contenido.ShouldNotMatch(@"(?i)\bENUM\s*\(|\bLIMIT\s+\d");
        }
    }
}
