using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using MenuLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using MenuLib.MonoBehaviors;
using ExitGames.Client.Photon;

namespace REPOLevelEditor
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("nickklmao.menulib", "2.6.0")]
    public class REPOLevelEditorPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.repolabeleditor.level_editor";
        public const string PluginName = "REPO Level Editor";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;

        public static string SelectedRepoLevel = null;

        private LevelEditorManager manager;

        private void Awake()
        {
            Log = Logger;
            var go = new GameObject("REPOLevelEditorManager");
            DontDestroyOnLoad(go);
            manager = go.AddComponent<LevelEditorManager>();

            try
            {
                MenuAPI.AddElementToMainMenu(parent =>
                {
                    MenuAPI.CreateREPOButton("Level Editor", () => manager.EnterEditor(), parent, Vector2.zero);
                });
            }
            catch (Exception e)
            {
                Logger.LogError("Failed to add main menu button: " + e);
            }

            try
            {
                MenuAPI.AddElementToLobbyMenu(parent =>
                {
                    MenuAPI.CreateREPOButton("Save Level to .repo", () => SaveLobbyLevel(), parent, Vector2.zero);
                    MenuAPI.CreateREPOButton("Select Repo Level", () => ShowLevelSelection(), parent, Vector2.zero);
                });
            }
            catch (Exception e)
            {
                Logger.LogError("Failed to add lobby menu button: " + e);
            }
        }

        private void SaveLobbyLevel()
        {
            try
            {
                string dir = GetLevelsDirectory();
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "lobby_level_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".repo");
                LevelData data = CaptureSceneLevel();
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(file, json);
                Log.LogInfo("Saved lobby level to " + file);
                try
                {
                    MenuAPI.OpenPopup("Level Saved", Color.green, "Saved to:\n" + file, () => { }, null);
                }
                catch { }
            }
            catch (Exception e)
            {
                Log.LogError("Lobby save failed: " + e);
            }
        }

        internal static void ShowLevelSelection()
        {
            try
            {
                var page = MenuAPI.CreateREPOPopupPage("Select Repo Level", REPOPopupPage.PresetSide.Left, shouldCachePage: false, pageDimmerVisibility: true, spacing: 1.5f);

                page.AddElement(parent =>
                {
                    MenuAPI.CreateREPOButton("None (procedural)", () =>
                    {
                        SelectedRepoLevel = null;
                        SyncRepoLevel();
                        page.ClosePage(false);
                    }, parent, Vector2.zero);
                });

                string dir = GetLevelsDirectory();
                if (Directory.Exists(dir))
                {
                    foreach (string file in Directory.GetFiles(dir, "*.repo"))
                    {
                        string name = Path.GetFileName(file);
                        page.AddElementToScrollView(scrollView =>
                        {
                            var btn = MenuAPI.CreateREPOButton(name, () =>
                            {
                                SelectedRepoLevel = name;
                                SyncRepoLevel();
                                page.ClosePage(false);
                            }, scrollView, Vector2.zero);
                            return btn.rectTransform;
                        });
                    }
                }

                page.OpenPage(false);
            }
            catch (Exception e)
            {
                Log.LogError("ShowLevelSelection failed: " + e);
            }
        }

        internal static void SyncRepoLevel()
        {
            try
            {
                if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null)
                {
                    var props = new ExitGames.Client.Photon.Hashtable();
                    props["repo_level"] = SelectedRepoLevel ?? "";
                    PhotonNetwork.CurrentRoom.SetCustomProperties(props);
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("SyncRepoLevel failed: " + e.Message);
            }
        }

        internal static string GetLevelsDirectory()
        {
            string baseDir = BepInEx.Paths.ConfigPath;
            if (string.IsNullOrEmpty(baseDir))
            {
                baseDir = Application.persistentDataPath;
            }
            return Path.Combine(baseDir, "REPOLevelEditor", "Levels");
        }

        internal static LevelData CaptureSceneLevel()
        {
            LevelData data = new LevelData();
            data.version = "1.0";

            // Rooms / modules
            foreach (var module in UnityEngine.Object.FindObjectsOfType<Module>())
            {
                if (module == null) continue;
                data.objects.Add(new PlacedObjectData
                {
                    prefabName = module.gameObject.name.Replace("(Clone)", "").Trim(),
                    px = module.transform.position.x,
                    py = module.transform.position.y,
                    pz = module.transform.position.z,
                    rx = module.transform.rotation.eulerAngles.x,
                    ry = module.transform.rotation.eulerAngles.y,
                    rz = module.transform.rotation.eulerAngles.z,
                    sx = module.transform.localScale.x,
                    sy = module.transform.localScale.y,
                    sz = module.transform.localScale.z
                });
            }

            foreach (var vo in UnityEngine.Object.FindObjectsOfType<ValuableObject>())
            {
                if (vo == null) continue;
                data.objects.Add(new PlacedObjectData
                {
                    prefabName = vo.gameObject.name.Replace("(Clone)", "").Trim(),
                    px = vo.transform.position.x,
                    py = vo.transform.position.y,
                    pz = vo.transform.position.z,
                    rx = vo.transform.rotation.eulerAngles.x,
                    ry = vo.transform.rotation.eulerAngles.y,
                    rz = vo.transform.rotation.eulerAngles.z,
                    sx = vo.transform.localScale.x,
                    sy = vo.transform.localScale.y,
                    sz = vo.transform.localScale.z
                });
            }

            foreach (var en in UnityEngine.Object.FindObjectsOfType<Enemy>())
            {
                if (en == null) continue;
                data.objects.Add(new PlacedObjectData
                {
                    prefabName = en.gameObject.name.Replace("(Clone)", "").Trim(),
                    px = en.transform.position.x,
                    py = en.transform.position.y,
                    pz = en.transform.position.z,
                    rx = en.transform.rotation.eulerAngles.x,
                    ry = en.transform.rotation.eulerAngles.y,
                    rz = en.transform.rotation.eulerAngles.z,
                    sx = en.transform.localScale.x,
                    sy = en.transform.localScale.y,
                    sz = en.transform.localScale.z
                });
            }

            foreach (var sp in UnityEngine.Object.FindObjectsOfType<SpawnPoint>())
            {
                if (sp == null) continue;
                data.objects.Add(new PlacedObjectData
                {
                    prefabName = "__SpawnPoint__",
                    px = sp.transform.position.x,
                    py = sp.transform.position.y,
                    pz = sp.transform.position.z,
                    rx = sp.transform.rotation.eulerAngles.x,
                    ry = sp.transform.rotation.eulerAngles.y,
                    rz = sp.transform.rotation.eulerAngles.z,
                    sx = sp.transform.localScale.x,
                    sy = sp.transform.localScale.y,
                    sz = sp.transform.localScale.z
                });
            }

            return data;
        }
    }

    [Serializable]
    public class PlacedObjectData
    {
        public string prefabName;
        public float px, py, pz;
        public float rx, ry, rz;
        public float sx = 1f, sy = 1f, sz = 1f;
    }

    [Serializable]
    public class LevelData
    {
        public string version = "1.0";
        public System.Collections.Generic.List<PlacedObjectData> objects = new System.Collections.Generic.List<PlacedObjectData>();
    }
}
