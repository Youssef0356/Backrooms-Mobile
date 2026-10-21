using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Rendering;
#if USE_URP
using UnityEngine.Rendering.Universal;
#endif

namespace StarterAssets
{
    public class URPWizard : EditorWindow
    {
        [InitializeOnLoadMethod]
        static void OnInitialize()
        {
            // Scripts also load inside asset import worker processes; never run there.
            if (AssetDatabase.IsAssetImportWorkerProcess())
                return;

            // Defer until the Editor is ready so package manager requests can progress.
            EditorApplication.delayCall += URPCheck;
        }

        static void URPCheck()
        {
            if (GraphicsSettings.currentRenderPipeline != null)
                return;

            var request = Client.List();

            // Never busy-wait on request.IsCompleted here: it blocks the main thread,
            // which prevents the package manager from ever completing the request
            // and freezes the whole Editor.
            WhenComplete(request, () =>
            {
                if (request.Status != StatusCode.Success)
                    return;

                if (request.Result.All(info => info.name != "com.unity.render-pipelines.universal"))
                {
                    var addRequest = Client.Add("com.unity.render-pipelines.universal");
                    WhenComplete(addRequest, () => Client.Resolve());
                }
                else
                {
                    FindAndAssignPipeline();
                }
            });
        }

        /// Polls a package manager request from the Editor update loop and invokes
        /// <paramref name="onCompleted"/> once it is done.
        static void WhenComplete<T>(Request<T> request, Action onCompleted)
        {
            void Poll()
            {
                if (request == null || !request.IsCompleted) return;
                EditorApplication.update -= Poll;
                onCompleted?.Invoke();
            }

            if (request != null && request.IsCompleted) Poll();
            else EditorApplication.update += Poll;
        }

#if USE_URP
        static void FindAndAssignPipeline()
        {
            var existingPipelines = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");

            if (existingPipelines.Length == 0)
            {
                Debug.LogError($"Universal Render Pipeline Asset was not found.\n" +
                               $"Please create one and assign under the Project Settings > Graphics > Scriptable Render Pipeline Settings.");
                return;
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(existingPipelines[0]));
            GraphicsSettings.defaultRenderPipeline = pipeline;
        }

        class PipelineAssetProcessor : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
            {
                //if we have no pipeline set, we try to find one as one may have been imported
                if (GraphicsSettings.currentRenderPipeline != null)
                    return;

                FindAndAssignPipeline();
            }
        }
#else
    static void FindAndAssignPipeline(){}
#endif
    }
}
