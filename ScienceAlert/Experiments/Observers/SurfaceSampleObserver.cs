﻿/******************************************************************************
                   Science Alert for Kerbal Space Program                    
 ******************************************************************************
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
 *****************************************************************************/
namespace ScienceAlert.Experiments.Observers
{
    /// <summary>
    /// This object works a bit like the EVA report observer: it checks if a particular experiment is available
    /// even though a ModuleScienceExperiment for that experiment doesn't exist. It's useful because the player
    /// technically has the capability to do this experiment at their fingertips already, just by going on EVA
    /// </summary>
    internal class SurfaceSampleObserver : EvaReportObserver
    {
        public SurfaceSampleObserver(Experiments.Data.ScienceDataCache cache, ProfileData.ExperimentSettings settings, BiomeFilter filter, ScanInterface scanInterface)
            : base(cache, settings, filter, scanInterface, "surfaceSample")
        {

        }

        public override bool IsReadyOnboard
        {
            get
            {
                if (FlightGlobals.ActiveVessel != null)
                {
                    if (FlightGlobals.ActiveVessel.isEVA)
                    {
                        return this.GetNextOnboardExperimentModule() != null;
                    }
                    else return Settings.Instance.CheckSurfaceSampleNotEva && base.IsReadyOnboard;

                }
                else return false;
            }
        }
    }
}
