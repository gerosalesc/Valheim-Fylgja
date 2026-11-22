using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace StandsOfValheim
{
    /// <summary>
    /// Loads and caches custom UI icons and textures for Aurvandil's Fylgja.
    /// Supports embedded assembly resources and override files on disk.
    /// </summary>
    internal static class IconManager
    {
        private static Sprite s_aurvandilsFylgjaIcon;
        private static Texture2D s_bossStoneTexture;

        public static Sprite AurvandilsFylgjaIcon
        {
            get
            {
                if (s_aurvandilsFylgjaIcon == null)
                {
                    s_aurvandilsFylgjaIcon = LoadAurvandilsFylgjaIcon();
                }
                return s_aurvandilsFylgjaIcon;
            }
        }

        public static Texture2D BossStoneTexture
        {
            get
            {
                if (s_bossStoneTexture == null)
                {
                    s_bossStoneTexture = LoadTextureResource(
                        "bossstone_aurvandilsfylgja.png",
                        "GP_AurvandilsFylgja_BossStoneTex",
                        new[]
                        {
                            "bossstone_aurvandilsfylgja.png",
                            Path.Combine("resources", "bossstone_aurvandilsfylgja.png")
                        });
                }
                return s_bossStoneTexture;
            }
        }

        private static Sprite LoadAurvandilsFylgjaIcon()
        {
            try
            {
                byte[] pngBytes = null;

                // 1. Check disk in plugins folder
                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dllDir))
                {
                    string[] candidates = {
                        "icon_aurvandilsfylgja.png",
                        "aurvandilsfylgja_icon.png",
                        "aurvandils_fylgja.png",
                        Path.Combine("resources", "icon_aurvandilsfylgja.png")
                    };
                    foreach (var name in candidates)
                    {
                        string path = Path.Combine(dllDir, name);
                        if (File.Exists(path))
                        {
                            pngBytes = File.ReadAllBytes(path);
                            Plugin.Log?.LogInfo($"IconManager: Loaded icon from disk: {path}");
                            break;
                        }
                    }
                }

                // 2. Fallback to embedded resource in DLL
                if (pngBytes == null)
                {
                    var asm = Assembly.GetExecutingAssembly();
                    foreach (var res in asm.GetManifestResourceNames())
                    {
                        if (res.EndsWith("icon_aurvandilsfylgja.png", StringComparison.OrdinalIgnoreCase))
                        {
                            using (var stream = asm.GetManifestResourceStream(res))
                            {
                                if (stream != null)
                                {
                                    pngBytes = new byte[stream.Length];
                                    stream.Read(pngBytes, 0, pngBytes.Length);
                                    Plugin.Log?.LogInfo($"IconManager: Loaded embedded icon: {res}");
                                    break;
                                }
                            }
                        }
                    }
                }

                if (pngBytes != null && pngBytes.Length > 0)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    var method = typeof(ImageConversion).GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]) });
                    if (method != null)
                    {
                        method.Invoke(null, new object[] { tex, pngBytes });
                        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                        sprite.name = "GP_AurvandilsFylgja_Icon";
                        return sprite;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"IconManager: Failed to load icon: {ex.Message}");
            }

            return null;
        }

        internal static Texture2D LoadTextureResource(string embeddedSuffix, string texName, string[] diskCandidates)
        {
            try
            {
                byte[] pngBytes = null;

                string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dllDir) && diskCandidates != null)
                {
                    foreach (var name in diskCandidates)
                    {
                        string path = Path.Combine(dllDir, name);
                        if (File.Exists(path))
                        {
                            pngBytes = File.ReadAllBytes(path);
                            Plugin.Log?.LogInfo($"IconManager: Loaded texture from disk: {path}");
                            break;
                        }
                    }
                }

                if (pngBytes == null)
                {
                    var asm = Assembly.GetExecutingAssembly();
                    foreach (var res in asm.GetManifestResourceNames())
                    {
                        if (res.EndsWith(embeddedSuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            using (var stream = asm.GetManifestResourceStream(res))
                            {
                                if (stream != null)
                                {
                                    pngBytes = new byte[stream.Length];
                                    stream.Read(pngBytes, 0, pngBytes.Length);
                                    Plugin.Log?.LogInfo($"IconManager: Loaded embedded texture: {res}");
                                    break;
                                }
                            }
                        }
                    }
                }

                if (pngBytes != null && pngBytes.Length > 0)
                {
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    var method = typeof(ImageConversion).GetMethod("LoadImage", new Type[] { typeof(Texture2D), typeof(byte[]) });
                    if (method != null)
                    {
                        method.Invoke(null, new object[] { tex, pngBytes });
                        tex.name = texName;
                        tex.wrapMode = TextureWrapMode.Clamp;
                        tex.filterMode = FilterMode.Bilinear;
                        return tex;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"IconManager: Failed to load texture '{embeddedSuffix}': {ex.Message}");
            }

            return null;
        }
    }
}
