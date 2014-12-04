﻿/******************************************************************************
 *                  Science Alert for Kerbal Space Program                    *
 *                                                                            *
 * Version 1.8                                                              *
 * Author: xEvilReeperx                                                       *
 * Created: 3/3/2014                                                          *
 * ************************************************************************** *
    Copyright (C) 2014 Allen Mrazek (amrazek@hotmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
 ****************************************************************************
 * Changelog
 * 1.8
 *      Feature: Per-vessel profiles
 *      
 *      Feature: Draggable, pinnable windows
 *      
 *      Feature: surface sample at top
 * 
 * 1.7 
 *      Feature: ApplicationLauncher (stock toolbar) support
 *      
 *      Feature: Blizzy's toolbar is now optional. If you have it installed,
 *               you can choose between the stock toolbar and Blizzy's toolbar
 *               in the options menu
 *               
 *      Feature: (Option) Experiment buttons can show next report value
 *      
 *      Feature: (Option) EVA report button can be set to always appear at
 *               the top of the list
 *               
 *      Feature: (Option) Surface sample availability can now be tracked while
 *               not on EVA. Surface sample button while not EVA'ing will cause
 *               kerbal to go EVA
 *               
 *      Feature: (Option) Experiment popup can be configured to automatically
 *               reappear on EVA if it was visible before leaving the vessel
 *      
 * 
 *      Bugfix: Fixed issue that caused the popup windows to flicker (especially
 *              the options window)
 *              
 *      Note: Sound config (other than per-experiment and global on/off) settings 
 *            were removed while making some changes to the audio system. 
 *            
 *      
 * 1.6
 *      Bugfix: Fixed issue where alerts could appear for science reports worth
 *              no science
 *              
 *      Bugfix: Rapidly disintegrating vessels could sometimes cause an issue
 *              that spammed KeyNotFound exceptions
 *              
 *      Bugfix: Biome map filter function could sometimes be interrupted and
 *              be left in a bad state, causing ScienceAlert to stop reporting
 *              science
 *              
 *      Bugfix: Biome map filter function could, in rare circumstances, produce
 *              a wrong result
 * 
 * 1.5
 *      New feature: you can now set a minimum threshold for a science report
 *          If performing an experiment won't result in at least that much
 *          science, then it will be ignored even if it would otherwise match
 *          your filter settings
 *          
 *      Small UI improvement: added a collapsing section where new
 *          options can be added
 *          
 *      Fixed an issue where the biome filter could sometimes
 *          ignore biome
 *          
 *      Fixed an issue caused by last version's fix which could make
 *          the flight camera and map camera active at the same time if
 *          the player goes on eva with an active map view
 *          
 *          
 * 1.4a
 *      Fixed an issue where game would use the incorrect camera
 *          after ejecting an eva kerbal, potentially causing a crash
 *          
 * 1.4
 *      Fixed an issue that could cause ScienceAlert to spew NREs when
 *          the orbited body didn't have a biome map
 *          
 * 1.3
 *      Fixed a serious issue: ScienceAlert wasn't taking science
 *          multipliers into account correctly which could result in
 *          incorrect alerts
 *          
 *      Added a global warp setting option
 *      
 *      Added a global alert sound setting option
 *      
 *      Log spam due to vessel modification reduced
 * 
 *      Experiments that rely on custom code to determine availability
 *          will now be hidden from the options window, since ScienceAlert
 *          will be unable to interact with them (determined by checking
 *          if that experiment's biome and situation mask flags are zero)
 * 
 * 
 * 1.2
 *      Fixed a serious issue: "stop on discovery" could sometimes affect
 *          orbital parameters, especially at high warp
 *          
 *      Further tweaked biome detection. There should no longer be any
 *          instances of incorrect science alerts
 * 
 * 1.1
 *      Added EVA condition warning
 *      
 *      Fixed issue where base function of ModuleScienceExperiment was called
 *          instead of the correct one
 *          
 *      Fixed issue where ScienceAlert would detect the wrong biome and flash
 *          the icon at the incorrect time
 *          
 *      Stock experiments no longer have "Assume onboard" option display
 *          by default (to reduce clutter)
 *          
 *      Temporarily added a debug menu, accessable using default settings by
 *          middle-mouse clicking the toolbar button
 *          
 *      Available experiment menu polished a little; will now appear in the
 *          same location consistently
 * 
 * 1.0
 *      First public release
 *****************************************************************************/

