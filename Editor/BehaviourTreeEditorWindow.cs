using System;
using Kuaishou.UIProxy.Runtime.DynamicImage;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor.Callbacks;
using Unity.Profiling;

namespace TheKiwiCoder {

    public class BehaviourTreeEditorWindow : EditorWindow {

        [System.Serializable]
        public class PendingScriptCreate {
            public bool pendingCreate = false;
            public string scriptName = "";
            public string sourceGuid = "";
            public bool isSourceParent = false;
            public Vector2 nodePosition;

            public void Reset() {
                pendingCreate = false;
                scriptName = "";
                sourceGuid = "";
                isSourceParent = false;
                nodePosition = Vector2.zero;
            }
        }

        public class BehaviourTreeEditorAssetModificationProcessor : AssetModificationProcessor {

            static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions opt) {
                if (HasOpenInstances<BehaviourTreeEditorWindow>()) {
                    BehaviourTreeEditorWindow wnd = GetWindow<BehaviourTreeEditorWindow>();
                    wnd.ClearIfSelected(path);
                }
                return AssetDeleteResult.DidNotDelete;
            }
        }

        static readonly ProfilerMarker editorUpdate = new ProfilerMarker("BehaviourTree.EditorUpdate");
        public static BehaviourTreeEditorWindow Instance;
        public BehaviourTreeProjectSettings settings;
        public VisualTreeAsset behaviourTreeXml;
        public VisualTreeAsset nodeXml;
        public StyleSheet behaviourTreeStyle;
        public TextAsset scriptTemplateActionNode;
        public TextAsset scriptTemplateConditionNode;
        public TextAsset scriptTemplateCompositeNode;
        public TextAsset scriptTemplateDecoratorNode;

        public BehaviourTree tree;
        [HideInInspector]
        public SerializedBehaviourTree serializedTree;
        public BehaviourTreeView treeView;

        public ToolbarBreadcrumbs breadcrumbs;
        
        public InspectorView inspectorView;
        public BlackboardView blackboardView;
        public OverlayView overlayView;
        public ToolbarMenu toolbarMenu;
        public Label versionLabel;
        public NewScriptDialogView newScriptDialog;
        public BehaviourTreeEditorWindowState windowState;

        [SerializeField]
        public PendingScriptCreate pendingScriptCreate = new PendingScriptCreate();


        [MenuItem("Tools/Home2D/BehaviorTree")]
        public static void OpenWindow() {
            BehaviourTreeEditorWindow wnd = GetWindow<BehaviourTreeEditorWindow>();
            wnd.titleContent = new GUIContent("BehaviourTreeEditor");
            wnd.minSize = new Vector2(800, 600);
        }

        public static void OpenWindow(BehaviourTree tree) {
            BehaviourTreeEditorWindow wnd = GetWindow<BehaviourTreeEditorWindow>();
            wnd.titleContent = new GUIContent("BehaviourTreeEditor");
            wnd.minSize = new Vector2(800, 600);
            wnd.SelectNewTree(tree);
        }

        [OnOpenAsset]
        public static bool OnOpenAsset(int instanceId, int line) {
            if (Selection.activeObject is BehaviourTree) {
                OpenWindow(Selection.activeObject as BehaviourTree);
                return true;
            }
            return false;
        }

        public void CreateGUI() {
            Instance = this;
            settings = BehaviourTreeProjectSettings.GetOrCreateSettings();
            windowState = settings.windowState;

            // Each editor window contains a root VisualElement object
            VisualElement root = rootVisualElement;

            // Import UXML
            var visualTree = behaviourTreeXml;
            visualTree.CloneTree(root);

            // A stylesheet can be added to a VisualElement.
            // The style will be applied to the VisualElement and all of its children.
            var styleSheet = behaviourTreeStyle;
            root.styleSheets.Add(styleSheet);

            // Main treeview
            inspectorView = root.Q<InspectorView>();
            blackboardView = root.Q<BlackboardView>();
            toolbarMenu = root.Q<ToolbarMenu>();
            overlayView = root.Q<OverlayView>("OverlayView");
            newScriptDialog = root.Q<NewScriptDialogView>("NewScriptDialogView");
            treeView = root.Q<BehaviourTreeView>();
            breadcrumbs = root.Q<ToolbarBreadcrumbs>();
            
            versionLabel = root.Q<Label>("Version");

            // Toolbar assets menu
            toolbarMenu.RegisterCallback<MouseEnterEvent>((evt) => {

                // Refresh the menu options just before it's opened (on mouse enter)
                toolbarMenu.menu.MenuItems().Clear();
                var behaviourTrees = EditorUtility.GetAssetPaths<BehaviourTree>();
                behaviourTrees.ForEach(path => {
                    var fileName = System.IO.Path.GetFileName(path);
                    toolbarMenu.menu.AppendAction($"{fileName}", (a) => {
                        var tree = AssetDatabase.LoadAssetAtPath<BehaviourTree>(path);
                        SelectNewTree(tree);
                    });
                });
                if (EditorApplication.isPlaying) {

                    toolbarMenu.menu.AppendSeparator();

                    var behaviourTreeInstances = Resources.FindObjectsOfTypeAll(typeof(BehaviourTreeInstance));
                    foreach (var instance in behaviourTreeInstances) {
                        BehaviourTreeInstance behaviourTreeInstance = instance as BehaviourTreeInstance;
                        GameObject gameObject = behaviourTreeInstance.gameObject;
                        if (behaviourTreeInstance != null && gameObject.scene != null && gameObject.scene.name != null) {

                            toolbarMenu.menu.AppendAction($"{gameObject.name} [{behaviourTreeInstance.behaviourTree.name}]", (a) => {
                                SelectNewTree(behaviourTreeInstance.RuntimeTree);
                                Selection.activeObject = gameObject;

                            });
                        }
                    }

                }
                toolbarMenu.menu.AppendSeparator();
                toolbarMenu.menu.AppendAction("New Tree...", (a) => OnToolbarNewAsset());
            });

            // Version label
            var packageManifest = EditorUtility.GetPackageManifest();
            if (packageManifest != null) {
                versionLabel.text = $"v {packageManifest.version}";
            }
            
            // Tree view
            treeView.OnNodeSelected -= OnNodeSelectionChanged;
            treeView.OnNodeSelected += OnNodeSelectionChanged;

            // Overlay view
            overlayView.OnTreeSelected -= SelectTree;
            overlayView.OnTreeSelected += SelectTree;

            // New Script Dialog
            newScriptDialog.style.visibility = Visibility.Hidden;

            // Restore window state between compilations
            windowState.Restore(this);

            // Create new node for any scripts just created coming back from a compile.
            if (pendingScriptCreate != null && pendingScriptCreate.pendingCreate) {
                CreatePendingScriptNode();
            }
        }

