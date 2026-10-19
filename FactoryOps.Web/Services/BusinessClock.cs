namespace FactoryOps.Web.Services;

public class BusinessClock(TimeProvider time, IConfiguration configuration)
{
    public string ZoneId => configuration["BusinessTimeZone"] ?? "Australia/Sydney";
    public DateTime Today => TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(ZoneId)).Date;
}
