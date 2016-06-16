﻿using ScienceAlert.Game;

namespace ScienceAlert.VesselContext.Experiments.Sensors.Rules
{
    // ReSharper disable once UnusedMember.Global
    public class RuleExperimentIsAvailableWhileVesselSituation : ScienceExperimentModuleTracker, ISensorRule
    {
        public RuleExperimentIsAvailableWhileVesselSituation(ScienceExperiment experiment, IVessel vessel)
            : base(experiment, vessel)
        {

        }

        public bool Passes()
        {
            return Experiment.IsAvailableWhile(Vessel.ExperimentSituation, Vessel.OrbitingBody.Body);
        }
    }
}