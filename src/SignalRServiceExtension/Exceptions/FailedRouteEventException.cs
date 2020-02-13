﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Extensions.SignalRService.Exceptions
{
    internal class FailedRouteEventException : SignalRBindingException
    {
        public FailedRouteEventException(string message) : base(message)
        {
        }
    }
}
