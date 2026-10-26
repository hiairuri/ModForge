using System;
using Godot;

namespace ArtProbe;

/// <summary>
/// Startup probe that measures which .ctex encodings the game can actually load.
///
/// The game's own textures all embed WebP, which hints that the exported build may not
/// include the PNG unpacker. Rather than guess and risk shipping art that silently
/// fails, each candidate encoding is tested inside the running game.
/// </summary>
public static class Probe
{
    private static readonly (string Label, string Route)[] Variants =
    {
        ("ctex with PNG payload  (data_format=1)", "res://ArtProbe/art/png_payload.ctex"),
        ("ctex with WEBP payload (data_format=2)", "res://ArtProbe/art/webp_payload.ctex"),
        ("png path resolved via .import stub", "res://ArtProbe/art/remapped.png"),
    };

    public static void Run()
    {
        GD.Print("[ArtProbe] === ctex load probe ===");

        foreach ((string label, string route) in Variants)
        {
            Probe_One(label, route);
        }

        GD.Print("[ArtProbe] === probe complete ===");
    }

    private static void Probe_One(string label, string route)
    {
        GD.Print($"[ArtProbe] {label}");
        GD.Print($"[ArtProbe]   path   : {route}");

        bool exists;
        try
        {
            exists = ResourceLoader.Exists(route);
        }
        catch (Exception ex)
        {
            GD.Print($"[ArtProbe]   exists : THREW {ex.Message}");
            return;
        }

        GD.Print($"[ArtProbe]   exists : {exists}");
        if (!exists)
        {
            return;
        }

        try
        {
            var texture = ResourceLoader.Load<Texture2D>(route, null, ResourceLoader.CacheMode.Reuse);
            if (texture is null)
            {
                GD.Print("[ArtProbe]   load   : NULL  (loader rejected the bytes)");
                return;
            }

            GD.Print($"[ArtProbe]   load   : OK  {texture.GetWidth()}x{texture.GetHeight()} " +
                     $"({texture.GetType().Name})");
        }
        catch (Exception ex)
        {
            GD.Print($"[ArtProbe]   load   : THREW {ex.GetType().Name}: {ex.Message}");
        }
    }
}
