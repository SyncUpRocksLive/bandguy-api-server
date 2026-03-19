using Dapper;
using Npgsql;
using System.Data;
using System.Reflection;
using System.Text.Json;

namespace api.DataLayer;

[AttributeUsage(AttributeTargets.Property)]
public class JsonbAttribute : Attribute { }

public class JsonbTypeHandler<T> : SqlMapper.TypeHandler<T>
{
    public override void SetValue(IDbDataParameter parameter, T? value)
    {
        parameter.Value = value == null ? DBNull.Value : JsonSerializer.Serialize(value);

        if (parameter is NpgsqlParameter ngParam)
            ngParam.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
    }

    public override T? Parse(object value)
    {
        if (value == null ||  value == DBNull.Value)
            return default;

        return JsonSerializer.Deserialize<T>(value.ToString()!);
    }
}

public static class DapperEntityMapper
{
    public static void RegisterHandlers(params Assembly[] assemblies)
    {
        var types = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t.GetProperties()
                .Any(p => p.GetCustomAttribute<JsonbAttribute>() != null));

        foreach (var type in types)
        {
            var jsonProperties = type.GetProperties().Where(p => p.GetCustomAttribute<JsonbAttribute>() != null);

            foreach(var property in jsonProperties)
            {
                var handlerType = typeof(JsonbTypeHandler<>).MakeGenericType(property.PropertyType);
                var handler = (SqlMapper.ITypeHandler)Activator.CreateInstance(handlerType)!;
                SqlMapper.AddTypeHandler(property.PropertyType, handler);
            }

        }
    }
}