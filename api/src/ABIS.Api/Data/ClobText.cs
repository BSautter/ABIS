using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace Abis.Api.Data;

/// <summary>Wraps a string that must be written to a <c>CLOB</c> column. On Oracle 11g a plain string
/// bind is a <c>VARCHAR2</c> and caps at 4000 bytes in a SQL statement, so a large payload (e.g. a
/// multi-coil X12 861) fails with ORA-01461 unless the parameter is explicitly typed as CLOB. Reading a
/// CLOB back into a <see cref="string"/> needs no handler — ODP.NET/Dapper already do that.</summary>
public readonly record struct ClobText(string Value)
{
    public static implicit operator ClobText(string value) => new(value);
}

/// <summary>Dapper handler that binds a <see cref="ClobText"/> as an Oracle CLOB when the underlying
/// provider parameter is an <see cref="OracleParameter"/>, and as a normal string otherwise (SQLite/CI).
/// Register once at startup: <c>SqlMapper.AddTypeHandler(new ClobTextHandler())</c>.</summary>
public sealed class ClobTextHandler : SqlMapper.TypeHandler<ClobText>
{
    public override void SetValue(IDbDataParameter parameter, ClobText value)
    {
        if (parameter is OracleParameter op)
        {
            op.OracleDbType = OracleDbType.Clob;
            op.Value = value.Value;
        }
        else
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.Value;
        }
    }

    public override ClobText Parse(object value) => new(value?.ToString() ?? "");
}
