namespace SanyTotalWar.Wasm;

using SanyTotalWar.Domain.Entities;

/// <summary>Векторная графика построек — единый источник для досье и карты.</summary>
public static class BuildingArt
{
    public static (string Svg, string Color) For(BuildingType type) => type switch
    {
        BuildingType.ConscriptionOffice => (Svg("c"), "#c6f24e"),
        BuildingType.Fortress => (Svg("f"), "#ff7a6e"),
        BuildingType.Airport => (Svg("a"), "#5bd4e6"),
        _ => (Svg("b"), "#5fc98d"),
    };

    // атрибуты в одинарных кавычках — без экранирования в C#
    private static string Svg(string key)
    {
        var body = key switch
        {
            "c" => "<path d='M6 21V4h1.6v3H15a.8.8 0 0 1 .62 1.3L13.2 11l2.42 2.7A.8.8 0 0 1 15 15H7.6v6H6Z' fill='currentColor'/><path d='M3.5 21h17v1.6h-17z' fill='currentColor' opacity='.55'/>",
            "f" => "<path d='M4 21V10h2V7h1.8v3h3.4V7H13v3h3.4V7H18v3h2v11h-5.5v-4a2.5 2.5 0 0 0-5 0v4H4Z' fill='currentColor'/><path d='M2.5 21h19v1.6h-19z' fill='currentColor' opacity='.55'/>",
            "a" => "<path d='M21 15.5 13.8 11V4.6a1.8 1.8 0 0 0-3.6 0V11L3 15.5v1.9l7.2-2.1v4.6l-2.4 1.6v1.3L12 21.9l4.2.9v-1.3l-2.4-1.6v-4.6l7.2 2.1v-1.9Z' fill='currentColor'/>",
            _ => "<path d='M12 2.5 20 6v5.5c0 5.2-3.4 8.7-8 10-4.6-1.3-8-4.8-8-10V6l8-3.5Z' fill='currentColor' opacity='.92'/><path d='M12 5.2v10.9c2.9-.9 5-2.9 5-6.2V7.3l-5-2.1Z' fill='#0c120c' opacity='.45'/>",
        };
        return $"<svg viewBox='0 0 24 24' xmlns='http://www.w3.org/2000/svg' class='bld-svg bld-svg-{key}'>{body}</svg>";
    }
}
