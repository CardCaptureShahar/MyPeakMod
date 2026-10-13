#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace MyPeakMod
{
    [BepInPlugin("com.shahar.mypeakmod", "MyPeakMod", "1.0.5")]
    public class Plugin : BaseUnityPlugin
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<TrailRenderer> playerTrails = new List<TrailRenderer>();
        private readonly Dictionary<TrailRenderer, Transform> targets = new Dictionary<TrailRenderer, Transform>();
        private readonly Dictionary<TrailRenderer, Material> coloredMaterials = new Dictionary<TrailRenderer, Material>();
        private Material trailMaterial;
        private bool trailsEnabled;
        private float scanTimer;
        private float diagnosticTimer;

        private void Awake()
        {
            Logger.LogInfo("[MyPeakMod] Mod initialized successfully! Trail diagnostic build 1.0.5");
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            Logger.LogInfo("[MyPeakMod] Active render pipeline: "
                + (pipeline == null ? "null (Built-in RP)" : pipeline.GetType().FullName));
        }

        private void Update()
        {
            RemoveDestroyedTargets();
            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = 2f;
                ScanForPlayers();
            }

            if (Input.GetKeyDown(KeyCode.K))
            {
                trailsEnabled = !trailsEnabled;
                foreach (var trail in playerTrails)
                {
                    trail.Clear();
                    trail.enabled = trailsEnabled;
                    trail.emitting = trailsEnabled;
                }
                Logger.LogInfo("[MyPeakMod] Trails " + (trailsEnabled ? "enabled" : "disabled")
                    + " (attached: " + playerTrails.Count + ")");
            }

            diagnosticTimer -= Time.deltaTime;
            if (trailsEnabled && diagnosticTimer <= 0f)
            {
                diagnosticTimer = 5f;
                var camera = GetWorldCamera();
                foreach (var trail in playerTrails)
                {
                    var material = trail.sharedMaterial;
                    Logger.LogInfo("[MyPeakMod] Trail diagnostic: target=" + targets[trail].name
                        + ", anchorScale=" + trail.transform.lossyScale
                        + ", targetScale=" + targets[trail].lossyScale
                        + ", position=" + trail.transform.position
                        + ", points=" + trail.positionCount
                        + ", enabled=" + trail.enabled + ", emitting=" + trail.emitting
                        + ", layer=" + trail.gameObject.layer
                        + ", camera=" + (camera != null ? camera.name : "NONE")
                        + ", cameraIncludesLayer=" + (camera != null && (camera.cullingMask & (1 << trail.gameObject.layer)) != 0)
                        + ", material=" + (material != null ? material.name : "NONE")
                        + ", shader=" + (material != null && material.shader != null ? material.shader.name : "NONE"));
                }
            }
        }

        private void ScanForPlayers()
        {
            try
            {
                var gameObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                foreach (var playerObject in gameObjects)
                {
                    if (playerObject == null || !playerObject.activeInHierarchy)
                        continue;
                    var playerName = playerObject.name;
                    if (playerName.Contains("Canvas", StringComparison.OrdinalIgnoreCase)
                        || playerName.Contains("Voice", StringComparison.OrdinalIgnoreCase)
                        || playerName.Contains("Chest", StringComparison.OrdinalIgnoreCase)
                        || playerName.Contains("UI", StringComparison.OrdinalIgnoreCase)
                        || playerName.Contains("MainCamera", StringComparison.OrdinalIgnoreCase)
                        || playerName == "ModTrailAnchor")
                        continue;

                    // Only a real PEAK Character with an animated movement transform qualifies.
                    var character = FindCharacter(playerObject);
                    if (character == null) continue;
                    var motionTransform = GetAnimatedTransform(character);
                    if (motionTransform == null) continue;
                    var alreadyAttached = false;
                    foreach (var target in targets.Values)
                        if (target == motionTransform) { alreadyAttached = true; break; }
                    if (alreadyAttached) continue;
                    if (!TryCreateMaterial())
                        continue; // Retry on the next scan after scene materials load.

                    // Scene-root anchor: no inherited rig scale or rotation.
                    var anchorGo = new GameObject("ModTrailAnchor");
                    var anchor = anchorGo.transform;
                    anchor.position = motionTransform.position + Vector3.up * 0.05f;
                    anchor.rotation = Quaternion.identity;
                    anchor.localScale = Vector3.one;
                    var trail = anchorGo.AddComponent<TrailRenderer>();

                    var playerMaterial = new Material(trailMaterial);
                    coloredMaterials.Add(trail, playerMaterial);
                    trail.sharedMaterial = playerMaterial;
                    trail.time = 15f;
                    trail.startWidth = 0.3f;
                    trail.endWidth = 0.05f;
                    trail.widthMultiplier = 1f;
                    trail.minVertexDistance = 0.05f;
                    trail.alignment = LineAlignment.View;
                    trail.autodestruct = false;
                    trail.allowOcclusionWhenDynamic = false;
                    trail.generateLightingData = true;
                    string colorSource;
                    var playerColor = GetPlayerColor(character, playerName, out colorSource);
                    ApplyPlayerColor(trail, playerMaterial, playerColor);
                    Logger.LogInfo("[MyPeakMod] Trail color for " + playerName + ": " + playerColor
                        + "; source=" + colorSource);
                    trail.Clear();
                    trail.enabled = trailsEnabled;
                    trail.emitting = true;
                    UpdateCameraLayer(anchor.gameObject);
                    playerTrails.Add(trail);
                    targets.Add(trail, motionTransform);
                    Logger.LogInfo("[MyPeakMod] Trail attached to: " + playerName);
                    Logger.LogInfo("[MyPeakMod] Trail anchor world position: " + anchor.position
                        + "; motion transform=" + motionTransform.name
                        + "; target.lossyScale=" + motionTransform.lossyScale
                        + "; anchor.lossyScale=" + anchor.lossyScale
                        + "; material=" + trailMaterial.name + "; shader=" + trailMaterial.shader.name);
                }
            }
            catch (Exception exception)
            {
                Logger.LogWarning("Trail scan failed safely: " + exception);
            }
        }

        private void LateUpdate()
        {
            RemoveDestroyedTargets();
            foreach (var trail in playerTrails)
            {
                var target = targets[trail];
                trail.transform.position = target.position + Vector3.up * 0.05f;
                // Keep the anchor independent even if the target's rig changes scale/rotation.
                trail.transform.rotation = Quaternion.identity;
                trail.transform.localScale = Vector3.one;
                UpdateCameraLayer(trail.gameObject);
                var active = trailsEnabled && target.gameObject.activeInHierarchy;
                trail.enabled = active;
                trail.emitting = active;
            }
        }

        private void RemoveDestroyedTargets()
        {
            for (int i = playerTrails.Count - 1; i >= 0; i--)
            {
                var trail = playerTrails[i];
                Transform target;
                if (trail != null && targets.TryGetValue(trail, out target) && target != null)
                    continue;
                // Destroy scene-root anchors explicitly; they cannot die with a character parent.
                if (trail != null) UnityEngine.Object.Destroy(trail.gameObject);
                if (!ReferenceEquals(trail, null))
                {
                    Material material;
                    if (coloredMaterials.TryGetValue(trail, out material))
                    {
                        if (material != null) UnityEngine.Object.Destroy(material);
                        coloredMaterials.Remove(trail);
                    }
                    targets.Remove(trail);
                }
                playerTrails.RemoveAt(i);
            }
        }

        private static Component FindCharacter(GameObject player)
        {
            foreach (var component in player.GetComponents<Component>())
                if (component != null && component.GetType().Name == "Character")
                    return component;
            return null;
        }

        private static object ReadMember(object instance, string name)
        {
            if (instance == null) return null;
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, Members | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(instance);
                var property = type.GetProperty(name, Members | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(instance);
            }
            return null;
        }

        private static Transform GetAnimatedTransform(Component character)
        {
            var value = ReadMember(ReadMember(character, "refs"), "animationPositionTransform");
            if (value is Transform transform) return transform;
            if (value is Component component) return component.transform;
            if (value is GameObject gameObject) return gameObject.transform;
            return null;
        }

        private static Color GetPlayerColor(Component character, string identity, out string source)
        {
            // PEAKTrails uses Character.refs.customization.PlayerColor.
            // Reflection keeps this project independent of Assembly-CSharp references.
            try
            {
                var customization = ReadMember(ReadMember(character, "refs"), "customization");
                var value = ReadMember(customization, "PlayerColor");
                if (value is Color color && !float.IsNaN(color.r) && !float.IsNaN(color.g)
                    && !float.IsNaN(color.b) && !float.IsInfinity(color.r)
                    && !float.IsInfinity(color.g) && !float.IsInfinity(color.b))
                {
                    source = "Character.refs.customization.PlayerColor";
                    return new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), 1f);
                }
            }
            catch (Exception) { } // A different game version can still use the identity fallback.

            // Stable FNV-1a, not string.GetHashCode (which can vary across processes).
            uint hash = 2166136261u;
            unchecked
            {
                foreach (char c in identity ?? "Unknown Character")
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
            }
            source = "deterministic character-name hash: " + identity;
            return Color.HSVToRGB((hash % 3600u) / 3600f, 0.9f, 1f);
        }

        private static void ApplyPlayerColor(TrailRenderer trail, Material material, Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.15f, 1f) });
            trail.colorGradient = gradient;

            // General URP Unlit/Lit can ignore trail vertex colors, so tint their own material.
            // Particle/sprite shaders consume vertex color: keep their material white to avoid tinting twice.
            var shaderName = material.shader.name;
            bool usesVertexColor = shaderName.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0
                || shaderName.IndexOf("Sprite", StringComparison.OrdinalIgnoreCase) >= 0;
            var tint = usesVertexColor ? Color.white : color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", tint);
        }

        private bool TryCreateMaterial()
        {
            if (trailMaterial != null) return true;
            // Diagnostic priority order. isSupported checks GPU/fallback support, not pipeline compatibility.
            var names = new[] { "Universal Render Pipeline/Particles/Unlit",
                "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Simple Lit",
                "Sprites/Default", "Legacy Shaders/Particles/Additive" };
            foreach (var name in names)
            {
                var shader = Shader.Find(name);
                Logger.LogInfo("[MyPeakMod] Shader candidate: " + name
                    + "; found=" + (shader != null)
                    + "; isSupported=" + (shader != null && shader.isSupported));
                if (shader == null || !shader.isSupported) continue;
                trailMaterial = new Material(shader);
                PrepareMaterial(trailMaterial);
                Logger.LogInfo("[MyPeakMod] Using trail shader: " + shader.name);
                return true;
            }

            // Clone an actual loaded material, never modify a game object's shared material.
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            Material fallback = null;
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null || !material.shader.isSupported
                        || material.shader.name.StartsWith("Hidden/", StringComparison.Ordinal)) continue;
                    if (fallback == null) fallback = material;
                    var name = material.shader.name.ToLowerInvariant();
                    if (renderer is TrailRenderer || renderer is LineRenderer
                        || name.Contains("particle") || name.Contains("unlit") || name.Contains("sprite"))
                    {
                        fallback = material;
                        goto Found;
                    }
                }
            }
        Found:
            if (fallback == null)
            {
                Logger.LogWarning("[MyPeakMod] No supported trail shader or scene material available. Retrying in 2 seconds.");
                return false;
            }
            trailMaterial = new Material(fallback);
            PrepareMaterial(trailMaterial);
            Logger.LogWarning("[MyPeakMod] Using cloned scene material shader: " + trailMaterial.shader.name
                + ". This fallback is diagnostic, not guaranteed to support trail vertex colors.");
            return true;
        }

        private static void PrepareMaterial(Material material)
        {
            material.name = "MyPeakMod_TrailMaterial";
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", Color.white);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", Texture2D.whiteTexture);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (material.HasProperty("_Cull")) material.SetInt("_Cull", 0);
            if (material.HasProperty("_Surface"))
            {
                // _Surface alone does not configure runtime blending or shader keywords.
                material.SetFloat("_Surface", 1f);
                if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
                if (material.HasProperty("_SrcBlend")) material.SetInt("_SrcBlend", 5); // SrcAlpha
                if (material.HasProperty("_DstBlend")) material.SetInt("_DstBlend", 10); // OneMinusSrcAlpha
                if (material.HasProperty("_SrcBlendAlpha")) material.SetInt("_SrcBlendAlpha", 1);
                if (material.HasProperty("_DstBlendAlpha")) material.SetInt("_DstBlendAlpha", 10);
                if (material.HasProperty("_ZWrite")) material.SetInt("_ZWrite", 0);
                if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.DisableKeyword("_ALPHAMODULATE_ON");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = 3000;
            }
            if (material.shader != null && material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)
                && material.shader.name.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0)
                material.renderQueue = 3000;
        }

        private static Camera GetWorldCamera()
        {
            var main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;
            foreach (var camera in Camera.allCameras)
                if (camera != null && camera.isActiveAndEnabled && camera.targetTexture == null
                    && camera.cullingMask != 0) return camera;
            return null;
        }

        private static void UpdateCameraLayer(GameObject anchor)
        {
            var camera = GetWorldCamera();
            if (camera == null) return;
            if ((camera.cullingMask & (1 << anchor.layer)) != 0) return;
            // Change only the mod anchor, never the game's player layers or camera mask.
            for (int layer = 0; layer < 32; layer++)
                if ((camera.cullingMask & (1 << layer)) != 0)
                {
                    anchor.layer = layer;
                    return;
                }
        }

        private void OnDestroy()
        {
            foreach (var trail in playerTrails)
                if (trail != null) UnityEngine.Object.Destroy(trail.gameObject);
            foreach (var material in coloredMaterials.Values)
                if (material != null) UnityEngine.Object.Destroy(material);
            coloredMaterials.Clear();
            if (trailMaterial != null) UnityEngine.Object.Destroy(trailMaterial);
        }
    }
}
