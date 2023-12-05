using System.Collections.Generic;
using System.IO;
using Quack.BuildMagic.Editor.Utils;
using UnityEditor;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Quack.Utils.GitLockSupport {
    internal static class GitLockSupport {
        private static readonly string EditorPrefsKey = $"{nameof(GitLockSupport)}:Enabled";

        private static bool SystemDisabled {
            get => EditorPrefs.GetBool(EditorPrefsKey, false);
            set => EditorPrefs.SetBool(EditorPrefsKey, value);
        }

        private class FileSaveWatcher : AssetModificationProcessor {
            private static string[] OnWillSaveAssets(string[] paths) {
                if (SystemDisabled) {
                    return paths;
                }
                var pathsToSave = new List<string>();
                for (int i = 0; i < paths.Length; ++i) {
                    var path = paths[i];
                    var fileInfo = new FileInfo(path);
                    if (fileInfo.IsReadOnly) {
                        WorkingOnUnLockedFileDialog(path);
                    }
                    else {
                        pathsToSave.Add(paths[i]);
                    }
                }
                return pathsToSave.ToArray();
            }
        }

        [InitializeOnLoad]
        private class ModifiedSceneWatcher {
            private static readonly HashSet<string> Ignored = new HashSet<string>();

            static ModifiedSceneWatcher() {
                EditorApplication.update += Update;
            }

            private static void Update() {
                if (EditorApplication.isPlaying) {
                    return;
                }
                if (SystemDisabled) {
                    return;
                }
                for (int i = 0; i < SceneManager.loadedSceneCount; i++) {
                    var scene = SceneManager.GetSceneAt(i);
                    var path = scene.path;

                    // scene not saved yet
                    if (path == "") {
                        continue;
                    }
                    if (!scene.isDirty) {
                        Ignored.Remove(path);
                        continue;
                    }
                    if (Ignored.Contains(path)) {
                        continue;
                    }
                    if (new FileInfo(path).IsReadOnly) {
                        var ignore = WorkingOnUnLockedFileDialog(path);
                        if (ignore) {
                            Ignored.Add(path);
                        }
                    }
                }
            }
        }

        private class UnlockFileContextMenuItem {
            [MenuItem("Assets/Unlock File")]
            private static void UnlockFile() {
                SystemDisabled = false;
                var obj = Selection.activeObject;
                var path = AssetDatabase.GetAssetPath(obj);
                var success = UnLockFile(path);
                if (success) {
                    EditorUtility.DisplayDialog("Git Lock",
                        $"Git Unlock Successful!",
                        $"OK");
                }
                else {
                    EditorUtility.DisplayDialog("Git Lock",
                        $"Git Unlock Failed",
                        $"ERROR");
                }
            }

            [MenuItem("Assets/Unlock File", true)]
            private static bool UnlockFileValidation() {
                return Selection.activeObject is SceneAsset;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="path"></param>
        /// <returns>ignore the file</returns>
        private static bool WorkingOnUnLockedFileDialog(string path) {
            var ans = EditorUtility.DisplayDialogComplex("Git Lock",
                $"You have modified this scene without locking it. " +
                $"You will not be able to save this scene without locking it. " +
                $"Would you like to lock this file now?",
                "OK, lock it",
                "NO, I am ok with not being able to save this file",
                "Disable git locking");
            switch (ans) {
                case 0: //ok, lockit 
                    return LockFileDialog(path);
                case 1: //cancel, ignore
                    return true;
                case 2: //alt, disable the system
                    return DisableSystemDialog();
            }
            return false;
        }
        private static bool DisableSystemDialog() {
            if (EditorUtility.DisplayDialog("Git Lock",
                    "Are you sure you would like to disable git locking support on this repository. " +
                    "It is very tricky to reverse this. " +
                    "Only do this if you know what you are doing.",
                    "Yes I am sure, disable git locking please",
                    "Cancel")) {
                SystemDisabled = true;
            }
            return false;
        }
        private static bool LockFileDialog(string path) {
            if (LockFile(path)) {
                Debug.Log("Locking Successful! Enjoy exclusive control over this file");
                EditorUtility.DisplayDialog("Git Lock",
                    "Locking Successful! Enjoy exclusive control over this file", "Thank you, I will");
                return false;
            }
            else {
                var lockerName = GetLockerName(path);
                Debug.Log($"Locking Failed! {lockerName} has this file currently locked");
                if (EditorUtility.DisplayDialog("Git Lock",
                        $"Locking Failed! {lockerName} has this file currently locked", "Force Acquire Lock",
                        "Work on the file without saving it")) {
                    if (ForceAcquireFile(path)) {
                        Debug.Log($"Force Acquire Lock Successful! Make sure {lockerName} knows about this!");
                        EditorUtility.DisplayDialog("Git Lock",
                            $"Force Acquire Lock Successful! Make sure {lockerName} knows about this!",
                            $"Of Course I will notify {lockerName} now.");
                    }
                    else {
                        Debug.LogError($"Force Acquire Failed. Something must have gone wrong");
                        EditorUtility.DisplayDialog("Git Lock",
                            $"Force Acquire Failed. Something must have gone wrong", $"OK");
                    }
                }
                else {
                    return true;
                }
            }
            return false;
        }

        private static bool LockFile(string path) {
            Util.RunCommandAndGetOutput("git", $"lfs lock \"{path}\"", out int exitCode, true);
            return exitCode == 0;
        }
        
        private static string GetLockerName(string path) {
            var jsonText =
                Util.RunCommandAndGetOutput("git", $"lfs locks --path \"{path}\" --json", out int exitCode, false);
            var json = SimpleJSON.JSON.Parse(jsonText);
            var name = json[0]["owner"]["name"].Value;
            return name;
        }
        
        private static bool ForceAcquireFile(string path) {
            Util.RunCommandAndGetOutput("git", $"lfs unlock \"{path}\" --force", true);
            Util.RunCommandAndGetOutput("git", $"lfs lock \"{path}\"", out int exitCode, true);
            return exitCode == 0;
        }

        private static bool UnLockFile(string path) {
            Util.RunCommandAndGetOutput("git", $"lfs unlock \"{path}\"", out int exitCode, true);
            return exitCode == 0;
        }
        
    }
}