//#define PROFILE
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ReeperCommon;


namespace ScienceAlert
{
    using Window = ReeperCommon.Window;


    

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ScienceAlertCore : MonoBehaviour
    {
        // --------------------------------------------------------------------
        //    Members of ScienceAlert
        // --------------------------------------------------------------------

        // owned objects
        private Toolbar.IToolbar button;
        private ScanInterface scanInterface;
        private Experiments.ExperimentManager experimentManager;


        // interfaces
        private Settings.ToolbarInterface buttonInterfaceType = Settings.ToolbarInterface.ApplicationLauncher;
        private Settings.ScanInterface scanInterfaceType = Settings.ScanInterface.None;

        // events
        public event Callback OnScanInterfaceChanged = delegate { };
        public event Callback OnToolbarButtonChanged = delegate { };


/******************************************************************************
 *                    Implementation Details
 ******************************************************************************/
        System.Collections.IEnumerator Start()
        {
            API.ScienceAlert = null;
            API.Ready = false;

            Log.Normal("Waiting on R&D...");
            while (ResearchAndDevelopment.Instance == null) yield return 0;
            while (FlightGlobals.ActiveVessel == null) yield return 0;
            while (!FlightGlobals.ready) yield return 0;

            Log.Normal("Waiting on ProfileManager...");
            while (ScienceAlertProfileManager.Instance == null || !ScienceAlertProfileManager.Instance.Ready) yield return 0; // it can sometimes take a few frames for ScenarioModules to be fully initialized

            Log.Normal("Initializing ScienceAlert");

// just in case this has unforseen consequences later...
// it should be okay since asteroidSample isn't actually defined
// in scienceDefs, who would know to mess with it?
#warning Changes asteroidSample title
            Log.Verbose("Renaming asteroidSample title");
            var exp = ResearchAndDevelopment.GetExperiment("asteroidSample");
            if (exp != null) exp.experimentTitle = "Sample (Asteroid)";
            

            Log.Verbose("Loading sounds...");
            gameObject.AddComponent<AudioPlayer>().LoadSoundsFrom(ConfigUtil.GetDllDirectoryPath() + "/sounds");
            Log.Verbose("Sounds ready.");


            Log.Verbose("Creating experiment manager");
            experimentManager = gameObject.AddComponent<Experiments.ExperimentManager>();

            

            gameObject.AddComponent<Windows.WindowEventLogic>();



            Log.Verbose("Finished creating windows");


            // set up whichever interface we're using to determine when it's
            // permissable to check for science reports
            ScanInterfaceType = Settings.Instance.ScanInterfaceType;



            Log.Verbose("ScienceAlert.Start: initializing toolbar");
            ToolbarType = Settings.Instance.ToolbarInterfaceType;
            Log.Verbose("Toolbar button ready");

            Log.Normal("ScienceAlert initialization finished.");
            API.Ready = true;
            API.ScienceAlert = this;

#if DEBUG
            //gameObject.AddComponent<Windows.Implementations.TestDrag>();
#endif
        }



        public void OnDestroy()
        {
            API.Ready = false;
            API.ScienceAlert = null;
            Button.Drawable = null;
            Settings.Instance.Save();
            Log.Verbose("ScienceAlert destroyed");
        }



#if DEBUG
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.P))
            {
                var btns = GameObject.FindObjectsOfType<UIButton>();

                btns.ToList().ForEach(b => Log.Write("UIButton: {0} at {1}", b.name, b.transform.position));

                if (ScreenSafeUI.fetch.centerAnchor.bottom == null) Log.Error("nope");
                Log.Write("center transform: {0}", ScreenSafeUI.fetch.centerAnchor.bottom.transform.position);
                Log.Write("UImanager: {0}", UIManager.instance.transform.position);
                Log.Write("ScreenSafeUI: {0}", ScreenSafeUI.fetch.transform.position);
            }
        }



        private void Awake()
        {
            Log.Debug("ScienceAlert.Awake");
        }
