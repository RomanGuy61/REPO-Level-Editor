using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;

namespace REPOLevelEditor
{
    public class LevelEditorManager : MonoBehaviour
    {
        public static LevelEditorManager Instance;

        public bool Active { get; private set; }
        private string previousSceneName;
        private Scene editorScene;

        // scene objects
        private Camera cam;
        private GameObject ground;
        private GameObject lightGO;
        private GameObject contentRoot;

        // camera control
        private float yaw = 0f;
        private float pitch = 30f;
        private Vector3 camPosition = new Vector3(0f, 12f, -18f);

        // palette
        private bool scanned = false;
        private readonly List<PrefabRef> roomRefs = new List<PrefabRef>();
        private readonly List<PrefabRef> valuableRefs = new List<PrefabRef>();
        private readonly List<PrefabRef> monsterRefs = new List<PrefabRef>();
        private readonly Dictionary<string, PrefabRef> prefabRefByName = new Dictionary<string, PrefabRef>();

        private int categoryIndex = 0; // 0 Rooms 1 Valuables 2 Monsters 3 Spawns 4 All
        private int selectedPalette = -1;
        private List<PrefabRef> currentPalette = new List<PrefabRef>();

        // placed
        private readonly List<PlacedHandle> placed = new List<PlacedHandle>();
        private int selectedPlaced = -1;

        private int gizmoMode = 0; // 0 Move 1 Rotate 2 Scale
        private GameObject gizmoRoot;
        private bool isDraggingGizmo = false;
        private int dragAxis = -1;
        private Vector3 dragStartObjectPos;
        private Quaternion dragStartObjectRot;
        private Vector3 dragStartObjectScale;
        private Vector3 dragStartWorldPoint;
        private Vector3 dragStartVector;
        private Plane dragPlane;

        // tool
        private int toolIndex = 0; // 0 Place 1 Select 2 Delete

        // UI state
        private Rect windowRect = new Rect(10, 10, 360, 520);
        private Rect saveRect = new Rect(10, 540, 360, 180);
        private string fileName = "my_level";
        private string status = "";
        private Vector2 paletteScroll;
        private bool pointerOverUI = false;

        private class PlacedHandle
        {
            public GameObject go;
            public string prefabName;
        }

        private void Awake()
        {
            Instance = this;
            REPOLevelEditorPlugin.Log.LogInfo("LevelEditorManager Awake");
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Reload")
            {
                StartCoroutine(PlayLevelRoutine());
            }
        }

        public void EnterEditor()
        {
            if (Active) return;
            Active = true;
            REPOLevelEditorPlugin.Log.LogInfo("Entering editor...");
            StartCoroutine(EnterRoutine());
        }

        public void ExitEditor()
        {
            if (!Active) return;
            Active = false;
            SceneManager.LoadScene(previousSceneName, LoadSceneMode.Single);
        }

        private IEnumerator EnterRoutine()
        {
            previousSceneName = SceneManager.GetActiveScene().name;
            REPOLevelEditorPlugin.Log.LogInfo("Prev scene: " + previousSceneName);
            editorScene = SceneManager.CreateScene("REPO Level Editor");
            SceneManager.SetActiveScene(editorScene);
            REPOLevelEditorPlugin.Log.LogInfo("Created scene");

            if (SceneManager.GetSceneByName(previousSceneName).isLoaded)
            {
                REPOLevelEditorPlugin.Log.LogInfo("Unloading prev");
                yield return SceneManager.UnloadSceneAsync(previousSceneName);
                REPOLevelEditorPlugin.Log.LogInfo("Unloaded prev");
            }

            SetupScene();
            REPOLevelEditorPlugin.Log.LogInfo("Setup done");

            if (!scanned)
            {
                REPOLevelEditorPlugin.Log.LogInfo("Scan start t=" + Time.realtimeSinceStartup);
                ScanResources();
                scanned = true;
                REPOLevelEditorPlugin.Log.LogInfo("Scan end t=" + Time.realtimeSinceStartup);
            }

            RefreshPalette();
            status = "Editor ready. WASD move, RMB look, LMB place/select.";
            REPOLevelEditorPlugin.Log.LogInfo(status);
        }

        private void SetupScene()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // camera
            GameObject camGO = new GameObject("Editor Camera");
            cam = camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            camGO.transform.position = camPosition;
            camGO.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            // light
            lightGO = new GameObject("Editor Light");
            Light l = lightGO.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // content root
            contentRoot = new GameObject("Editor Content");

