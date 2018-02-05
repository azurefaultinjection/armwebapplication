﻿using System;
using System.Collections.Generic;
using System.Text;

namespace AzureChaos.Enums
{
    public enum VirtualMachineGroup
    {
        /// <summary>Standlone Virtual Machines</summary>
        VirtualMachines,

        /// <summary>Virtual Machines in Availability Sets</summary>
        AvailabilitySets,

        /// <summary>Virtual Machines in Scale Sets</summary>
        ScaleSets,

        /// <summary>Virtual Machines in Load Balancer</summary>
        LoadBalancer,

        /// <summary>Virtual Machines in Availability Zones</summary>
        AvailabilityZones
    }
}