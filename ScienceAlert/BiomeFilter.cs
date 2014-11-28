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
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ReeperCommon;

namespace ScienceAlert
{
    /// <summary>
    /// Filtering at the edges of the original biome maps can cause some
    /// inaccurate biome reporting. That's not good, because it'll trick
    /// experiment observers into believing they entered a new situation
    /// but only for a split second if the filtered color just happens
    /// to match any of the biomes.
    /// 
    /// This class will generate a "clean" version with no aliasing. The
    /// previous version simply queried the cleaned up version whenever
    /// it was available. However, while this avoids "bogus" results
    /// (where the alias color between shores and grasslands matches something
    /// that's incorrect, tundra for instance) it isn't very good at low
    /// altitudes.
    /// 
    /// In 1.1, I'm switching back to the stock GetAttr default function
    /// but we'll add a filter by checking against the clean map within
    /// a certain radius for its result. If we don't get any matches, the
    /// GetAttr value is probably invalid and should be discarded.
    /// </summary>
    public class BiomeFilter : MonoBehaviour
    {
        private const int HALF_SEARCH_DIMENSIONS = 2;    // box around the point on the biome map to
                                                         // use to verify biome map results


        // --------------------------------------------------------------------
        //    Members
        // --------------------------------------------------------------------

        private CelestialBody current;      // which CelestialBody we've got a cached biome map texture for
        private Texture2D projectedMap;     // this is the cleaned biome map of the current CelestialBody
        private System.Collections.IEnumerator projector;   // this coroutine constructs the projectedMap from current CelestialBody
        private const float COLOR_THRESHOLD = 0.005f;       // Maximum color difference for two colors to be considered the same

/******************************************************************************
 *                    Implementation Details
 ******************************************************************************/
        void Start()
        {
            GameEvents.onDominantBodyChange.Add(OnDominantBodyChanged);
            GameEvents.onVesselChange.Add(OnVesselChanged);
            ReprojectBiomeMap(FlightGlobals.currentMainBody);
        }

        void OnDestroy()
        {
            GameEvents.onVesselChange.Remove(OnVesselChanged);
            GameEvents.onDominantBodyChange.Remove(OnDominantBodyChanged);
        }


        public void Update()
        {
            if (projector != null)
                projector.MoveNext();
        }


        public bool GetCurrentBiome(out string biome)
        {
            biome = "N/A";

            if (FlightGlobals.ActiveVessel == null)
                return false;

            string possibleBiome = string.Empty;

            if (GetBiome(FlightGlobals.ActiveVessel.latitude * Mathf.Deg2Rad, FlightGlobals.ActiveVessel.longitude * Mathf.Deg2Rad, out possibleBiome))
            {
                // the biome we got is most likely good
                biome = possibleBiome;
                return true;
            }
            else
            {
                // the biome we got is not very accurate (e.g. polar ice caps in middle of kerbin grasslands and
                // such, due to the way the biome map is filtered).
                // we'll return it anyway; let the caller figure out what to do about it
                biome = possibleBiome;
                return false;
            }
        }



        public bool GetBiome(double latRad, double lonRad, out string biome)
        {
            biome = string.Empty;
            var vessel = FlightGlobals.ActiveVessel;

            if (vessel == null || vessel.mainBody.BiomeMap == null || vessel.mainBody.BiomeMap.Map == null)
                return true;

            // vessel.landedAt gets priority since there are some special
            // factors it will take into account for us; specifically, that
            // the vessel is on the launchpad, ksc, etc which are treated
            // as biomes even though they don't exist on the biome map.
            if (!string.IsNullOrEmpty(vessel.landedAt))
            {
                biome = Vessel.GetLandedAtString(vessel.landedAt);
                return true;
            }
            else
            {
                // use the stock function to get an initial possibility
                var possibleBiome = vessel.mainBody.BiomeMap.GetAtt(latRad, lonRad);

                // if the cleaned map is finished, let's test the validity
                // of the result by checking some surrounding areas
                if (!IsBusy)
                {
                    if (VerifyBiomeResult(latRad, lonRad, possibleBiome))
                    {
                        // looks good!
                        biome = possibleBiome.name;
                        return true;
                    }
                    else
                    {
#if DEBUG
                        Log.Error("Biome '{0}' is probably an error", possibleBiome.name);
#endif
                        
                        return false;
                    }
                }
                else
                {
                    // not finished building the cleaned map so we'll
                    // just use what we have
                    biome = possibleBiome.name;
                    return true;
                }
            }
        }

