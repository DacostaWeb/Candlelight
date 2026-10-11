namespace Candlelight.Engine;

public sealed record SolarLocation(
    string Name,
    double Latitude,
    double Longitude,
    string TimeZoneId
)
{
    public SolarLocation Validate()
    {
        if (
            string.IsNullOrWhiteSpace(Name)
            || Name.Length > 120
            || !double.IsFinite(Latitude)
            || Latitude is < -90 or > 90
            || !double.IsFinite(Longitude)
            || Longitude is < -180 or > 180
        )
            throw new ArgumentException("Localização inválida.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        return this;
    }
}

public sealed record SolarDay(DateTimeOffset? Sunrise, DateTimeOffset? Sunset, bool PolarDay);

/// <summary>NOAA fractional-year equations, apparent horizon 90.833 degrees. Entirely offline.</summary>
public static class SolarTimes
{
    public static SolarDay Calculate(DateOnly date, SolarLocation location)
    {
        location.Validate();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(location.TimeZoneId);
        var gamma =
            2 * Math.PI / (DateTime.IsLeapYear(date.Year) ? 366 : 365) * (date.DayOfYear - 1);
        var equation =
            229.18
            * (
                0.000075
                + 0.001868 * Math.Cos(gamma)
                - 0.032077 * Math.Sin(gamma)
                - 0.014615 * Math.Cos(2 * gamma)
                - 0.040849 * Math.Sin(2 * gamma)
            );
        var declination =
            0.006918
            - 0.399912 * Math.Cos(gamma)
            + 0.070257 * Math.Sin(gamma)
            - 0.006758 * Math.Cos(2 * gamma)
            + 0.000907 * Math.Sin(2 * gamma)
            - 0.002697 * Math.Cos(3 * gamma)
            + 0.00148 * Math.Sin(3 * gamma);
        var latitude = location.Latitude * Math.PI / 180;
        var cosine =
            (Math.Cos(90.833 * Math.PI / 180) - Math.Sin(latitude) * Math.Sin(declination))
            / (Math.Cos(latitude) * Math.Cos(declination));
        if (cosine is < -1 or > 1)
            return new(null, null, cosine < -1);
        var angle = Math.Acos(Math.Clamp(cosine, -1, 1)) * 180 / Math.PI;
        var noon = 720 - 4 * location.Longitude - equation;
        var midnight = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        return new(
            TimeZoneInfo.ConvertTime(midnight.AddMinutes(noon - 4 * angle), zone),
            TimeZoneInfo.ConvertTime(midnight.AddMinutes(noon + 4 * angle), zone),
            false
        );
    }

    public static DateTimeOffset AtLocalTime(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        // A missing spring-forward time runs at the first valid local minute.
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        // The repeated autumn hour runs once, at its first occurrence.
        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new(local, offset);
    }
}
