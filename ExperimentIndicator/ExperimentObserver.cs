﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ExperimentIndicator
{
    using ScienceModuleList = List<ModuleScienceExperiment>;
    using StorageList = List<IScienceDataContainer>;
    //using TransmitterList = List<IScienceDataTransmitter>;
    


    /// <summary>
    /// Given an experiment, monitor conditions for the experiment.  If
    /// an experiment onboard is available and the conditions are right
    /// for the given filter, the experiment observer will indicate that
    /// the experiment is Available.
    /// </summary>
    internal class ExperimentObserver
    {
        private ScienceModuleList modules;              // all ModuleScienceExperiments onboard that represent our experiment
        protected ScienceExperiment experiment;           // The actual experiment that will be performed
        protected StorageList storage;                  // containers for science data
        //protected TransmitterList transmitters;         // Science transmitters, obviously
        protected MagicDataTransmitter magicTransmitter;    // MagicDataTransmitter keeps an eye on any queued data for the vessel



        public enum FilterMode
        {
            Unresearched = 0,                           // only light on subjects for which no science has been confirmed at all
            NotMaxed = 1,                               // light whenever the experiment subject isn't maxed
        }


/******************************************************************************
 *                    Implementation Details
 ******************************************************************************/


        public ExperimentObserver(string expid)
        {
            experiment = ResearchAndDevelopment.GetExperiment(expid);
            Rebuild();
        }



        ~ExperimentObserver()
        {

        }



        public virtual void Rebuild()
        {
            Log.Verbose("ExperimentObserver ({0}): rebuilding...", ExperimentTitle);
            modules = new ScienceModuleList();
            magicTransmitter = null;

            if (FlightGlobals.ActiveVessel == null)
                return;

            // todo: set filter on creation?
            Filter = FilterMode.Unresearched;

            // locate all ModuleScienceExperiments that implement this
            // experiment.  By keeping track of them ourselves, we don't
            // need to bother ExperimentIndicator with any details of
            // the inner workings of this object
            ScienceModuleList potentials = FlightGlobals.ActiveVessel.FindPartModulesImplementing<ModuleScienceExperiment>();

            foreach (var potential in potentials)
                if (potential.experimentID == experiment.id)
                    modules.Add(potential);

            RebuildStorageList();
            Log.Debug("Added ExperimentLight for experiment {0} (active vessel has {1} experiments of type)", experiment.id, modules.Count);
        }



        protected void RebuildStorageList()
        {
            storage = FlightGlobals.ActiveVessel.FindPartModulesImplementing<IScienceDataContainer>();

            Log.Debug("Locating PartModules that implement IScienceDataTransmitter ...");
            var transmitterList = FlightGlobals.ActiveVessel.FindPartModulesImplementing<IScienceDataTransmitter>();

            if (transmitterList.Count == 1)
            {
                // whoops, something's wrong.  If there's a real transmitter,
                // there will also be a magic transmitter.
                Log.Error("ExperimentObserver.RebuildStorageList - found only 1 transmitter!");
            } else
            {
                // ExperimentIndicator will handle the magic transmitter details
                // for us.  All we need to know is that there's definitely
                // at least one real and one fake transmitter onboard (or else
                // none of the above; vessel cannot transmit anyway)

                if (transmitterList.Any(tx => !(tx is MagicDataTransmitter)))
                {
                    Log.Debug("Onboard transmitter count: {0}; root part transmitters {1}", transmitterList.Count, FlightGlobals.ActiveVessel.rootPart.Modules.OfType<MagicDataTransmitter>().ToList().Count);
                    magicTransmitter = FlightGlobals.ActiveVessel.rootPart.Modules.OfType<MagicDataTransmitter>().First();
                }
                else magicTransmitter = null;
            }
        }



        /// <summary>
        /// As you might imagine, there are definitely cases where an experiment
        /// is "available" and no science is submitted, yet the data has already
        /// been collected.  For example, a vessel has two mystery goo containers.
        /// One is activated in a given situation.  That data is saved, not transmitted.
        /// Since there is another experiment available and technically no science
        /// has been collected for the experiment, should the indicator light for
        /// goo be on?  I say no.  That means we must account for any existing stored
        /// data in any container onboard the vessel.
        /// 
        /// Another note: when data is being transmitted, it is moved from a container
        /// into a transmitter module.  That's going to be a problem if we don't also
        /// include transmitters in our search for data; otherwise, as soon as the 
        /// player begins transmitting it looks like no data for the experiment is 
        /// onboard and the experiment will "become available" for a few seconds 
        /// until the data is submitted.
        ///    However, we encounter another problem here: there's no way to get at
        /// the queued ScienceData in the transmitter.  We can trick the game into
        /// always choosing our fake transmitter and get the data that way instead,
        /// though.  More detail in MagicDataTransmitter description.
        /// </summary>
        /// <param name="subjectid"></param>
        /// <param name="refData"></param>
        /// <returns></returns>
        protected bool FindStoredData(string subjectid, out ScienceData refData)
        {
            Log.Debug("Queued data count: {0}", magicTransmitter.QueuedData.Count);

            foreach (var container in storage)
                foreach (var data in container.GetData())
                    if (data.subjectID == subjectid)
                    {
                        refData = data;
                        return true;
                    }

            if (magicTransmitter != null)
                foreach (var data in magicTransmitter.QueuedData)
                    if (data.subjectID == subjectid)
                    {
                        refData = data;
                        Log.Warning("Found stored data in transmitter queue");
                        return true;
                    }

            refData = null;
            return false;
        }



        /// <summary>
        /// Returns true if the status just changed to available (so that
        /// ExperimentIndicator can play a sound when the experiment
        /// status changes)
        /// </summary>
        /// <returns></returns>
        public virtual bool UpdateStatus(ExperimentSituations experimentSituation)
        {
            if (FlightGlobals.ActiveVessel == null)
            {
                Available = false;
                return false;
            }

            //Log.Debug("Updating status for experiment {0}", ExperimentTitle);

            bool lastStatus = Available;

            // does this experiment even apply in the current situation?
            var vessel = FlightGlobals.ActiveVessel;

            if (IsReadyOnboard)
            {
                if (experiment.IsAvailableWhile(experimentSituation, vessel.mainBody))
                {
                    var biome = string.Empty;

                    // note: apparently simply providing the biome name whether its
                    // relevant or not will result in the biome being INCORRECTLY applied
                    // to the experiment id.  This causes all kinds of confusion because
                    // R&D will report incorrect science values based on the wrong id
                    //
                    // Supplying an empty string if the biome doesn't matter seems to work
                    if (experiment.BiomeIsRelevantWhile(experimentSituation))
                        biome = vessel.mainBody.BiomeMap.GetAtt(vessel.latitude * Mathf.Deg2Rad, vessel.longitude * Mathf.Deg2Rad).name;

                    var subject = ResearchAndDevelopment.GetExperimentSubject(experiment, experimentSituation, vessel.mainBody, biome);
                    ScienceData data;


                    switch (Filter)
                    {
                        case FilterMode.Unresearched:
                            // If there's a report ready to be transmitted, the experiment
                            // is hardly "Unresearched" in this situation
                            Available = !FindStoredData(subject.id, out data) && subject.science < 0.0005f;
                            //Log.Debug("    - Mode: Unresearched, result {0}, science {1}, id {2}", Available, subject.science, subject.id);
                            break;

                        case FilterMode.NotMaxed:
                            if (FindStoredData(subject.id, out data))
                            {
                                Available = subject.science + ResearchAndDevelopment.GetNextScienceValue(data.dataAmount, subject) < subject.scienceCap;
                            }
                            else Available = subject.science < subject.scienceCap;

                            //Log.Debug("    - Mode: NotMaxed, result {0}, science {1}, id {2}", Available, subject.science, subject.id);
                            break;
                    }

                }
                else
                {
                    // experiment isn't available under this situation
#if DEBUG
                    //if (GetNextOnboardExperimentModule())
                    //Log.Verbose("    - is onboard but not applicable in this situation {1}", ExperimentTitle, experimentSituation);
#endif
                    Available = false;
                }
            }
            else Available = false; // no experiments ready

            return Available != lastStatus && Available;
        }


        public virtual bool Deploy()
        {
            if (!Available)
            {
                Log.Error("Cannot deploy experiment {0}; Available = {1}", Available);
                return false;
            }

            if (FlightGlobals.ActiveVessel == null)
            {
                Log.Error("Deploy -- invalid active vessel");
                return false;
            }

            // find an unused science module and use it 
            //      note for crew reports: as long as a kerbal exists somewhere in the vessel hierarchy,
            //      crew reports are allowed from "empty" command modules as stock behaviour.  So we 
            //      needn't find a specific module to use
            var deployable = GetNextOnboardExperimentModule();

            if (deployable)
            {
                Log.Debug("Deploying experiment module on part {0}", deployable.part.ConstructID);
                deployable.DeployExperiment();
                return true;
            }

            // we should never reach this point if IsExperimentAvailableOnboard did
            // its job.  This would indicate we're not accounting for something about 
            // experiment states
            Log.Error("Logic problem: Did not deploy experiment, but we should have been able to.  Investigate {0}", ExperimentTitle);
            return false;
        }


        #region Properties
        private ModuleScienceExperiment GetNextOnboardExperimentModule()
        {
            foreach (var module in modules)
                if (!module.Deployed && !module.Inoperable)
                    return module;

            return null;

        }

        public virtual bool IsReadyOnboard
        {
            get
            {
                return GetNextOnboardExperimentModule() != null;
            }
        }



        public virtual bool Available
        {
            get;
            protected set;
        }



        public FilterMode Filter
        {
            get;
            set;
        }

        public string ExperimentTitle
        {
            get
            {
                return experiment.experimentTitle;
            }
        }

        public virtual int OnboardExperimentCount
        {
            get
            {
                return modules.Count;
            }
        }

        #endregion

        #region helpers


        #endregion
    }



    /// <summary>
    /// Eva report is a special kind of experiment.  As long as a Kerbal
    /// is aboard the active vessel, it's "available".  A ModuleScienceExperiment
    /// won't appear in the way the other science modules do for other 
    /// experiments though, so we'll be needing a special case to handle it.
    /// 
    /// To prevent duplicate reports, we take into account any stored experiment
    /// data as normal.
    /// </summary>
    internal class EvaReportObserver : ExperimentObserver
    {
        List<Part> crewableParts = new List<Part>();

        /// <summary>
        /// Constructor.  We already know exactly which kind of
        /// </summary>
        public EvaReportObserver()
            : base("evaReport")
        {

        }

        public override bool Deploy()
        {
            if (!Available || !IsReadyOnboard)
            {
                Log.Error("Cannot deploy eva experiment {0}; Available = {1}, Onboard = {2}", Available, IsReadyOnboard);
                return false;
            }

            if (FlightGlobals.ActiveVessel == null)
            {
                Log.Error("Deploy -- invalid active vessel");
                return false;
            }


            // the current vessel IS NOT an eva'ing Kerbal, so
            // find a kerbal and dump him into space
            if (!FlightGlobals.ActiveVessel.isEVA)
            {
                // You might think HighLogic.CurrentGame.CrewRoster.GetNextAvailableCrewMember
                // is a logical function to use.  Actually it's possible for it to
                // generate a crew member out of thin air and put it outside, so nope
                // 
                // luckily we can choose a specific Kerbal.  We'll do so by
                // finding the possibilities and then picking one totally at 
                // pseudorandom

                List<ProtoCrewMember> crewChoices = new List<ProtoCrewMember>();

                foreach (var crewable in crewableParts)
                    crewChoices.AddRange(crewable.protoModuleCrew);

                if (crewChoices.Count == 0)
                {
                    Log.Error("EvaReportObserver.Deploy - No crew choices available.  Check logic");
                    return false;
                }
                else
                {
                    Log.Debug("Choices of kerbal:");
                    foreach (var crew in crewChoices)
                        Log.Debug(" - {0}", crew.name);

                    var luckyKerbal = crewChoices[UnityEngine.Random.Range(0, crewChoices.Count - 1)];
                    Log.Debug("{0} is the lucky Kerbal.  Out the airlock with him!", luckyKerbal.name);

                    // out he goes!
                    bool success = FlightEVA.SpawnEVA(luckyKerbal.KerbalRef);

                    if (!success)
                    {
                        Log.Error("EvaReportObserver.Deploy - Did not successfully send {0} out the airlock.  Hatch might be blocked.", luckyKerbal.name);
                        return false;
                    }

                    // todo: schedule a coroutine to wait for it to exist and pop open
                    // the report?

                    return true;
                }
            }
            else
            {
                Log.Error("Um ... The vessel is already an EVA kerbal.  Don't eat glue");
                return true;
            }
        }



        /// <summary>
        /// Note: ExperimentIndicator will look out for vessel changes for
        /// us and call Rebuild() as necessary
        /// </summary>
        public override void Rebuild()
        {
            crewableParts.Clear();

            if (FlightGlobals.ActiveVessel == null)
            {
                Log.Debug("EvaReportObserver: active vessel null; observer will not function");
                return;
            }

            // cache any part that can hold crew, so we don't have to
            // wastefully go through the entire vessel part tree
            // when updating status
            foreach (var part in FlightGlobals.ActiveVessel.Parts)
                if (part.CrewCapacity > 0)
                    crewableParts.Add(part);

            RebuildStorageList();

        }



        /// <summary>
        /// The original UpdateStatus relies on ModuleScienceExperiments.
        /// If the active vessel isn't a kerbal on EVA, none will exist
        /// </summary>
        /// <returns></returns>
        public override bool UpdateStatus(ExperimentSituations experimentSituation)
        {
            Log.Debug("Updating status for experiment {0}", ExperimentTitle);

            bool lastStatus = Available;

            if (FlightGlobals.ActiveVessel != null)
                if (IsReadyOnboard)
                {
                    var vessel = FlightGlobals.ActiveVessel;

                    if (experiment.IsAvailableWhile(experimentSituation, vessel.mainBody))
                    {
                        var biome = string.Empty; // taking what we learned about biomes and situations before ;\

                        if (experiment.BiomeIsRelevantWhile(experimentSituation))
                            biome = vessel.mainBody.BiomeMap.GetAtt(vessel.latitude * Mathf.Deg2Rad, vessel.longitude * Mathf.Deg2Rad).name;

                        // there's kerbals, the experiment is runnable, everything
                        // looks good so far.  Let's just make sure there isn't already
                        // an evaReport of this kind stored
                        ScienceData data;
                        var subject = ResearchAndDevelopment.GetExperimentSubject(experiment, experimentSituation, vessel.mainBody, biome);


                        if (FindStoredData(subject.id, out data))
                        {
                            Log.Write("Found eva stored data for id {0}!", subject.id);
                            Available = false;
                        }
                        else
                        {
                            switch (Filter)
                            {
                                case FilterMode.Unresearched:
                                    Available = subject.science < 0.0005f;
                                    break;

                                case FilterMode.NotMaxed:
                                    Available = subject.science < subject.scienceCap;
                                    break;

                                default:
                                    Log.Error("Unrecognized filter mode!");
                                    Available = false;
                                    break;
                            }
                        }
                    }
                    else // I can't imagine any situation where evaReport wasn't technically allowed
                    {    // but who knows, maybe somebody is running a mod that changes it?
                        Log.Debug("Experiment {0} isn't available under experiment situation {1}", ExperimentTitle, experimentSituation);
                        Available = false;
                    }

                }
                else Available = false; // no kerbals onboard apparently


            // Only return true if the status just changed to "available"
            return Available != lastStatus && Available;
        }

        public override bool IsReadyOnboard
        {
            get
            {
                foreach (var crewable in crewableParts)
                    if (crewable.protoModuleCrew.Count > 0)
                        return true;
                return false;
            }
        }
    }
}
