﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReeperCommon.Logging;
using ReeperKSP.Extensions;
using strange.extensions.command.impl;
using ScienceAlert.SensorDefinitions;

namespace ScienceAlert.VesselContext.Experiments.Trigger
{
// ReSharper disable once ClassNeverInstantiated.Global
    class CommandCreateExperimentTriggers : Command
    {
        private readonly IEnumerable<SensorDefinition> _definitions;
        private readonly ITriggerFactory _triggerFactory;
        private readonly ITemporaryBindingFactory _bindingFactory;
        private static bool _loggedInvalidTriggers = false;

        public CommandCreateExperimentTriggers(
            ReadOnlyCollection<SensorDefinition> definitions, 
            ITriggerFactory triggerFactory,
            ITemporaryBindingFactory bindingFactory)
        {
            if (definitions == null) throw new ArgumentNullException("definitions");
            if (triggerFactory == null) throw new ArgumentNullException("triggerFactory");
            if (bindingFactory == null) throw new ArgumentNullException("bindingFactory");

            _definitions = definitions;
            _triggerFactory = triggerFactory;
            _bindingFactory = bindingFactory;
        }


        public override void Execute()
        {
            LogInvalidTriggers();

            var triggers = new List<ExperimentTrigger>();

            foreach (var triggerDefinition in _definitions)
            {
                try
                {
                    triggers.Add(CreateTrigger(triggerDefinition));
                }
                catch (Exception e)
                {
                    Log.Error("Failed to create trigger for " + triggerDefinition.Experiment.id + " due to: " + e);
                }
            }

            injectionBinder.Bind<IEnumerable<ExperimentTrigger>>().Bind<List<ExperimentTrigger>>().To(triggers);
            Log.Verbose("Created " + triggers.Count + " ExperimentTriggers");
        }


        private void LogInvalidTriggers()
        {
            if (_loggedInvalidTriggers) return;

            _loggedInvalidTriggers = true;

            var invalids = GetDefinitionsWithInvalidTriggerDefinitions().ToList();

            if (invalids.Any())
            {
                Log.Debug("Logging invalid triggers");

                invalids
                    .ForEach(sd =>
                            Log.Error("Can't create trigger for " + sd.Experiment.id + ": " +
                                      sd.TriggerDefinition.ToSafeString()));
            }
        }


        private IEnumerable<SensorDefinition> GetDefinitionsWithInvalidTriggerDefinitions()
        {
            return _definitions.Where(sd => !_triggerFactory.CanHandle(sd.TriggerDefinition));
        }


        private ExperimentTrigger CreateTrigger(SensorDefinition definition)
        {
            try
            {
                injectionBinder.Bind<ScienceExperiment>().To(definition.Experiment);

                return _triggerFactory.Build(definition.TriggerDefinition, _triggerFactory, injectionBinder,
                    _bindingFactory);
            }
            finally
            {
                injectionBinder.Unbind<ScienceExperiment>();
            }
        }
    }
}