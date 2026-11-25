using System.Text.RegularExpressions;
namespace Vairas.Scheduling.Persistance.Migrations;
internal static class CanonicalSql
{
    public static string Read(string name)
    {
        var assembly=typeof(CanonicalSql).Assembly;
        using var stream=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith(".Sql."+name,StringComparison.Ordinal)))!;
        using var reader=new StreamReader(stream);return reader.ReadToEnd();
    }
    public static string Initial()
    {
        // Preserve the imported file byte-for-byte. EF owns transaction/history for this execution.
        var sql=Read("initial.sql");sql=sql[sql.IndexOf("CREATE TYPE lesson_kind",StringComparison.Ordinal)..];
        return sql[..sql.IndexOf("INSERT INTO \"__EFMigrationsHistory\"",StringComparison.Ordinal)];
    }
    public static string Actual()=>Regex.Replace(Read("002-actual-interval.sql"),@"(?m)^(BEGIN|COMMIT);\s*$","");
}
