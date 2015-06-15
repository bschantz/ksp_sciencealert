﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ScienceAlert.Providers
{
    public interface IScienceExperimentModuleProvider
    {
        IEnumerable<ModuleScienceExperiment> Get();
    }
}