using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace Privacy.Services;

internal sealed class GameIconCache : IDisposable
{
    private readonly ITextureProvider textureProvider;
    private readonly IPluginLog log;
    private readonly Dictionary<uint, IDalamudTextureWrap> cache = new();

    public GameIconCache(ITextureProvider textureProvider, IPluginLog log)
    {
        this.textureProvider = textureProvider;
        this.log = log;
    }

    public IDalamudTextureWrap? GetIcon(uint iconId)
    {
        if (iconId == 0) return null;

        if (cache.TryGetValue(iconId, out var cached))
        {
            try
            {
                _ = cached.Handle;
                return cached;
            }
            catch (ObjectDisposedException)
            {
                cache.Remove(iconId);
                try { cached.Dispose(); } catch { }
            }
        }

        try
        {
            var texture = textureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();
            if (texture == null) return null;
            cache[iconId] = texture;
            return texture;
        }
        catch (ObjectDisposedException)
        {
            cache.Remove(iconId);
            return null;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Failed to load game icon {IconId}.", iconId);
            return null;
        }
    }

    public void Remove(uint iconId)
    {
        if (!cache.Remove(iconId, out var texture)) return;
        try { texture.Dispose(); } catch { }
    }

    public void Dispose()
    {
        foreach (var texture in cache.Values)
            texture.Dispose();
        cache.Clear();
    }
}
