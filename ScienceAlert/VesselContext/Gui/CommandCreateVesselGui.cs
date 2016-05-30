﻿using System;
using System.Collections;
using System.Linq;
using JetBrains.Annotations;
using KSP.UI;
using ReeperCommon.Containers;
using ReeperCommon.Extensions;
using ReeperCommon.Logging;
using ReeperCommon.Utilities;
using ReeperKSP.AssetBundleLoading;
using strange.extensions.command.impl;
using strange.extensions.context.api;
using ScienceAlert.UI.ExperimentWindow;
using ScienceAlert.UI.OptionsWindow;
using ScienceAlert.UI.TooltipWindow;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ScienceAlert.VesselContext.Gui
{
// ReSharper disable once ClassNeverInstantiated.Global
    class CommandCreateVesselGui : Command
    {
        private readonly IContext _context;
        private readonly CoroutineHoster _coroutineRunner;
        private readonly ICriticalShutdownEvent _criticalShutdownSignal;


        
#pragma warning disable 649 // is not assigned to; that's fine because it will be assigned via AssetBundleAssetLoader

        [AssetBundleAsset("assets/sciencealert/ui/sciencealertoptionswindowprefab.prefab", "sciencealert.ksp")] 
        private OptionsWindowView _optionsWindow;

        [AssetBundleAsset("assets/sciencealert/ui/sciencealertexperimentwindowprefab.prefab", "sciencealert.ksp")]
        private ExperimentWindowView _experimentWindow;

        [AssetBundleAsset("assets/sciencealert/ui/sciencealertsensortooltipwindowprefab.prefab", "sciencealert.ksp")]
        private TooltipWindowView _tooltipWindow;
#pragma warning restore 649


        public CommandCreateVesselGui(
            [NotNull, Name(ContextKeys.CONTEXT)] IContext context,
            [NotNull] CoroutineHoster coroutineRunner,
            [NotNull] ICriticalShutdownEvent criticalShutdownSignal)
        {
            if (context == null) throw new ArgumentNullException("context");
            if (coroutineRunner == null) throw new ArgumentNullException("coroutineRunner");
            if (criticalShutdownSignal == null) throw new ArgumentNullException("criticalShutdownSignal");

            _context = context;
            _coroutineRunner = coroutineRunner;
            _criticalShutdownSignal = criticalShutdownSignal;
        }


        public override void Execute()
        {
            Log.Verbose("Creating VesselContext Gui");

            Retain();
            _coroutineRunner.StartCoroutine(Begin());
        }


        private IEnumerator Begin()
        {
            var loadAssetRoutine = CoroutineHoster.Instance.StartCoroutineValueless(LoadViewAssets());
            yield return loadAssetRoutine.YieldUntilComplete;

            if (loadAssetRoutine.Error.Any())
            {
                Log.Error("Failed to load view assets: " + loadAssetRoutine.Error.Value);
                Cancel();
                Release();
                yield break;
            }

            var createViewsRoutine = CoroutineHoster.Instance.StartCoroutineValueless(CreateViews());
            yield return createViewsRoutine.YieldUntilComplete;

            if (createViewsRoutine.Error.Any())
            {
                Log.Error("Failed to create views: " + createViewsRoutine.Error.Value);

                _criticalShutdownSignal.Dispatch();
                Cancel();
                Release();
                yield break;
            }

            Release();
        }


        private IEnumerator CreateViews()
        {
            var experimentWindow = Object.Instantiate(_experimentWindow);
            var optionsWindow = Object.Instantiate(_optionsWindow);
            var tooltipWindow = Object.Instantiate(_tooltipWindow);

            if (experimentWindow == null || optionsWindow == null || tooltipWindow == null)
            {
                experimentWindow.Do(Object.Destroy);
                optionsWindow.Do(Object.Destroy);
                tooltipWindow.Do(Object.Destroy);

                throw new FailedToLoadAssetException("One or more view prefabs failed to load.");
            }

            var mainCanvas = UIMasterController.Instance.transform.Find("MainCanvas") as RectTransform;

            var dialogCanvas = UIMasterController.Instance.transform.Find("DialogCanvas")
                .IfNull(() => Log.Warning("Failed to find expected dialog canvas on UIMasterController"))
                .Return(t => t as RectTransform, mainCanvas);

            var tooltipCanvas = UIMasterController.Instance.transform.Find("TooltipCanvas")
                .IfNull(() => Log.Warning("Failed to find expected tooltip canvas on UIMasterController"))
                .Return(t => t as RectTransform, mainCanvas);

            _context.AddView(experimentWindow);
            _context.AddView(optionsWindow);
            _context.AddView(tooltipWindow);

            foreach (var viewTransform in new [] { experimentWindow.transform, optionsWindow.transform}.Select(t => t as RectTransform))
                viewTransform.Do(view => view.SetParent(dialogCanvas, false)).Do(view => view.SetAsLastSibling());

            tooltipWindow.Do(v => v.transform.SetParent(tooltipCanvas.transform as RectTransform, false));

            mainCanvas.Do(LayoutRebuilder.MarkLayoutForRebuild);
            dialogCanvas.Do(LayoutRebuilder.MarkLayoutForRebuild);

            //mainCanvas.GetComponentsInChildren<Canvas>().Union(new[] {mainCanvas.GetComponent<Canvas>()})
            //    .ToList().ForEach(item =>
            //    {
            //        Log.Normal(item.name + " camera: " + item.worldCamera.Return(wc => wc.name, "<Not set>"));
            //    });
            //mainCanvas.gameObject.PrintComponents(new DebugLog("MainCanvas"));

            //optionsWindow.gameObject.SetActive(false);

            //experimentWindow.transform.root.gameObject.PrintComponents(new DebugLog("ExperimentWindowDebug"));

            yield return null; // wait for views to start before proceeding
        }


        private IEnumerator LoadViewAssets()
        {
            Log.Verbose("Loading GUI assets from AssetBundle...");

            var loaderRoutine = AssetBundleAssetLoader.InjectAssetsAsync(this);
            yield return loaderRoutine.YieldUntilComplete;

            if (loaderRoutine.Error.Any())
                // ReSharper disable once ThrowingSystemException
                throw loaderRoutine.Error.Value;

            Log.Verbose("Finished loading view assets");
        }
    }
}