        void CreatePendingScriptNode() {

            // #TODO: Unify this with CreateNodeWindow.CreateNode

            if (treeView == null) {
                return;
            }

            NodeView source = treeView.GetNodeByGuid(pendingScriptCreate.sourceGuid) as NodeView;
            var nodeType = Type.GetType($"{pendingScriptCreate.scriptName}, Assembly-CSharp");
            if (nodeType != null) {
                NodeView createdNode;
                if (source != null) {
                    if (pendingScriptCreate.isSourceParent) {
                        createdNode = treeView.CreateNode(nodeType, pendingScriptCreate.nodePosition, source);
                    } else {
                        createdNode = treeView.CreateNodeWithChild(nodeType, pendingScriptCreate.nodePosition, source);
                    }
                } else {
                    createdNode = treeView.CreateNode(nodeType, pendingScriptCreate.nodePosition, null);
                }

                treeView.SelectNode(createdNode);
                EditorUtility.OpenScriptInEditor(createdNode);
            }

            pendingScriptCreate.Reset();
        }

        void OnUndoRedo()
        {
            if (tree == null) return;
            serializedTree.serializedObject.Update();
            treeView.PopulateView(serializedTree);
        }

        private void OnEnable() {
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable() {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnPlayModeStateChanged(PlayModeStateChange obj) {
            switch (obj) {
                case PlayModeStateChange.EnteredEditMode:
                    EditorApplication.delayCall += OnExitPlayMode;
                    break;
                case PlayModeStateChange.ExitingEditMode:
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    EditorApplication.delayCall += OnEnterPlayMode;
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    inspectorView?.Clear();
                    break;
            }
        }

        void OnEnterPlayMode() {
            OnSelectionChange();
        }

        void OnExitPlayMode() {
            OnSelectionChange();
        }

        private void OnSelectionChange() {
            if (Selection.activeGameObject) {
                BehaviourTreeInstance runner = Selection.activeGameObject.GetComponent<BehaviourTreeInstance>();
                if (runner) {
                    SelectNewTree(runner.RuntimeTree);
                }
            }
        }

        void SelectNewTree(BehaviourTree tree) {
            PopToSubtree(0, null);
            SelectTree(tree);
        }

        public void SelectTree(BehaviourTree newTree) {

            // If tree view is null the window is probably unfocused
            if (treeView == null) {
                return;
            }

            if (!newTree) {
                ClearSelection();
                return;
            }

            if (newTree != tree) {
                ClearSelection();
            }

            tree = newTree;
            serializedTree = new SerializedBehaviourTree(newTree);

            var childCount = breadcrumbs.childCount;
            breadcrumbs.PushItem($"{serializedTree.tree.name}", () => PopToSubtree(childCount, newTree));
            settings.windowState.treeStack.Add(tree);
            settings.windowState.Save();

            overlayView?.Hide();
            treeView?.PopulateView(serializedTree);
            blackboardView?.Bind(serializedTree);
        }
        
        public void PushSubTreeView(SubTree subtreeNode)
        {
            if (subtreeNode.treeAsset != null)
            {
                SelectTree(Application.isPlaying ? subtreeNode.treeInstance : subtreeNode.treeAsset);
            }
            else
            {
                Debug.LogError("Invalid subtree assigned. Assign a a behaviour tree to the tree asset field");
            }   
        }

        private void PopToSubtree(int depth, BehaviourTree tree)
        {
            while (breadcrumbs != null && breadcrumbs.childCount > depth)
            {
                breadcrumbs.PopItem();
                settings.windowState.treeStack.Pop();
                settings.windowState.Save();
            }

            if (tree)
            {
                SelectTree(tree);
            }
        }

        void ClearSelection()
        {
            tree = null;
            serializedTree = null;
            treeView?.ClearView();
            inspectorView?.Clear();
            blackboardView?.ClearView();
            overlayView?.Show();
        }

        void ClearIfSelected(string path) {
            if (serializedTree == null) {
                return;
            }

            if (AssetDatabase.GetAssetPath(serializedTree.tree) == path) {
                // Need to delay because this is called from a will delete asset callback
                EditorApplication.delayCall += () => {
                    SelectTree(null);
                };
            }
        }

        private void OnInspectorUpdate() {
            if (Application.isPlaying) {
                editorUpdate.Begin();
                treeView?.UpdateNodeStates();
                editorUpdate.End();
            }
        }

        void OnToolbarNewAsset() {
            BehaviourTree tree = EditorUtility.CreateNewTree();
            if (tree) {
                SelectNewTree(tree);
            }
        }

        public void OnNodeSelectionChanged(NodeView nodeView)
        {
            inspectorView.UpdateSelection(serializedTree, nodeView);   
        }
    }
}