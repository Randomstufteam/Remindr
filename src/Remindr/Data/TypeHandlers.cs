using System.Data;
using System.Globalization;
using Dapper;

namespace Remindr.Data;

/// <summary>Dapper handlers for the PLAN's date storage: due_date = yyyy-MM-dd, due_time = HH:mm (local wall-clock text).</summary>
public static class TypeHandlers
{
    public const string DateFormat = "yyyy-MM-dd";
    public const string TimeFormat = "HH:mm";

    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1) return;
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new TimeOnlyHandler());
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) =>
            DateOnly.ParseExact((string)value, DateFormat, CultureInfo.InvariantCulture);

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString(DateFormat, CultureInfo.InvariantCulture);
        }
    }

    private sealed class TimeOnlyHandler : SqlMapper.TypeHandler<TimeOnly>
    {
        public override TimeOnly Parse(object value) =>
            TimeOnly.ParseExact((string)value, new[] { TimeFormat, "HH:mm:ss" }, CultureInfo.InvariantCulture);

        public override void SetValue(IDbDataParameter parameter, TimeOnly value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }
    }
}
