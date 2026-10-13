using System;
using System.Collections.Generic;
using BepInEx;
using UnityEngine;

namespace MyPeakMod
{
    [BepInPlugin("com.shahar.mypeakmod", "MyPeakMod", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private readonly List<TrailRenderer> playerTrails = new List<TrailRenderer>();
        private float scanTimer = 0f;

        private void Awake()
        {
            Logger.LogInfo("[MyPeakMod] Mod initialized successfully!");
        }

        private void Update()
        {
            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = 2f;
                ScanForPlayers();
            }

            if (!Input.GetKeyDown(KeyCode.K) || playerTrails.Count == 0)
                return;

            var enableTrails = false;
            foreach (var trail in playerTrails)
            {
                if (!trail.enabled) { enableTrails = true; break; }
            }
            foreach (var trail in playerTrails)
                trail.enabled = enableTrails;

            Logger.LogInfo("[MyPeakMod] Trails " + (enableTrails ? "enabled" : "disabled"));
        }

        private void ScanForPlayers()
        {
            try
            {
                playerTrails.RemoveAll(t => t == null);
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
                        || playerName.Contains("MainCamera", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var isPlayer = playerObject.GetComponent<CharacterController>() != null
                        || playerName.Contains("Player", StringComparison.OrdinalIgnoreCase);
                    if (!isPlayer)
                        continue;

                    var anchor = playerObject.transform.Find("ModTrailAnchor");
                    TrailRenderer trail = anchor != null ? anchor.GetComponent<TrailRenderer>() : null;

                    if (trail == null)
                    {
                        var anchorGo = new GameObject("ModTrailAnchor");
                        anchorGo.transform.SetParent(playerObject.transform, false);
                        anchorGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);

                        trail = anchorGo.AddComponent<TrailRenderer>();
                        trail.time = 3f;
                        trail.startWidth = 0.3f;
                        trail.endWidth = 0f;
                        trail.minVertexDistance = 0.1f;
                        trail.emitting = true;

                        var gradient = new Gradient();
                        gradient.SetKeys(
                            new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.yellow, 1f) },
                            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                        trail.colorGradient = gradient;

                        var shader = Shader.Find("Sprites/Default");
                        if (shader != null)
                            trail.material = new Material(shader);

                        trail.enabled = false;
                        Logger.LogInfo("[MyPeakMod] Trail attached to: " + playerObject.name);
                    }

                    if (!playerTrails.Contains(trail))
                        playerTrails.Add(trail);
                }
            }
            catch (Exception exception)
            {
                Logger.LogWarning("Trail scan failed safely: " + exception.Message);
            }
        }
    }
}
