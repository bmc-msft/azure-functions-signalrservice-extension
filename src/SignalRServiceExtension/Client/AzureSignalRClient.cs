// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Azure.SignalR;
using Microsoft.Azure.SignalR.Management;

namespace Microsoft.Azure.WebJobs.Extensions.SignalRService
{
    /// <summary>
    /// AzureSignalRClient used for negotiation, publishing messages and managing group membership.
    /// It will be created for each function request.
    /// </summary>
    internal class AzureSignalRClient : IAzureSignalRSender
    {
        public const string AzureSignalRUserPrefix = "asrs.u.";

        private static readonly string[] SystemClaims =
        {
            "aud", // Audience claim, used by service to make sure token is matched with target resource.
            "exp", // Expiration time claims. A token is valid only before its expiration time.
            "iat", // Issued At claim. Added by default. It is not validated by service.
            "nbf"  // Not Before claim. Added by default. It is not validated by service.
        };

        private readonly IServiceManagerStore serviceManagerStore;
        private readonly string connectionStringKey;

        public string HubName { get; }

        internal AzureSignalRClient(IServiceManagerStore serviceManagerStore, string connectionStringKey, string hubName)
        {
            this.serviceManagerStore = serviceManagerStore;
            this.HubName = hubName;
            this.connectionStringKey = connectionStringKey;
        }

        public Task<SignalRConnectionInfo> GetClientConnectionInfoAsync(string userId, string idToken, string[] claimTypeList)
        {
            var customerClaims = GetCustomClaims(idToken, claimTypeList);
            return GetClientConnectionInfoAsync(userId, customerClaims);
        }

        public async Task<SignalRConnectionInfo> GetClientConnectionInfoAsync(string userId, IList<Claim> claims)
        {
            var serviceHubContext = await GetHubContextAsync();
            var negotiateResponse = await serviceHubContext.NegotiateAsync(null, userId, BuildJwtClaims(claims, AzureSignalRUserPrefix).ToList());
            return new SignalRConnectionInfo
            {
                Url = negotiateResponse.Url,
                AccessToken = negotiateResponse.AccessToken
            };
        }

        public IList<Claim> GetCustomClaims(string idToken, string[] claimTypeList)
        {
            var customClaims = new List<Claim>();

            if (idToken != null && claimTypeList != null && claimTypeList.Length > 0)
            {
                var jwtToken = new JwtSecurityTokenHandler().ReadJwtToken(idToken);
                foreach (var claim in jwtToken.Claims)
                {
                    if (claimTypeList.Contains(claim.Type))
                    {
                        customClaims.Add(claim);
                    }
                }
            }

            return customClaims;
        }

        public Task SendToAll(SignalRData data)
        {
            return InvokeAsync(data.Endpoints, hubContext => hubContext.Clients.All.SendCoreAsync(data.Target, data.Arguments));
        }

        public Task SendToConnection(string connectionId, SignalRData data)
        {
            return InvokeAsync(data.Endpoints, hubContext => hubContext.Clients.Client(connectionId).SendCoreAsync(data.Target, data.Arguments));
        }

        public Task SendToUser(string userId, SignalRData data)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException($"{nameof(userId)} cannot be null or empty");
            }
            return InvokeAsync(data.Endpoints, hubContext => hubContext.Clients.User(userId).SendCoreAsync(data.Target, data.Arguments));
        }

        public Task SendToGroup(string groupName, SignalRData data)
        {
            if (string.IsNullOrEmpty(groupName))
            {
                throw new ArgumentException($"{nameof(groupName)} cannot be null or empty");
            }
            return InvokeAsync(data.Endpoints, hubContext => hubContext.Clients.Group(groupName).SendCoreAsync(data.Target, data.Arguments));
        }

        public Task AddUserToGroup(SignalRGroupAction action)
        {
            var userId = action.UserId;
            var groupName = action.GroupName;
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException($"{nameof(userId)} cannot be null or empty");
            }
            if (string.IsNullOrEmpty(groupName))
            {
                throw new ArgumentException($"{nameof(groupName)} cannot be null or empty");
            }
            return InvokeAsync(action.Endpoints, hubContext => hubContext.UserGroups.AddToGroupAsync(userId, groupName));
        }

        public Task RemoveUserFromGroup(SignalRGroupAction action)
        {
            var userId = action.UserId;
            var groupName = action.GroupName;
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException($"{nameof(userId)} cannot be null or empty");
            }
            if (string.IsNullOrEmpty(groupName))
            {
                throw new ArgumentException($"{nameof(groupName)} cannot be null or empty");
            }
            return InvokeAsync(action.Endpoints, hubContext => hubContext.UserGroups.RemoveFromGroupAsync(userId, groupName));
        }

        public Task RemoveUserFromAllGroups(SignalRGroupAction action)
        {
            var userId = action.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentException($"{nameof(userId)} cannot be null or empty");
            }
            return InvokeAsync(action.Endpoints, hubContext => hubContext.UserGroups.RemoveFromAllGroupsAsync(userId));
        }

        public Task AddConnectionToGroup(SignalRGroupAction action)
        {
            var connectionId = action.ConnectionId;
            var groupName = action.GroupName;
            if (string.IsNullOrEmpty(connectionId))
            {
                throw new ArgumentException($"{nameof(connectionId)} cannot be null or empty");
            }
            if (string.IsNullOrEmpty(groupName))
            {
                throw new ArgumentException($"{nameof(groupName)} cannot be null or empty");
            }
            return InvokeAsync(action.Endpoints, hubContext => hubContext.Groups.AddToGroupAsync(connectionId, groupName));
        }

        public Task RemoveConnectionFromGroup(SignalRGroupAction action)
        {
            var connectionId = action.ConnectionId;
            var groupName = action.GroupName;
            if (string.IsNullOrEmpty(connectionId))
            {
                throw new ArgumentException($"{nameof(connectionId)} cannot be null or empty");
            }
            if (string.IsNullOrEmpty(groupName))
            {
                throw new ArgumentException($"{nameof(groupName)} cannot be null or empty");
            }
            return InvokeAsync(action.Endpoints, hubContext => hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName));
        }

        private static IEnumerable<Claim> BuildJwtClaims(IEnumerable<Claim> customerClaims, string prefix)
        {
            if (customerClaims != null)
            {
                foreach (var claim in customerClaims)
                {
                    // Add AzureSignalRUserPrefix if customer's claim name is duplicated with SignalR system claims.
                    // And split it when return from SignalR Service.
                    if (SystemClaims.Contains(claim.Type))
                    {
                        yield return new Claim(prefix + claim.Type, claim.Value);
                    }
                    else
                    {
                        yield return claim;
                    }
                }
            }
        }

        private async Task InvokeAsync(ServiceEndpoint[] endpoints, Func<IInternalServiceHubContext, Task> func)
        {
            var serviceHubContext = await GetHubContextAsync();
            var targetHubContext = endpoints == null ? serviceHubContext : serviceHubContext.WithEndpoints(endpoints);
            await func.Invoke(targetHubContext);
            await targetHubContext.DisposeAsync();
        }

        private async ValueTask<IInternalServiceHubContext> GetHubContextAsync() => (await serviceManagerStore.GetOrAddByConnectionStringKey(connectionStringKey).GetAsync(HubName)) as IInternalServiceHubContext;
    }
}