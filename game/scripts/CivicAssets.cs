using Godot;

namespace Guild.Game;

/// <summary>Presentation-only raster assets. No game data is derived from artwork.</summary>
public static class CivicAssets
{
    private static readonly Dictionary<string, Texture2D> Textures = new();
    private static readonly Dictionary<string, Texture2D> Weapons = new();

    public static Texture2D? Texture(string name)
    {
        if (Textures.TryGetValue(name, out var cached)) return cached;
        var path = $"res://assets/guild-ui/{name}.png";
        if (!ResourceLoader.Exists(path)) return null;
        var texture = GD.Load<Texture2D>(path);
        if (texture is not null) Textures[name] = texture;
        return texture;
    }

    public static Texture2D? Weapon(string weaponType)
    {
        if (Weapons.TryGetValue(weaponType, out var cached)) return cached;
        var index = weaponType.ToLowerInvariant() switch
        {
            "sword" => 0, "spear" => 1, "bow" => 2, "staff" => 3, "hammer" => 4, _ => -1
        };
        var atlas = Texture("weapon-atlas");
        if (atlas is null || index < 0) return null;
        var width = atlas.GetWidth() / 5f;
        var texture = new AtlasTexture { Atlas = atlas, Region = new Rect2(index * width, atlas.GetHeight() * .14f, width, atlas.GetHeight() * .7f), FilterClip = true };
        Weapons[weaponType] = texture;
        return texture;
    }
}