        private bool Similar(Color first, Color second)
        {
            return Mathf.Abs(first.r - second.r) < COLOR_THRESHOLD && Mathf.Abs(first.g - second.g) < COLOR_THRESHOLD && Mathf.Abs(first.b - second.b) < COLOR_THRESHOLD;
        }
        private bool VerifyBiomeResult(double lat, double lon, CBAttributeMap.MapAttribute target)
        {
            if (projectedMap == null)
            {
                Log.Debug("Cannot verify biome result; no projected map exists");
                return true; // we'll have to assume it's accurate since we can't prove otherwise
            }

            if (target == null || target.mapColor == null)
                return true; // this shouldn't happen 

            lon -= Mathf.PI * 0.5f;
            if (lon < 0d) lon += Mathf.PI * 2d;
            lon %= Mathf.PI * 2d;

            int x_center = (int)Math.Round(projectedMap.width * (float)(lon / (Mathf.PI * 2)), 0);
            int y_center = (int)Math.Round(projectedMap.height * ((float)(lat / Mathf.PI) + 0.5f), 0);

            for (int y = y_center - HALF_SEARCH_DIMENSIONS; y < y_center + HALF_SEARCH_DIMENSIONS; ++y)
            for (int x = x_center - HALF_SEARCH_DIMENSIONS; x < x_center + HALF_SEARCH_DIMENSIONS; ++x)
            {
                Color c = projectedMap.GetPixel(x, y);

#if DEBUG
                //Log.Write("Projected is {0}, comparing to {1}, comparison is {2}", c, target.mapColor, Similar(c, target.mapColor));
#endif
                if (Similar(c, target.mapColor))
                    return true; // we have a match, no need to look further
            }

            return false;
        }



        /// <summary>
        /// Exists solely to kick off the ReprojectMap function and
        /// overwrite any old version, if one happens to be in progress
        /// at the time.
        /// </summary>
        /// <param name="newBody"></param>
        private void ReprojectBiomeMap(CelestialBody newBody)
        {
            projector = ReprojectMap(newBody);
        }



        /// <summary>
        /// This function will run in short segments every Update() loop until
        /// it finishes the new projectedMap texture. Until then, the
        /// BiomeFilter should use the "normal" fallback method of querying
        /// the original biome map in the meantime.
        /// </summary>
        /// <param name="newBody"></param>
        /// <returns></returns>
        private System.Collections.IEnumerator ReprojectMap(CelestialBody newBody)
        {
            if (current == newBody)
            {
                projector = null;
                yield break;
            }

            if (newBody == null)
            {
                Log.Error("BiomeFilter.ReprojectMap failure: newBody is null!");
                projector = null;
                current = null;
                yield break;
            }

            Log.Debug("ScienceAlert.BiomeFilter: Reprojecting biome map for {0}", newBody.name);

#if DEBUG
            if (newBody != FlightGlobals.currentMainBody)
                Log.Error("Error: BiomeFilter.ReprojectBiomeMap was given wrong target! Given {0}, it should be {1}", newBody.name, FlightGlobals.currentMainBody.name);
#endif
            current = null;

            if (newBody.BiomeMap == null || newBody.BiomeMap.Map == null)
            {
                projectedMap = null;
                projector = null;
                yield break;
            }

            Texture2D projection = new Texture2D(newBody.BiomeMap.Map.width, newBody.BiomeMap.Map.height, TextureFormat.ARGB32, false);
            projection.filterMode = FilterMode.Point;


            yield return null;

            float timer = Time.realtimeSinceStartup;
            Color32[] pixels = projection.GetPixels32();

            for (int y = 0; y < projection.height; ++y)
            {
                for (int x = 0; x < projection.width; ++x)
                {
                    // convert x and y into uv coordinates
                    float u = (float)x / projection.width;
                    float v = (float)y / projection.height;

                    // convert uv coordinates into latitude and longitude
                    double lat = Math.PI * v - Math.PI * 0.5;
                    double lon = 2d * Math.PI * u + Math.PI * 0.5;

                    // set biome color in our clean texture
                    pixels[y * projection.width + x] = (Color32)newBody.BiomeMap.GetAtt(lat, lon).mapColor;
                }

                if (y % 5 == 0)
                {
                    Log.Performance("ScienceAlert.BiomeFilter: still working ({0} complete)", ((y * projection.width) / (float)pixels.Length).ToString("P"));
                    yield return null;
                }    
            }

            projection.SetPixels32(pixels);
            projection.Apply();

            Log.Debug("Finished projection; storing data");
#if DEBUG
            projection.SaveToDisk(string.Format("ScienceAlert/projected_{0}.png", newBody.name));

            var original = new Texture2D(newBody.BiomeMap.Map.width, newBody.BiomeMap.Map.height, TextureFormat.ARGB32, false);
            var origPixels = newBody.BiomeMap.Map.GetPixels32();
            original.SetPixels32(origPixels);
            original.Apply();

            original.SaveToDisk(string.Format("ScienceAlert/original_{0}.png", newBody.name));
#endif   
            Log.Verbose("Filtering biome map of {0} took {1} seconds", newBody.name, Time.realtimeSinceStartup - timer);

            current = newBody;
            projectedMap = projection;
            projector = null; // we're finished!
        }



        /// <summary>
        /// When the player enters a new orbit, this function will be invoked
        /// to generate an aliasing/filtering-free biome map texture to avoid
        /// inaccurate results.
        /// </summary>
        /// <param name="bodies"></param>
        private void OnDominantBodyChanged(GameEvents.FromToAction<CelestialBody, CelestialBody> bodies)
        {
            Log.Normal("BiomeFilter.OnDominantBodyChanged from {0} to {1}", bodies.from, bodies.to);
            ReprojectBiomeMap(bodies.to);
        }


        private void OnVesselChanged(Vessel v)
        {
            ReprojectBiomeMap(v.mainBody);
        }
        

        public bool IsBusy
        {
            get
            {
                return projector != null;
            }
        }
    }
}