#endif



        #region properties


        public Toolbar.IToolbar Button
        {
            get
            {
                return button;
            }
        }


        public Experiments.ExperimentManager ExperimentManager
        {
            get
            {
                return experimentManager;
            }
        }


        public ScanInterface ScanInterface
        {
            get;
            private set;
        }

        /// <summary>
        /// Switch between toolbar at runtime as desired by the user
        /// </summary>
        public Settings.ToolbarInterface ToolbarType
        {
            get
            {
                return buttonInterfaceType;
            }
            set
            {
                if (value != buttonInterfaceType || button == null)
                {
                    if (button != null)
                    {
                        var c = gameObject.GetComponent(button.GetType());
                        if (c == null) Log.Warning("ToolbarInterface: Did not find previous interface");

                        Destroy(c);
                        button = null;
                    }

                    switch (value)
                    {
                        case Settings.ToolbarInterface.BlizzyToolbar:
                            Log.Verbose("Setting Toolbar interface to BlizzyToolbar");

                            if (ToolbarManager.ToolbarAvailable)
                            {
                                button = gameObject.AddComponent<Toolbar.BlizzyInterface>();
                            }
                            else
                            {
                                Log.Warning("Cannot set BlizzyToolbar; Toolbar not found!");
                                ToolbarType = Settings.ToolbarInterface.ApplicationLauncher;
                            }

                            break;

                        case Settings.ToolbarInterface.ApplicationLauncher:
                            Log.Verbose("Setting Toolbar interface to Application Launcher");

                            button = gameObject.AddComponent<Toolbar.AppLauncherInterface>();
                            break;
                    }

                    buttonInterfaceType = value;
                    OnToolbarButtonChanged();
                }
            }
        }



        /// <summary>
        /// Switch between scan interfaces (used to determine whether the
        /// player "knows about" a biome and can be alerted if the experiment
        /// in question uses the biome mask for a given situation)
        /// </summary>
        public Settings.ScanInterface ScanInterfaceType
        {
            get
            {
                return scanInterfaceType;
            }
            set
            {
                if (value != scanInterfaceType || scanInterface == null)
                {
                    if (scanInterface != null) DestroyImmediate(GetComponent<ScanInterface>());

                    Log.Normal("Setting scan interface type to {0}", value.ToString());

                    try
                    {
                        switch (value)
                        {
                            case Settings.ScanInterface.None:
                                scanInterface = gameObject.AddComponent<DefaultScanInterface>();
                                break;

                            case Settings.ScanInterface.ScanSat:
                                if (SCANsatInterface.IsAvailable())
                                {
                                    scanInterface = gameObject.AddComponent<SCANsatInterface>();
                                    break;
                                }
                                else
                                {
                                    Log.Write("SCANsat Interface is not available. Using default.");
                                    ScanInterfaceType = Settings.ScanInterface.None;
                                    return;
                                }

                            default:
                                throw new NotImplementedException("Unrecognized interface type");
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Error("ScienceAlert.ScanInterfaceType failed with exception {0}", e);

                        // default interface should always be available, that should be safe
                        // unless things are completely unrecoverable
                        ScanInterfaceType = Settings.ScanInterface.None;
                        return;
                    }


                    scanInterfaceType = value;
                    OnScanInterfaceChanged();
                    Log.Normal("Scan interface type is now {0}", ScanInterfaceType.ToString());
                }
            }
        }

#endregion 
    }




}