            // ground
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Editor Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            var rend = ground.GetComponent<Renderer>();
            if (rend != null && rend.material != null)
            {
                rend.material.color = new Color(0.35f, 0.35f, 0.4f);
            }
        }

        private void ScanResources()
        {
            roomRefs.Clear();
            valuableRefs.Clear();
            monsterRefs.Clear();
            prefabRefByName.Clear();

            HashSet<string> seen = new HashSet<string>();

            try
            {
                // Rooms from game levels (fast, no Resources.LoadAll)
                if (RunManager.instance != null && RunManager.instance.levels != null)
                {
                    foreach (var level in RunManager.instance.levels)
                    {
                        if (level == null) continue;
                        AddModuleRefs(level.StartRooms, seen, roomRefs);
                        AddModuleRefs(level.ModulesNormal1, seen, roomRefs);
                        AddModuleRefs(level.ModulesNormal2, seen, roomRefs);
                        AddModuleRefs(level.ModulesNormal3, seen, roomRefs);
                        AddModuleRefs(level.ModulesPassage1, seen, roomRefs);
                        AddModuleRefs(level.ModulesPassage2, seen, roomRefs);
                        AddModuleRefs(level.ModulesPassage3, seen, roomRefs);
                        AddModuleRefs(level.ModulesDeadEnd1, seen, roomRefs);
                        AddModuleRefs(level.ModulesDeadEnd2, seen, roomRefs);
                        AddModuleRefs(level.ModulesDeadEnd3, seen, roomRefs);
                        AddModuleRefs(level.ModulesExtraction1, seen, roomRefs);
                        AddModuleRefs(level.ModulesExtraction2, seen, roomRefs);
                        AddModuleRefs(level.ModulesExtraction3, seen, roomRefs);
                        AddModuleRefs(level.ModulesMega1, seen, roomRefs);
                        AddModuleRefs(level.ModulesMega2, seen, roomRefs);
                        AddModuleRefs(level.ModulesMega3, seen, roomRefs);
                        AddModuleRefs(level.ModulesSpecial, seen, roomRefs);

                        if (level.ValuablePresets != null)
                        {
                            foreach (var vp in level.ValuablePresets)
                            {
                                if (vp == null) continue;
                                AddValuableRefs(vp.tiny, seen, valuableRefs);
                                AddValuableRefs(vp.small, seen, valuableRefs);
                                AddValuableRefs(vp.medium, seen, valuableRefs);
                                AddValuableRefs(vp.big, seen, valuableRefs);
                                AddValuableRefs(vp.wide, seen, valuableRefs);
                                AddValuableRefs(vp.tall, seen, valuableRefs);
                                AddValuableRefs(vp.veryTall, seen, valuableRefs);
                            }
                        }
                    }
                }

                // Monsters from EnemyDirector
                if (EnemyDirector.instance != null)
                {
                    REPOLevelEditorPlugin.Log.LogInfo("diff1=" + (EnemyDirector.instance.enemiesDifficulty1?.Count ?? -1) + " diff2=" + (EnemyDirector.instance.enemiesDifficulty2?.Count ?? -1) + " diff3=" + (EnemyDirector.instance.enemiesDifficulty3?.Count ?? -1));
                    try
                    {
                        var lists = new List<List<EnemySetup>>();
                        if (EnemyDirector.instance.enemiesDifficulty1 != null) lists.Add(EnemyDirector.instance.enemiesDifficulty1);
                        if (EnemyDirector.instance.enemiesDifficulty2 != null) lists.Add(EnemyDirector.instance.enemiesDifficulty2);
                        if (EnemyDirector.instance.enemiesDifficulty3 != null) lists.Add(EnemyDirector.instance.enemiesDifficulty3);
                        foreach (var list in lists)
                        {
                            foreach (var enemy in list)
                            {
                                if (enemy == null || enemy.spawnObjects == null) continue;
                                foreach (var po in enemy.spawnObjects)
                                {
                                    if (po == null || !po.IsValid()) continue;
                                    if (seen.Add("M:" + po.PrefabName))
                                    {
                                        monsterRefs.Add(po);
                                        prefabRefByName[po.PrefabName] = po;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        REPOLevelEditorPlugin.Log.LogWarning("EnemyDirector scan failed: " + e.Message);
                    }
                }

                // Fallback: if nothing found, use Resources scan (may be slow)
                if (roomRefs.Count == 0 && valuableRefs.Count == 0 && monsterRefs.Count == 0)
                {
                    REPOLevelEditorPlugin.Log.LogWarning("No refs from managers, falling back to Resources scan");
                    foreach (var m in Resources.LoadAll<Module>(""))
                    {
                        if (m == null) continue;
                        var go = m.gameObject;
                        if (go == null) continue;
                        var pr = new PrefabRef();
                        // cannot create PrefabRef easily; skip
                    }
                }

                // Hardcoded monster prefab names (avoids slow Resources scan)
                string[] monsterNames = new string[] {
                    "Enemy - Animal","Enemy - Bang","Enemy - Bang Director","Enemy - Beamer",
                    "Enemy - Birthday boy","Enemy - Bomb Thrower","Enemy - Bowtie","Enemy - Ceiling Eye",
                    "Enemy - Duck","Enemy - Elsa","Enemy - Floater","Enemy - Gnome","Enemy - Gnome Director",
                    "Enemy - Head","Enemy - Head Grabber","Enemy - Heart Hugger","Enemy - Hidden",
                    "Enemy - Hunter","Enemy - Oogly","Enemy - Robe","Enemy - Runner","Enemy - Shadow",
                    "Enemy - Slow Mouth","Enemy - Slow Walker","Enemy - Snatcher","Enemy - Spinny",
                    "Enemy - Thin Man","Enemy - Tick","Enemy - Tick Heal Aura","Enemy - Tricycle",
                    "Enemy - Tumbler","Enemy - Upscream","Enemy - Valuable Thrower","Enemy - Virus"
                };
                foreach (var mn in monsterNames)
                {
                    if (seen.Add("M:" + mn))
                    {
                        var pr = CreateNameRef(mn);
                        monsterRefs.Add(pr);
                        prefabRefByName[mn] = pr;
                    }
                }

                status = "Scanned: " + roomRefs.Count + " rooms, " + valuableRefs.Count + " valuables, " + monsterRefs.Count + " monsters.";
                REPOLevelEditorPlugin.Log.LogInfo(status);
            }
            catch (Exception e)
            {
                status = "Scan failed: " + e.Message;
                REPOLevelEditorPlugin.Log.LogError(e);
            }
        }

        private IEnumerator PlayLevelRoutine()
        {
            // sync selection from room property
            try
            {
                if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey("repo_level"))
                {
                    REPOLevelEditorPlugin.SelectedRepoLevel = PhotonNetwork.CurrentRoom.CustomProperties["repo_level"] as string;
                }
            }
            catch { }

            string file = REPOLevelEditorPlugin.SelectedRepoLevel;
            if (string.IsNullOrEmpty(file)) yield break;

            float timeout = 60f;
            while (timeout > 0f && (LevelGenerator.Instance == null || !LevelGenerator.Instance.Generated))
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            if (LevelGenerator.Instance == null || !LevelGenerator.Instance.Generated) yield break;

            string path = Path.Combine(REPOLevelEditorPlugin.GetLevelsDirectory(), file);
            if (!File.Exists(path))
            {
                if (File.Exists(file)) path = file;
                else yield break;
            }

            string json = File.ReadAllText(path);
            LevelData data = JsonUtility.FromJson<LevelData>(json);
            if (data == null || data.objects == null) yield break;

            // clear generated
            foreach (var m in FindObjectsOfType<Module>()) if (m != null && m.gameObject != null) Destroy(m.gameObject);
            foreach (var v in FindObjectsOfType<ValuableObject>()) if (v != null && v.gameObject != null) Destroy(v.gameObject);
            foreach (var e in FindObjectsOfType<Enemy>()) if (e != null && e.gameObject != null) Destroy(e.gameObject);
            yield return null;

            Transform parent = (LevelGenerator.Instance.LevelParent != null) ? LevelGenerator.Instance.LevelParent.transform : null;

            foreach (var od in data.objects)
            {
                if (od.prefabName == "__SpawnPoint__")
                {
                    GameObject marker = new GameObject("SpawnPoint");
                    marker.AddComponent<SpawnPoint>();
                    var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    sphere.transform.SetParent(marker.transform, false);
                    sphere.transform.localScale = Vector3.one * 0.5f;
                    var r = sphere.GetComponent<Renderer>();
                    if (r != null) r.material.color = Color.yellow;
                    Destroy(sphere.GetComponent<Collider>());
                    if (parent != null) marker.transform.SetParent(parent, true);
                    marker.transform.position = new Vector3(od.px, od.py, od.pz);
                    marker.transform.rotation = Quaternion.Euler(od.rx, od.ry, od.rz);
                    marker.transform.localScale = new Vector3(od.sx, od.sy, od.sz);
                }
                else
                {
                    GameObject prefab = Resources.Load<GameObject>(od.prefabName);
                    if (prefab == null) continue;
                    GameObject inst = Instantiate(prefab, new Vector3(od.px, od.py, od.pz), Quaternion.Euler(od.rx, od.ry, od.rz));
                    inst.name = od.prefabName + "(Clone)";
                    if (parent != null) inst.transform.SetParent(parent, true);
                    inst.transform.localScale = new Vector3(od.sx, od.sy, od.sz);
                }
            }

            REPOLevelEditorPlugin.Log.LogInfo("Played repo level: " + file);
        }

        private void AddModuleRefs(List<PrefabRef> list, HashSet<string> seen, List<PrefabRef> target)
        {
            if (list == null) return;
            foreach (var pr in list)
            {
                if (pr == null || !pr.IsValid()) continue;
                if (seen.Add("R:" + pr.PrefabName))
                {
                    target.Add(pr);
                    prefabRefByName[pr.PrefabName] = pr;
                }
            }
        }

        private PrefabRef CreateNameRef(string name)
        {
            var pr = new PrefabRef();
            var t = typeof(PrefabRef).BaseType;
            var f1 = t.GetField("prefabName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var f2 = t.GetField("resourcePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (f1 != null) f1.SetValue(pr, name);
            if (f2 != null) f2.SetValue(pr, name);
            return pr;
        }

        private void AddValuableRefs(List<PrefabRef> list, HashSet<string> seen, List<PrefabRef> target)
        {
            if (list == null) return;
            foreach (var pr in list)
            {
                if (pr == null || !pr.IsValid()) continue;
                if (seen.Add("V:" + pr.PrefabName))
                {
                    target.Add(pr);
                    prefabRefByName[pr.PrefabName] = pr;
                }
            }
        }

        private void Update()
        {
            if (!Active)
            {
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    EnterEditor();
                }
                return;
            }

            // keep cursor usable
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // follow selected object with gizmo
            if (selectedPlaced >= 0 && selectedPlaced < placed.Count && placed[selectedPlaced].go != null)
            {
                if (gizmoRoot == null) CreateGizmo();
                gizmoRoot.transform.position = placed[selectedPlaced].go.transform.position;
            }
            else
            {
                DestroyGizmo();
            }

            HandleCamera();
            HandleInput();
        }

        private void HandleCamera()
        {
            if (cam == null) return;

            // look
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * 3f;
                pitch -= Input.GetAxis("Mouse Y") * 3f;
                pitch = Mathf.Clamp(pitch, -80f, 80f);
            }

            Vector3 forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += forward;
            if (Input.GetKey(KeyCode.S)) move -= forward;
            if (Input.GetKey(KeyCode.A)) move -= right;
            if (Input.GetKey(KeyCode.D)) move += right;
            if (Input.GetKey(KeyCode.Q)) move += Vector3.down;
            if (Input.GetKey(KeyCode.E)) move += Vector3.up;
            camPosition += move * (Input.GetKey(KeyCode.LeftShift) ? 25f : 10f) * Time.deltaTime;
            // zoom with scroll
            camPosition += forward * Input.GetAxis("Mouse ScrollWheel") * 20f;

            cam.transform.position = camPosition;
            cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ExitEditor();
                return;
            }

            if (pointerOverUI) return;

            // Gizmo dragging
            if (isDraggingGizmo)
            {
                if (!Input.GetMouseButton(0))
                {
                    isDraggingGizmo = false;
                    return;
                }
                if (selectedPlaced < 0 || selectedPlaced >= placed.Count) { isDraggingGizmo = false; return; }
                var sel = placed[selectedPlaced];
                if (sel.go == null) { isDraggingGizmo = false; return; }

                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (dragPlane.Raycast(ray, out float enter))
                {
                    Vector3 cur = ray.GetPoint(enter);
                    Vector3 delta = cur - dragStartWorldPoint;
                    Vector3 axisDir = (dragAxis == 0) ? Vector3.right : (dragAxis == 1 ? Vector3.up : Vector3.forward);

                    if (gizmoMode == 0) // move
                    {
                        sel.go.transform.position = dragStartObjectPos + Vector3.Project(delta, axisDir);
                    }
                    else if (gizmoMode == 2) // scale
                    {
                        float d = Vector3.Dot(delta, axisDir);
                        Vector3 s = dragStartObjectScale;
                        float ns = Mathf.Max(0.01f, s[dragAxis] + d);
                        s[dragAxis] = ns;
                        sel.go.transform.localScale = s;
                    }
                    else if (gizmoMode == 1) // rotate
                    {
                        Vector3 fromCenter = cur - sel.go.transform.position;
                        Vector3 proj = Vector3.ProjectOnPlane(fromCenter, axisDir);
                        if (proj.sqrMagnitude > 0.0001f)
                        {
                            float ang = Mathf.Atan2(Vector3.Dot(Vector3.Cross(dragStartVector, proj), axisDir), Vector3.Dot(dragStartVector, proj));
                            sel.go.transform.rotation = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, axisDir) * dragStartObjectRot;
                        }
                    }
                }
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                // Gizmo handle hit?
                if (toolIndex == 1 && Physics.Raycast(ray, out RaycastHit ghit, 1000f))
                {
                    var gh = ghit.collider != null ? ghit.collider.GetComponent<GizmoHandle>() : null;
                    if (gh != null && selectedPlaced >= 0 && selectedPlaced < placed.Count)
                    {
                        var sel = placed[selectedPlaced];
                        if (sel.go != null)
                        {
                            isDraggingGizmo = true;
                            dragAxis = gh.axis;
                            dragStartObjectPos = sel.go.transform.position;
                            dragStartObjectRot = sel.go.transform.rotation;
                            dragStartObjectScale = sel.go.transform.localScale;
                            Vector3 axisDir = (dragAxis == 0) ? Vector3.right : (dragAxis == 1 ? Vector3.up : Vector3.forward);
                            if (gizmoMode == 1)
                            {
                                dragPlane = new Plane(axisDir, sel.go.transform.position);
                                dragStartVector = Vector3.ProjectOnPlane(ghit.point - sel.go.transform.position, axisDir);
                                if (dragStartVector.sqrMagnitude < 0.0001f) dragStartVector = Vector3.forward;
                            }
                            else
                            {
                                dragPlane = new Plane(cam.transform.forward, sel.go.transform.position);
                            }
                            if (dragPlane.Raycast(ray, out float enter)) dragStartWorldPoint = ray.GetPoint(enter);
                            return;
                        }
                    }
                }

                if (toolIndex == 0) // place
                {
                    if (selectedPalette >= 0 && selectedPalette < currentPalette.Count)
                    {
                        if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
                        {
                            PlaceSelected(hit.point);
                        }
                    }
                }
                else if (toolIndex == 1) // select
                {
                    if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
                    {
                        var handle = placed.FirstOrDefault(p => p.go != null && (p.go == hit.collider.gameObject || p.go.transform.IsChildOf(hit.collider.transform) || hit.collider.transform.IsChildOf(p.go.transform)));
                        if (handle != null)
                        {
                            selectedPlaced = placed.IndexOf(handle);
                        }
                        else
                        {
                            selectedPlaced = -1;
                        }
                        DestroyGizmo();
                    }
                }
                else if (toolIndex == 2) // delete
                {
                    if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
                    {
                        var handle = placed.FirstOrDefault(p => p.go != null && (p.go == hit.collider.gameObject || p.go.transform.IsChildOf(hit.collider.transform) || hit.collider.transform.IsChildOf(p.go.transform)));
                        if (handle != null)
                        {
                            if (handle.go != null) Destroy(handle.go);
                            placed.Remove(handle);
                            selectedPlaced = -1;
                            DestroyGizmo();
                        }
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.Delete) && selectedPlaced >= 0 && selectedPlaced < placed.Count)
            {
                var h = placed[selectedPlaced];
                if (h.go != null) Destroy(h.go);
                placed.RemoveAt(selectedPlaced);
                selectedPlaced = -1;
                DestroyGizmo();
            }
        }

        private void PlaceSelected(Vector3 point)
        {
            string name = null;

            if (categoryIndex == 3) // spawns
            {
                // create marker
                GameObject marker = new GameObject("SpawnPoint");
                marker.AddComponent<SpawnPoint>();
                // visual
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetParent(marker.transform, false);
                sphere.transform.localScale = Vector3.one * 0.5f;
                var r = sphere.GetComponent<Renderer>();
                if (r != null) r.material.color = Color.yellow;
                Destroy(sphere.GetComponent<Collider>());
                name = "__SpawnPoint__";
                marker.transform.SetParent(contentRoot.transform, true);
                marker.transform.position = point + Vector3.up * 0.25f;
                placed.Add(new PlacedHandle { go = marker, prefabName = name });
                return;
            }

            if (selectedPalette < 0 || selectedPalette >= currentPalette.Count) return;
            var prefRef = currentPalette[selectedPalette];
            if (prefRef == null || !prefRef.IsValid()) return;
            GameObject prefabAsset = prefRef.Prefab;
            if (prefabAsset == null) return;
            name = prefRef.PrefabName;
            GameObject inst = Instantiate(prefabAsset, point, Quaternion.identity);
            inst.name = name + "(Clone)";
            if (contentRoot != null) inst.transform.SetParent(contentRoot.transform, true);
            placed.Add(new PlacedHandle { go = inst, prefabName = name });
            if (!prefabRefByName.ContainsKey(name)) prefabRefByName[name] = prefRef;
        }

        private bool IsMonsterPrefab(GameObject go)
        {
            if (go == null) return false;
            if (go.name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c.GetType().Name.IndexOf("Enemy", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private void CreateGizmo()
        {
            DestroyGizmo();
            if (selectedPlaced < 0 || selectedPlaced >= placed.Count) return;
            var sel = placed[selectedPlaced];
            if (sel.go == null) return;

            gizmoRoot = new GameObject("GizmoRoot");
            Color[] colors = new Color[] { Color.red, Color.green, Color.blue };
            Vector3[] positions = new Vector3[] { Vector3.right, Vector3.up, Vector3.forward };
            for (int axis = 0; axis < 3; axis++)
            {
                GameObject h = GameObject.CreatePrimitive(PrimitiveType.Cube);
                h.name = "GizmoHandle";
                h.transform.SetParent(gizmoRoot.transform, false);
                h.transform.localPosition = positions[axis];
                if (gizmoMode == 0)
                {
                    h.transform.localScale = new Vector3(0.8f, 0.05f, 0.05f);
                }
                else if (gizmoMode == 2)
                {
                    h.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
                    h.transform.localPosition = positions[axis] * 1.2f;
                }
                else
                {
                    h.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
                    h.transform.localPosition = positions[axis] * 1.2f;
                }
                var rend = h.GetComponent<Renderer>();
                if (rend != null) rend.material.color = colors[axis];
                var gh = h.AddComponent<GizmoHandle>();
                gh.axis = axis;
            }
        }

        private void DestroyGizmo()
        {
            if (gizmoRoot != null)
            {
                Destroy(gizmoRoot);
                gizmoRoot = null;
            }
        }

        private void RefreshPalette()
        {
            currentPalette.Clear();
            selectedPalette = -1;
            if (categoryIndex == 0) currentPalette.AddRange(roomRefs);
            else if (categoryIndex == 1) currentPalette.AddRange(valuableRefs);
            else if (categoryIndex == 2) currentPalette.AddRange(monsterRefs);
            else if (categoryIndex == 3) currentPalette.Clear(); // spawns have no prefab list
            else
            {
                currentPalette.AddRange(roomRefs);
                currentPalette.AddRange(valuableRefs);
                currentPalette.AddRange(monsterRefs);
            }
        }

        private void OnGUI()
        {
            if (!Active) return;

            pointerOverUI = false;

            // main window
            windowRect = GUI.Window(0, windowRect, DrawMainWindow, "REPO Level Editor");
            saveRect = GUI.Window(1, saveRect, DrawSaveWindow, "Save / Load");

            // selected object inspector small
            if (selectedPlaced >= 0 && selectedPlaced < placed.Count)
            {
                var h = placed[selectedPlaced];
                if (h.go != null)
                {
                    Rect r = new Rect(Screen.width - 320, 10, 310, 260);
                    GUI.Box(r, "Selected: " + h.prefabName);
                    GUILayout.BeginArea(new Rect(r.x + 10, r.y + 25, r.width - 20, r.height - 35));
                    Vector3 p = h.go.transform.position;
                    p.x = FloatField("X", p.x);
                    p.y = FloatField("Y", p.y);
                    p.z = FloatField("Z", p.z);
                    h.go.transform.position = p;
                    Vector3 e = h.go.transform.rotation.eulerAngles;
                    e.x = FloatField("Rot X", e.x);
                    e.y = FloatField("Rot Y", e.y);
                    e.z = FloatField("Rot Z", e.z);
                    h.go.transform.rotation = Quaternion.Euler(e);
                    GUILayout.EndArea();
                }
            }
        }

        private float FloatField(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(50));
            string s = GUILayout.TextField(value.ToString("F2"), GUILayout.Width(80));
            GUILayout.EndHorizontal();
            if (float.TryParse(s, out float f)) return f;
            return value;
        }

        private void DrawMainWindow(int id)
        {
            pointerOverUI = true;
            GUILayout.BeginVertical();

            // tool
            GUILayout.Label("Tool:");
            toolIndex = GUILayout.Toolbar(toolIndex, new string[] { "Place", "Select", "Delete" });

            GUILayout.Space(2);
            GUILayout.Label("Gizmo:");
            int newGizmo = GUILayout.Toolbar(gizmoMode, new string[] { "Move", "Rotate", "Scale" });
            if (newGizmo != gizmoMode)
            {
                gizmoMode = newGizmo;
                DestroyGizmo();
            }

            GUILayout.Space(4);
            GUILayout.Label("Category:");
            string[] cats = new string[] { "Rooms", "Valuables", "Monsters", "Spawns", "All" };
            int newCat = GUILayout.Toolbar(categoryIndex, cats);
            if (newCat != categoryIndex)
            {
                categoryIndex = newCat;
                RefreshPalette();
                selectedPalette = -1;
            }

            if (categoryIndex != 3)
            {
                GUILayout.Label("Palette (" + currentPalette.Count + "):");
                paletteScroll = GUILayout.BeginScrollView(paletteScroll, GUILayout.Height(220));
                for (int i = 0; i < currentPalette.Count; i++)
                {
                    var pr = currentPalette[i];
                    if (pr == null) continue;
                    bool sel = (i == selectedPalette);
                    string label = (sel ? "> " : "  ") + pr.PrefabName;
                    if (GUILayout.Button(label))
                    {
                        selectedPalette = i;
                    }
                }
                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label("Spawns: click to place marker.");
            }

            GUILayout.Space(4);
            GUILayout.Label("Placed: " + placed.Count);

            if (GUILayout.Button("Clear All"))
            {
                foreach (var h in placed)
                {
                    if (h.go != null) Destroy(h.go);
                }
                placed.Clear();
                selectedPlaced = -1;
            }

            GUILayout.Label(status, GUILayout.Height(30));

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawSaveWindow(int id)
        {
            pointerOverUI = true;
            GUILayout.BeginVertical();

            GUILayout.Label("File name:");
            fileName = GUILayout.TextField(fileName);

            GUILayout.Label("Directory: " + REPOLevelEditorPlugin.GetLevelsDirectory());

            if (GUILayout.Button("Save .repo"))
            {
                try
                {
                    string dir = REPOLevelEditorPlugin.GetLevelsDirectory();
                    Directory.CreateDirectory(dir);
                    string safe = string.IsNullOrEmpty(fileName) ? "level" : fileName;
                    foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                    string file = Path.Combine(dir, safe + ".repo");
                    LevelData data = new LevelData();
                    data.version = "1.0";
                    foreach (var h in placed)
                    {
                        if (h.go == null) continue;
                        data.objects.Add(new PlacedObjectData
                        {
                            prefabName = h.prefabName,
                            px = h.go.transform.position.x,
                            py = h.go.transform.position.y,
                            pz = h.go.transform.position.z,
                            rx = h.go.transform.rotation.eulerAngles.x,
                            ry = h.go.transform.rotation.eulerAngles.y,
                            rz = h.go.transform.rotation.eulerAngles.z,
                            sx = h.go.transform.localScale.x,
                            sy = h.go.transform.localScale.y,
                            sz = h.go.transform.localScale.z
                        });
                    }
                    string json = JsonUtility.ToJson(data, true);
                    File.WriteAllText(file, json);
                    status = "Saved: " + file;
                }
                catch (Exception e)
                {
                    status = "Save failed: " + e.Message;
                }
            }

            if (GUILayout.Button("Load .repo"))
            {
                try
                {
                    string dir = REPOLevelEditorPlugin.GetLevelsDirectory();
                    string safe = string.IsNullOrEmpty(fileName) ? "level" : fileName;
                    foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                    string file = Path.Combine(dir, safe + ".repo");
                    if (!File.Exists(file))
                    {
                        status = "File not found: " + file;
                        return;
                    }
                    string json = File.ReadAllText(file);
                    LevelData data = JsonUtility.FromJson<LevelData>(json);
                    // clear
                    foreach (var h in placed)
                    {
                        if (h.go != null) Destroy(h.go);
                    }
                    placed.Clear();
                    selectedPlaced = -1;

                    foreach (var od in data.objects)
                    {
                        if (od.prefabName == "__SpawnPoint__")
                        {
                            GameObject marker = new GameObject("SpawnPoint");
                            marker.AddComponent<SpawnPoint>();
                            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                            sphere.transform.SetParent(marker.transform, false);
                            sphere.transform.localScale = Vector3.one * 0.5f;
                            var r = sphere.GetComponent<Renderer>();
                            if (r != null) r.material.color = Color.yellow;
                            Destroy(sphere.GetComponent<Collider>());
                            if (contentRoot != null) marker.transform.SetParent(contentRoot.transform, true);
                            marker.transform.position = new Vector3(od.px, od.py, od.pz);
                            marker.transform.rotation = Quaternion.Euler(od.rx, od.ry, od.rz);
                            marker.transform.localScale = new Vector3(od.sx, od.sy, od.sz);
                            placed.Add(new PlacedHandle { go = marker, prefabName = "__SpawnPoint__" });
                        }
                        else
                        {
                            GameObject prefab = null;
                            if (prefabRefByName.TryGetValue(od.prefabName, out var pr) && pr != null)
                            {
                                prefab = pr.Prefab;
                            }
                            else
                            {
                                prefab = Resources.Load<GameObject>(od.prefabName);
                            }
                            if (prefab == null)
                            {
                                status = "Missing prefab: " + od.prefabName;
                                continue;
                            }
                            GameObject inst = Instantiate(prefab, new Vector3(od.px, od.py, od.pz), Quaternion.Euler(od.rx, od.ry, od.rz));
                            inst.name = od.prefabName + "(Clone)";
                            if (contentRoot != null) inst.transform.SetParent(contentRoot.transform, true);
                            inst.transform.localScale = new Vector3(od.sx, od.sy, od.sz);
                            placed.Add(new PlacedHandle { go = inst, prefabName = od.prefabName });
                        }
                    }
                    status = "Loaded: " + file;
                }
                catch (Exception e)
                {
                    status = "Load failed: " + e.Message;
                }
            }

            if (GUILayout.Button("Play This Level"))
            {
                try
                {
                    string dir = REPOLevelEditorPlugin.GetLevelsDirectory();
                    Directory.CreateDirectory(dir);
                    string safe = string.IsNullOrEmpty(fileName) ? "level" : fileName;
                    foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                    string file = safe + ".repo";
                    string fullPath = Path.Combine(dir, file);

                    LevelData data = new LevelData();
                    data.version = "1.0";
                    foreach (var h in placed)
                    {
                        if (h.go == null) continue;
                        data.objects.Add(new PlacedObjectData
                        {
                            prefabName = h.prefabName,
                            px = h.go.transform.position.x,
                            py = h.go.transform.position.y,
                            pz = h.go.transform.position.z,
                            rx = h.go.transform.rotation.eulerAngles.x,
                            ry = h.go.transform.rotation.eulerAngles.y,
                            rz = h.go.transform.rotation.eulerAngles.z,
                            sx = h.go.transform.localScale.x,
                            sy = h.go.transform.localScale.y,
                            sz = h.go.transform.localScale.z
                        });
                    }
                    string json = JsonUtility.ToJson(data, true);
                    File.WriteAllText(fullPath, json);

                    REPOLevelEditorPlugin.SelectedRepoLevel = file;
                    REPOLevelEditorPlugin.SyncRepoLevel();
                    ExitEditor();
                }
                catch (Exception e)
                {
                    status = "Play failed: " + e.Message;
                }
            }

            if (GUILayout.Button("Exit Editor"))
            {
                ExitEditor();
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}

public class GizmoHandle : UnityEngine.MonoBehaviour
{
    public int axis;
}
