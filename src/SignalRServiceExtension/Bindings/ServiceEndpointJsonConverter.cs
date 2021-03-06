﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Azure.SignalR;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Extensions.SignalRService
{
    internal class ServiceEndpointJsonConverter : JsonConverter<ServiceEndpoint>
    {
        private const string FakeAccessKey = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        public override ServiceEndpoint ReadJson(JsonReader reader, Type objectType, ServiceEndpoint existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            return ToEqualServiceEndpoint(serializer.Deserialize<LiteServiceEndpoint>(reader));
        }

        public override void WriteJson(JsonWriter writer, ServiceEndpoint value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, ToLiteServiceEndpoint(value));
        }

        private LiteServiceEndpoint ToLiteServiceEndpoint(ServiceEndpoint e)
        {
            return new LiteServiceEndpoint
            {
                EndpointType = e.EndpointType,
                Name = e.Name,
                Endpoint = e.Endpoint,
                Online = e.Online
            };
        }

        private ServiceEndpoint ToEqualServiceEndpoint(LiteServiceEndpoint e)
        {
            var connectionString = $"Endpoint={e.Endpoint};AccessKey={FakeAccessKey};Version=1.0;";
            return new ServiceEndpoint(connectionString, e.EndpointType, e.Name);
        }
    }
}