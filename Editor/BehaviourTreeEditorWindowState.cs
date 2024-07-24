using System.Collections.Generic;
using System.Linq;

namespace TheKiwiCoder {

    [System.Serializable]
    public class BehaviourTreeEditorWindowState {

        public List<BehaviourTree> treeStack = new List<BehaviourTree>();

        public void Restore(BehaviourTreeEditorWindow window)
        {
            if (treeStack.Count == 0) {
                window.overlayView.Show();
            } else {
                var treeStackCopy = treeStack.ToList();
                treeStack.Clear();
                foreach (var tree in treeStackCopy) {
                    window.SelectTree(tree);
                }
            }
        }

        public void Save() {
            BehaviourTreeEditorWindow window = BehaviourTreeEditorWindow.Instance;
            BehaviourTreeProjectSettings settings = window.settings;
            UnityEditor.EditorUtility.SetDirty(settings);
        }
    }
}