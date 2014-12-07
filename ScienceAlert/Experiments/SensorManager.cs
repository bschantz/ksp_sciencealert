﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ScienceAlert.Experiments.Sensors;
using UnityEngine;
using ReeperCommon;

namespace ScienceAlert.Experiments
{
    using ProfileManager = ScienceAlertProfileManager;
    using SensorList = List<IExperimentSensor>;



    public class SensorManager
    {
        private System.Collections.IEnumerator _sensorLoop;                     // infinite-looping method used to update experiment monitors
        private SensorList _sensors = new SensorList();                         // monitors which will be checked sequentially
        private readonly SensorFactory sensorFactory;


        public delegate void ExperimentStatusChanged(SensorState oldStatus, IExperimentSensor sensor); // all status changes, including alert->no alert
        public delegate void ExperimentRecoveryAlert(IExperimentSensor sensor);
        public delegate void ExperimentTransmittableAlert(IExperimentSensor sensor);
        public delegate void ExperimentSubjectChanged(IExperimentSensor sensor);


        public event ExperimentStatusChanged OnExperimentStatusChanged = delegate { };
        public event ExperimentRecoveryAlert OnExperimentRecoveryAlert = delegate { };
        public event ExperimentTransmittableAlert OnExperimentTransmittableAlert = delegate { };
        public event ExperimentSubjectChanged OnExperimentSubjectChanged = delegate { };
 

/******************************************************************************
 *                    Implementation Details
 ******************************************************************************/


        public SensorManager(Experiments.Science.OnboardScienceDataCache onboardCache)
        {
            sensorFactory = new SensorFactory(onboardCache);
            SubscribeVesselEvents();
            CreateSensorsForVessel();
        }



        ~SensorManager()
        {
            UnsubscribeVesselEvents();
        }


        public void UpdateSensorStates()
        {
            if (FlightGlobals.ActiveVessel != null && _sensorLoop != null)
                _sensorLoop.MoveNext(); // loop never ends so no need to check return value here
        }



        private void SubscribeVesselEvents()
        {
            GameEvents.onVesselWasModified.Add(OnVesselEvent);
            GameEvents.onVesselChange.Add(OnVesselEvent);
            GameEvents.onVesselDestroy.Add(OnVesselEvent);
        }


        private void UnsubscribeVesselEvents()
        {
            GameEvents.onVesselWasModified.Remove(OnVesselEvent);
            GameEvents.onVesselChange.Remove(OnVesselEvent);
            GameEvents.onVesselDestroy.Remove(OnVesselEvent);
        }



        public void CreateSensorsForVessel()
        {
            _sensors.Clear();

            Log.Verbose("SensorManager: Checking vessel for experiments");

            if (FlightGlobals.ActiveVessel == null)
            {
                Log.Verbose("aborted; no active vessel");
                return;
            }

            try
            {
                var modules = FlightGlobals.ActiveVessel.FindPartModulesImplementing<ModuleScienceExperiment>();
                Log.Normal("Scanned vessel and found {0} experiment modules", modules.Count);

                var experimentids = ResearchAndDevelopment.GetExperimentIDs();

                foreach (string expid in experimentids)
                {
                    IExperimentSensor sensor = sensorFactory.CreateSensor(expid, modules);
                    if (sensor == null)
                    {
                        Log.Error("Failed to create {0} sensor", expid);
                    }
                    else _sensors.Add(sensor);
                }
            }
            catch (Exception e)
            {
                Log.Error("An exception occurred while scanning the vessel for experiment modules: {0}", e);
            }

            _sensorLoop = CheckSensorStates();
        }



        private System.Collections.IEnumerator CheckSensorStates()
        {
            ExperimentSituations situation;

            while (true)
            {
                situation = ScienceUtil.GetExperimentSituation(FlightGlobals.ActiveVessel);

                foreach (var sensor in _sensors)
                {
                    CheckSensorForEvent(situation, sensor);

                    if (TimeWarp.CurrentRate < Settings.Instance.TimeWarpCheckThreshold)
                    {
                        yield return 0; // pause until next frame
                        situation = ScienceUtil.GetExperimentSituation(FlightGlobals.ActiveVessel); // otherwise situation will be increasingly out of date
                    }
                }

                yield return 0;
            }
        }



        private void CheckSensorForEvent(ExperimentSituations currentSituation, IExperimentSensor sensor)
        {
            var oldStatus = sensor.Status;
            var oldSubject = sensor.Subject;

            sensor.UpdateState(FlightGlobals.ActiveVessel.mainBody, currentSituation);

            if (oldStatus != sensor.Status)
                OnExperimentStatusChanged(oldStatus, sensor);

            if (IsFlagStateNewlySet(sensor.Status, oldStatus, SensorState.RecoveryAlert))
                OnExperimentRecoveryAlert(sensor);

            if (IsFlagStateNewlySet(sensor.Status, oldStatus, SensorState.TransmitAlert))
                OnExperimentTransmittableAlert(sensor);

            if (oldSubject != sensor.Subject)
                OnExperimentSubjectChanged(sensor);

        }


        private static bool IsFlagStateNewlySet(SensorState newState, SensorState oldState, SensorState flag)
        {
            return ((oldState & flag) == 0) && (newState & flag) != 0;
        }

       
        #region GameEvents


        private void OnVesselEvent(Vessel v)
        {
#if DEBUG
            Log.Debug("OnVesselEvent for {0}", v.vesselName);
#endif

            if (FlightGlobals.ActiveVessel == v)
            {
                CreateSensorsForVessel();
            }
#if DEBUG
            else
            {
                // just to be sure
                Log.Debug("SensorManager.OnVesselEvent: ignoring event for vessel {0}", v.vesselName);
            }
#endif
        }



        #endregion


        #region properties

#endregion
    }
}
