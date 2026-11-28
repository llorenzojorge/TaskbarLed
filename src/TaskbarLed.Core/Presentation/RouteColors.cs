namespace TaskbarLed.Core.Presentation;

public static class RouteColors
{
    public static string For(string letter) => letter switch
    {
        "A" => "#A63831",
        "B" => "#23558B",
        "C" => "#146B74",
        "D" => "#326343",
        "E" => "#835789",
        "F" => "#935013",
        "G" => "#534372",
        _ => "#465D6B",
    };
}
